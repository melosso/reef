using Reef.Core.Models;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Sources;

/// <summary>
/// Reads files from a network share (UNC path / SMB / NFS mount).
/// On Linux the path is assumed to already be mounted.
/// On Windows, the source path is accessed directly; Windows impersonation
/// is not required when the process already runs under a service account with
/// the necessary share permissions.
/// </summary>
public class NetworkShareImportSource : IImportSource
{
    public async Task<List<ImportSourceFile>> FetchAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var cfg = ParseConfig(profile);
        var basePath = GetBasePath(cfg, profile);

        var files = ResolveFiles(basePath, profile.SourceFilePath, profile.SourceFilePattern, profile.SourceFileSelection);
        var result = new List<ImportSourceFile>();

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                Log.Warning("NetworkShareImportSource: file not found: {Path}", path);
                continue;
            }

            var info = new FileInfo(path);
            var ms = new MemoryStream();
            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                await fs.CopyToAsync(ms, ct);
            }
            ms.Position = 0;

            result.Add(new ImportSourceFile
            {
                Identifier = path,
                Content = ms,
                SizeBytes = info.Length,
                LastModified = info.LastWriteTimeUtc
            });

            Log.Debug("NetworkShareImportSource: loaded {Path} ({Bytes} bytes)", path, info.Length);
        }

        return result;
    }

    public Task<List<SourceFileInfo>> ListFilesAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var cfg = ParseConfig(profile);
        var basePath = GetBasePath(cfg, profile);

        if (!Directory.Exists(basePath))
            return Task.FromResult(new List<SourceFileInfo>());

        var pattern = string.IsNullOrWhiteSpace(profile.SourceFilePattern) ? "*" : profile.SourceFilePattern;
        var infos = Directory.GetFiles(basePath, pattern, SearchOption.TopDirectoryOnly)
            .Select(p =>
            {
                var fi = new FileInfo(p);
                return new SourceFileInfo
                {
                    Name = fi.Name,
                    Path = p,
                    SizeBytes = fi.Length,
                    LastModified = fi.LastWriteTimeUtc
                };
            })
            .OrderByDescending(f => f.LastModified)
            .ToList();

        return Task.FromResult(infos);
    }

    public async Task<bool> ArchiveAsync(
        ImportProfile profile,
        string fileIdentifier,
        CancellationToken ct = default)
    {
        if (!File.Exists(fileIdentifier)) return false;

        var archivePath = profile.ArchivePath;
        if (string.IsNullOrWhiteSpace(archivePath))
            archivePath = Path.Combine(Path.GetDirectoryName(fileIdentifier)!, "archive");

        Directory.CreateDirectory(archivePath);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var newName = Path.GetFileNameWithoutExtension(fileIdentifier)
                      + $"_{timestamp}"
                      + Path.GetExtension(fileIdentifier);
        var dest = Path.Combine(archivePath, newName);

        await Task.Run(() => File.Move(fileIdentifier, dest, overwrite: true), ct);
        Log.Information("NetworkShareImportSource: archived {Source} → {Dest}", fileIdentifier, dest);
        return true;
    }

    public Task<(bool Success, string? Message)> TestAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var cfg = ParseConfig(profile);
        var basePath = GetBasePath(cfg, profile);

        if (string.IsNullOrWhiteSpace(basePath))
            return Task.FromResult<(bool, string?)>((false, "No UNC path configured"));

        if (!string.IsNullOrWhiteSpace(profile.SourceFilePath) && File.Exists(profile.SourceFilePath))
            return Task.FromResult<(bool, string?)>((true, $"File exists: {profile.SourceFilePath}"));

        if (Directory.Exists(basePath))
        {
            var pattern = string.IsNullOrWhiteSpace(profile.SourceFilePattern) ? "*" : profile.SourceFilePattern;
            var count = Directory.GetFiles(basePath, pattern, SearchOption.TopDirectoryOnly).Length;
            return Task.FromResult<(bool, string?)>((true, $"Share path accessible. {count} matching file(s) found."));
        }

        return Task.FromResult<(bool, string?)>((false, $"Path not accessible: {basePath}"));
    }

    // Helpers

    private static List<string> ResolveFiles(string basePath, string? exactPath, string? pattern, string fileSelection)
    {
        if (!string.IsNullOrWhiteSpace(exactPath) && fileSelection == "Exact")
            return new List<string> { exactPath };

        if (!Directory.Exists(basePath))
        {
            if (File.Exists(basePath)) return new List<string> { basePath };
            throw new InvalidOperationException($"Network share path not found: {basePath}");
        }

        var glob = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
        var matches = Directory.GetFiles(basePath, glob, SearchOption.TopDirectoryOnly);

        if (matches.Length == 0) return new List<string>();

        return fileSelection switch
        {
            "All" => matches.ToList(),
            "Oldest" => new List<string> { matches.MinBy(File.GetLastWriteTimeUtc)! },
            _ => new List<string> { matches.MaxBy(File.GetLastWriteTimeUtc)! }
        };
    }

    private static string GetBasePath(NetworkShareSourceConfig cfg, ImportProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SourceFilePath))
        {
            var fi = new FileInfo(profile.SourceFilePath);
            return fi.DirectoryName ?? profile.SourceFilePath;
        }

        var base_ = cfg.UncPath ?? cfg.BasePath ?? "";

        if (!string.IsNullOrWhiteSpace(cfg.SubFolder))
            base_ = Path.Combine(base_, cfg.SubFolder);

        return base_;
    }

    private NetworkShareSourceConfig ParseConfig(ImportProfile profile)
    {
        var json = profile.SourceConfig ?? "{}";
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? Get(params string[] keys)
        {
            foreach (var key in keys)
                if (root.TryGetProperty(key, out var el)) return el.GetString();
            return null;
        }

        return new NetworkShareSourceConfig
        {
            UncPath = Get("uncPath", "UncPath"),
            BasePath = Get("basePath", "BasePath"),
            SubFolder = Get("subFolder", "SubFolder"),
            Username = Get("username", "Username"),
            Password = Get("password", "Password"),
            Domain = Get("domain", "Domain")
        };
    }

    private sealed class NetworkShareSourceConfig
    {
        public string? UncPath { get; set; }
        public string? BasePath { get; set; }
        public string? SubFolder { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Domain { get; set; }
    }
}
