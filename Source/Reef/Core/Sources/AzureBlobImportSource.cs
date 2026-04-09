using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Reef.Core.Models;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Sources;

/// <summary>
/// Reads files from an Azure Blob Storage container.
/// Configuration is read from profile.SourceConfig JSON using the same shape as AzureBlobConfig.
/// </summary>
public class AzureBlobImportSource : IImportSource
{
    public async Task<List<ImportSourceFile>> FetchAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var cfg = ParseConfig(profile);
        var container = new BlobContainerClient(cfg.ConnectionString, cfg.ContainerName);

        var keys = await ResolveKeysAsync(container, cfg, profile.SourceFileSelection, profile.SourceFilePattern, ct);
        var result = new List<ImportSourceFile>();

        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            var blob = container.GetBlobClient(key);
            var ms = new MemoryStream();
            var download = await blob.DownloadToAsync(ms, ct);
            ms.Position = 0;

            var props = (await blob.GetPropertiesAsync(cancellationToken: ct)).Value;

            result.Add(new ImportSourceFile
            {
                Identifier = key,
                Content = ms,
                SizeBytes = props.ContentLength,
                LastModified = props.LastModified.UtcDateTime
            });

            Log.Debug("AzureBlobImportSource: downloaded {Container}/{Key} ({Bytes} bytes)",
                cfg.ContainerName, key, props.ContentLength);
        }

        return result;
    }

    public async Task<List<SourceFileInfo>> ListFilesAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var cfg = ParseConfig(profile);
        var container = new BlobContainerClient(cfg.ConnectionString, cfg.ContainerName);
        var pattern = string.IsNullOrWhiteSpace(profile.SourceFilePattern) ? "*" : profile.SourceFilePattern;
        var prefix = cfg.Prefix ?? "";

        var infos = new List<SourceFileInfo>();
        await foreach (var item in container.GetBlobsAsync(BlobTraits.Metadata, BlobStates.All, prefix, ct))
        {
            var name = Path.GetFileName(item.Name);
            if (MatchesPattern(name, pattern))
            {
                infos.Add(new SourceFileInfo
                {
                    Name = name,
                    Path = item.Name,
                    SizeBytes = item.Properties.ContentLength,
                    LastModified = item.Properties.LastModified?.UtcDateTime
                });
            }
        }

        return infos.OrderByDescending(f => f.LastModified).ToList();
    }

    public Task<bool> ArchiveAsync(
        ImportProfile profile,
        string fileIdentifier,
        CancellationToken ct = default) => Task.FromResult(false);

    public async Task<(bool Success, string? Message)> TestAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        try
        {
            var cfg = ParseConfig(profile);
            if (string.IsNullOrWhiteSpace(cfg.ConnectionString))
                return (false, "ConnectionString is not configured");

            var container = new BlobContainerClient(cfg.ConnectionString, cfg.ContainerName);
            var exists = await container.ExistsAsync(ct);
            return exists
                ? (true, $"Container '{cfg.ContainerName}' is accessible.")
                : (false, $"Container '{cfg.ContainerName}' does not exist.");
        }
        catch (Exception ex)
        {
            return (false, $"Azure Blob test failed: {ex.Message}");
        }
    }

    // Helpers

    private async Task<List<string>> ResolveKeysAsync(
        BlobContainerClient container,
        AzureBlobSourceConfig cfg,
        string fileSelection,
        string? pattern,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ExactKey))
            return new List<string> { cfg.ExactKey };

        var glob = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
        var prefix = cfg.Prefix ?? "";

        var items = new List<(string Key, DateTimeOffset? LastModified)>();
        await foreach (var item in container.GetBlobsAsync(BlobTraits.Metadata, BlobStates.All, prefix, ct))
        {
            if (MatchesPattern(Path.GetFileName(item.Name), glob))
                items.Add((item.Name, item.Properties.LastModified));
        }

        if (items.Count == 0) return new List<string>();

        return fileSelection switch
        {
            "All" => items.Select(i => i.Key).ToList(),
            "Oldest" => new List<string> { items.MinBy(i => i.LastModified)!.Key },
            _ => new List<string> { items.MaxBy(i => i.LastModified)!.Key }
        };
    }

    private AzureBlobSourceConfig ParseConfig(ImportProfile profile)
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

        return new AzureBlobSourceConfig
        {
            ConnectionString = Get("connectionString", "ConnectionString") ?? "",
            ContainerName = Get("container", "Container", "containerName", "ContainerName") ?? "",
            Prefix = Get("path", "Path", "blobPrefix", "BlobPrefix") ?? "",
            ExactKey = profile.SourceFilePath
        };
    }

    private static bool MatchesPattern(string filename, string pattern)
    {
        if (pattern == "*") return true;
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(filename, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private sealed class AzureBlobSourceConfig
    {
        public string ConnectionString { get; set; } = "";
        public string ContainerName { get; set; } = "";
        public string? Prefix { get; set; }
        public string? ExactKey { get; set; }
    }
}
