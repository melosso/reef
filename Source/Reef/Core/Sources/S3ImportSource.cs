using Amazon.S3;
using Amazon.S3.Model;
using Reef.Core.Models;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Sources;

/// <summary>
/// Reads files from an AWS S3 bucket (or S3-compatible storage).
/// Configuration is read from profile.SourceConfig JSON using the same shape as S3Config / DestinationConfiguration.
/// </summary>
public class S3ImportSource : IImportSource
{
    public async Task<List<ImportSourceFile>> FetchAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var cfg = ParseConfig(profile);
        var client = BuildClient(cfg);

        var files = await ResolveKeysAsync(client, cfg, profile.SourceFileSelection, profile.SourceFilePattern, ct);
        var result = new List<ImportSourceFile>();

        foreach (var key in files)
        {
            ct.ThrowIfCancellationRequested();
            var request = new GetObjectRequest { BucketName = cfg.BucketName, Key = key };
            using var response = await client.GetObjectAsync(request, ct);

            var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct);
            ms.Position = 0;

            result.Add(new ImportSourceFile
            {
                Identifier = key,
                Content = ms,
                SizeBytes = response.ContentLength,
                LastModified = response.LastModified
            });

            Log.Debug("S3ImportSource: downloaded s3://{Bucket}/{Key} ({Bytes} bytes)",
                cfg.BucketName, key, response.ContentLength);
        }

        return result;
    }

    public async Task<List<SourceFileInfo>> ListFilesAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var cfg = ParseConfig(profile);
        var client = BuildClient(cfg);
        var pattern = string.IsNullOrWhiteSpace(profile.SourceFilePattern) ? "*" : profile.SourceFilePattern;
        var prefix = cfg.Prefix ?? "";

        var request = new ListObjectsV2Request { BucketName = cfg.BucketName, Prefix = prefix };
        var infos = new List<SourceFileInfo>();

        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, ct);
            foreach (var obj in response.S3Objects)
            {
                var name = Path.GetFileName(obj.Key);
                if (MatchesPattern(name, pattern))
                {
                    infos.Add(new SourceFileInfo
                    {
                        Name = name,
                        Path = obj.Key,
                        SizeBytes = obj.Size,
                        LastModified = obj.LastModified
                    });
                }
            }
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        return infos.OrderByDescending(f => f.LastModified).ToList();
    }

    public Task<bool> ArchiveAsync(
        ImportProfile profile,
        string fileIdentifier,
        CancellationToken ct = default)
    {
        // S3 archive: rename by copying to archive prefix then deleting original
        return Task.FromResult(false); // Not implemented; callers handle gracefully
    }

    public async Task<(bool Success, string? Message)> TestAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        try
        {
            var cfg = ParseConfig(profile);
            if (string.IsNullOrWhiteSpace(cfg.BucketName))
                return (false, "BucketName is not configured");

            var client = BuildClient(cfg);
            var request = new ListObjectsV2Request { BucketName = cfg.BucketName, MaxKeys = 1 };
            var response = await client.ListObjectsV2Async(request, ct);
            return (true, $"Connected to bucket '{cfg.BucketName}'. Accessible.");
        }
        catch (Exception ex)
        {
            return (false, $"S3 test failed: {ex.Message}");
        }
    }

    // Helpers

    private async Task<List<string>> ResolveKeysAsync(
        AmazonS3Client client,
        S3SourceConfig cfg,
        string fileSelection,
        string? pattern,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ExactKey))
            return new List<string> { cfg.ExactKey };

        var glob = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
        var prefix = cfg.Prefix ?? "";
        var request = new ListObjectsV2Request { BucketName = cfg.BucketName, Prefix = prefix };

        var allObjects = new List<S3Object>();
        ListObjectsV2Response response;
        do
        {
            response = await client.ListObjectsV2Async(request, ct);
            allObjects.AddRange(response.S3Objects.Where(o =>
                MatchesPattern(Path.GetFileName(o.Key), glob)));
            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);

        if (allObjects.Count == 0) return new List<string>();

        return fileSelection switch
        {
            "All" => allObjects.Select(o => o.Key).ToList(),
            "Oldest" => new List<string> { allObjects.MinBy(o => o.LastModified)!.Key },
            _ => new List<string> { allObjects.MaxBy(o => o.LastModified)!.Key }
        };
    }

    private AmazonS3Client BuildClient(S3SourceConfig cfg)
    {
        var s3Config = new AmazonS3Config();

        if (!string.IsNullOrWhiteSpace(cfg.ServiceUrl))
        {
            s3Config.ServiceURL = cfg.ServiceUrl;
            s3Config.ForcePathStyle = true;
        }
        else if (!string.IsNullOrWhiteSpace(cfg.Region))
        {
            s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(cfg.Region);
        }

        if (!string.IsNullOrWhiteSpace(cfg.AccessKey) && !string.IsNullOrWhiteSpace(cfg.SecretKey))
            return new AmazonS3Client(cfg.AccessKey, cfg.SecretKey, s3Config);

        return new AmazonS3Client(s3Config);
    }

    private S3SourceConfig ParseConfig(ImportProfile profile)
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

        return new S3SourceConfig
        {
            BucketName = Get("bucketName", "BucketName") ?? "",
            Region = Get("region", "Region") ?? "",
            AccessKey = Get("accessKey", "AccessKey"),
            SecretKey = Get("secretKey", "SecretKey"),
            Prefix = Get("prefix", "Prefix") ?? "",
            ServiceUrl = Get("serviceUrl", "ServiceUrl"),
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

    private sealed class S3SourceConfig
    {
        public string BucketName { get; set; } = "";
        public string Region { get; set; } = "";
        public string? AccessKey { get; set; }
        public string? SecretKey { get; set; }
        public string? Prefix { get; set; }
        public string? ServiceUrl { get; set; }
        public string? ExactKey { get; set; }
    }
}
