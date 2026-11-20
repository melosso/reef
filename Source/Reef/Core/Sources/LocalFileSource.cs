using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Sources;

/// <summary>
/// Reads files from the local filesystem.
/// Supports exact path, glob pattern matching, and file selection strategies.
/// </summary>
public class LocalFileSource : IImportSource
{
    public async Task<List<ImportSourceFile>> FetchAsync(
        ImportSourceOptions options,
        CancellationToken ct = default)
    {
        var files = await ResolveFilesAsync(options, ct);
        var result = new List<ImportSourceFile>();

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(path))
            {
                Log.Warning("LocalFileSource: file not found: {Path}", path);
                continue;
            }

            var info = new FileInfo(path);
            var stream = new MemoryStream();
            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                await fs.CopyToAsync(stream, ct);
            }

            stream.Position = 0;
            result.Add(new ImportSourceFile
            {
                Identifier = path,
                Content = stream,
                SizeBytes = info.Length,
                LastModified = info.LastWriteTimeUtc
            });

            Log.Debug("LocalFileSource: loaded {Path} ({Bytes} bytes)", path, info.Length);
        }

        return result;
    }

    public Task<List<SourceFileInfo>> ListFilesAsync(
        ImportSourceOptions options,
        CancellationToken ct = default)
    {
        var basePath = GetBasePath(options);
        if (!Directory.Exists(basePath))
        {
            return Task.FromResult(new List<SourceFileInfo>());
        }

        var pattern = string.IsNullOrWhiteSpace(options.SourceFilePattern) ? "*" : options.SourceFilePattern;
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
        ImportSourceOptions options,
        string fileIdentifier,
        CancellationToken ct = default)
    {
        if (!File.Exists(fileIdentifier)) return false;

        var archivePath = options.ArchivePath;
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            // Default: create an "archive" subfolder next to the source file
            archivePath = Path.Combine(Path.GetDirectoryName(fileIdentifier)!, "archive");
        }

        Directory.CreateDirectory(archivePath);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var newName = Path.GetFileNameWithoutExtension(fileIdentifier)
                      + $"_{timestamp}"
                      + Path.GetExtension(fileIdentifier);
        var dest = Path.Combine(archivePath, newName);

        await Task.Run(() => File.Move(fileIdentifier, dest, overwrite: true), ct);
        Log.Information("LocalFileSource: archived {Source} → {Dest}", fileIdentifier, dest);
        return true;
    }

    public Task<(bool Success, string? Message)> TestAsync(
        ImportSourceOptions options,
        CancellationToken ct = default)
    {
        var basePath = GetBasePath(options);

        if (string.IsNullOrWhiteSpace(basePath))
            return Task.FromResult<(bool, string?)>((false, "No file path or base directory configured"));

        if (!string.IsNullOrWhiteSpace(options.SourceFilePath) && File.Exists(options.SourceFilePath))
            return Task.FromResult<(bool, string?)>((true, $"File exists: {options.SourceFilePath}"));

        if (Directory.Exists(basePath))
        {
            var pattern = string.IsNullOrWhiteSpace(options.SourceFilePattern) ? "*" : options.SourceFilePattern;
            var count = Directory.GetFiles(basePath, pattern, SearchOption.TopDirectoryOnly).Length;
            return Task.FromResult<(bool, string?)>((true, $"Directory exists. {count} matching file(s) found."));
        }

        return Task.FromResult<(bool, string?)>((false, $"Path not found: {basePath}"));
    }

    // Helpers

    private async Task<List<string>> ResolveFilesAsync(ImportSourceOptions options, CancellationToken ct)
    {
        // Exact file path overrides everything
        if (!string.IsNullOrWhiteSpace(options.SourceFilePath) && options.SourceFileSelection == "Exact")
        {
            return new List<string> { options.SourceFilePath };
        }

        var basePath = GetBasePath(options);
        if (string.IsNullOrWhiteSpace(basePath))
            throw new InvalidOperationException("No source path configured for local file import");

        if (!Directory.Exists(basePath))
        {
            if (File.Exists(basePath))
                return new List<string> { basePath }; // basePath is actually a file path
            throw new InvalidOperationException($"Source directory not found: {basePath}");
        }

        var pattern = string.IsNullOrWhiteSpace(options.SourceFilePattern) ? "*" : options.SourceFilePattern;
        var matches = Directory.GetFiles(basePath, pattern, SearchOption.TopDirectoryOnly);

        if (matches.Length == 0)
        {
            Log.Warning("LocalFileSource: no files matching '{Pattern}' in {Directory}", pattern, basePath);
            return new List<string>();
        }

        return options.SourceFileSelection switch
        {
            "All" => matches.ToList(),
            "Oldest" => new List<string> { matches.MinBy(File.GetLastWriteTimeUtc)! },
            _ => new List<string> { matches.MaxBy(File.GetLastWriteTimeUtc)! } // Latest (default)
        };
    }

    private static string GetBasePath(ImportSourceOptions options)
    {
        // SourceFilePath may be an absolute file path or a directory
        if (!string.IsNullOrWhiteSpace(options.SourceFilePath))
        {
            if (Directory.Exists(options.SourceFilePath))
                return options.SourceFilePath;
            var fi = new FileInfo(options.SourceFilePath);
            return fi.DirectoryName ?? options.SourceFilePath;
        }

        // Try to extract basePath from SourceConfig JSON
        if (!string.IsNullOrWhiteSpace(options.SourceConfig))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(options.SourceConfig);
                if (doc.RootElement.TryGetProperty("path", out var pathEl) ||
                    doc.RootElement.TryGetProperty("basePath", out pathEl))
                {
                    return pathEl.GetString() ?? string.Empty;
                }
            }
            catch { /* ignore */ }
        }

        return string.Empty;
    }
}
