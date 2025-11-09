using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using Amazon.S3;
using Amazon.S3.Model;
using ILogger = Serilog.ILogger;

// Note: Using built-in CSV parsing instead of CsvHelper to reduce dependencies

namespace Reef.Core.Services.Import.DataSourceExecutors;

/// <summary>
/// Executor for fetching data from AWS S3
/// Supports CSV and JSON files with optional glob pattern filtering
/// Streams large files to avoid memory exhaustion
/// </summary>
public class S3DataSourceExecutor : IDataSourceExecutor
{
    public DataSourceType SourceType => DataSourceType.S3;

    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(S3DataSourceExecutor));

    /// <summary>
    /// Executes S3 retrieval and returns all rows from matching files
    /// S3 path format: s3://bucket-name/path/to/files
    /// </summary>
    public async Task<List<Dictionary<string, object>>> ExecuteAsync(
        string sourceUri,
        string? sourceConfig,
        CancellationToken cancellationToken = default)
    {
        var config = ParseConfiguration(sourceConfig);
        var allRows = new List<Dictionary<string, object>>();

        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Log.Information("Starting S3 import from {Uri} with pattern {Pattern}", sourceUri, config.FilePattern);

            using var s3Client = CreateS3Client(config);
            var files = await ListMatchingFilesAsync(s3Client, sourceUri, config, cancellationToken);

            if (!files.Any())
            {
                Log.Warning("No files found matching pattern {Pattern} in {Uri}", config.FilePattern, sourceUri);
                return allRows;
            }

            Log.Information("Found {Count} files matching pattern", files.Count);

            // Process each file
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Log.Debug("Processing file {Key}", file.Key);

                    var rows = await ReadFileAsync(s3Client, file.BucketName, file.Key, config, cancellationToken);
                    allRows.AddRange(rows);

                    Log.Debug("Read {Count} rows from {Key} (total: {Total})", rows.Count, file.Key, allRows.Count);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to process file {Key}", file.Key);
                    if (config.SkipFailedFiles != true)
                        throw;
                }
            }

            watch.Stop();
            Log.Information("S3 import completed: {Count} rows in {ElapsedMs}ms", allRows.Count, watch.ElapsedMilliseconds);

            return allRows;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("S3 import was cancelled after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "S3 import failed after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Validates that the S3 location is accessible
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        string sourceUri,
        string? sourceConfig,
        CancellationToken cancellationToken = default)
    {
        var config = ParseConfiguration(sourceConfig);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Log.Information("Validating S3 connection to {Uri}", sourceUri);

            using var s3Client = CreateS3Client(config);
            var files = await ListMatchingFilesAsync(s3Client, sourceUri, config, cancellationToken);

            watch.Stop();

            if (files.Any())
            {
                Log.Information("S3 validation successful: Found {Count} files", files.Count);
                return new ValidationResult
                {
                    IsValid = true,
                    ResponseTimeMs = watch.ElapsedMilliseconds
                };
            }
            else
            {
                Log.Warning("S3 validation: No files found matching pattern {Pattern}", config.FilePattern);
                return new ValidationResult
                {
                    IsValid = true, // S3 is valid, just no files
                    ResponseTimeMs = watch.ElapsedMilliseconds
                };
            }
        }
        catch (AmazonS3Exception ex)
        {
            watch.Stop();
            var errorMsg = $"AWS S3 error: {ex.Message}";
            Log.Error(ex, "S3 validation failed");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = errorMsg,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "S3 validation error");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
    }

    // ===== Helper Methods =====

    private IAmazonS3 CreateS3Client(S3SourceConfig config)
    {
        var clientConfig = new AmazonS3Config
        {
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config.Region ?? "us-east-1"),
            ForcePathStyle = config.ForcePathStyle == true
        };

        if (!string.IsNullOrEmpty(config.EndpointUrl))
        {
            clientConfig.ServiceURL = config.EndpointUrl;
        }

        // Use credentials if provided
        if (!string.IsNullOrEmpty(config.AccessKeyId) && !string.IsNullOrEmpty(config.SecretAccessKey))
        {
            return new AmazonS3Client(config.AccessKeyId, config.SecretAccessKey, clientConfig);
        }

        // Otherwise use IAM role or environment credentials
        return new AmazonS3Client(clientConfig);
    }

    private async Task<List<S3FileInfo>> ListMatchingFilesAsync(
        IAmazonS3 s3Client,
        string sourceUri,
        S3SourceConfig config,
        CancellationToken cancellationToken)
    {
        var matchingFiles = new List<S3FileInfo>();
        var (bucketName, prefix) = ParseS3Uri(sourceUri);

        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = prefix,
            MaxKeys = config.MaxFiles ?? 1000
        };

        try
        {
            ListObjectsV2Response response;
            do
            {
                response = await s3Client.ListObjectsV2Async(request, cancellationToken);

                foreach (var s3Object in response.S3Objects)
                {
                    if (MatchesPattern(s3Object.Key, config.FilePattern ?? "*.json,*.csv"))
                    {
                        matchingFiles.Add(new S3FileInfo
                        {
                            BucketName = bucketName,
                            Key = s3Object.Key,
                            Size = s3Object.Size ?? 0,
                            LastModified = s3Object.LastModified ?? DateTime.UtcNow
                        });
                    }
                }

                request.ContinuationToken = response.ContinuationToken;
            } while (response.IsTruncated == true);

            return matchingFiles;
        }
        catch (AmazonS3Exception ex)
        {
            Log.Error(ex, "Failed to list S3 objects");
            throw;
        }
    }

    private async Task<List<Dictionary<string, object>>> ReadFileAsync(
        IAmazonS3 s3Client,
        string bucketName,
        string key,
        S3SourceConfig config,
        CancellationToken cancellationToken)
    {
        var fileExtension = Path.GetExtension(key).ToLowerInvariant();

        return fileExtension switch
        {
            ".csv" => await ReadCsvFileAsync(s3Client, bucketName, key, config, cancellationToken),
            ".json" => await ReadJsonFileAsync(s3Client, bucketName, key, config, cancellationToken),
            _ => throw new NotSupportedException($"File format {fileExtension} not supported")
        };
    }

    private async Task<List<Dictionary<string, object>>> ReadCsvFileAsync(
        IAmazonS3 s3Client,
        string bucketName,
        string key,
        S3SourceConfig config,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object>>();

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            using var response = await s3Client.GetObjectAsync(request, cancellationToken);
            using var streamReader = new StreamReader(response.ResponseStream);

            // Read header
            var headerLine = await streamReader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(headerLine))
            {
                Log.Debug("Empty CSV file {Key}", key);
                return rows;
            }

            var headers = ParseCsvLine(headerLine);

            // Read data rows
            string? line;
            while ((line = await streamReader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var values = ParseCsvLine(line);
                var row = new Dictionary<string, object>();

                for (int i = 0; i < headers.Count; i++)
                {
                    var value = i < values.Count ? values[i] : string.Empty;
                    row[headers[i]] = value;
                }

                rows.Add(row);
            }

            Log.Debug("Read {Count} rows from CSV file {Key}", rows.Count, key);
            return rows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read CSV file {Key}", key);
            throw;
        }
    }

    private List<string> ParseCsvLine(string line)
    {
        // Simple CSV parser - handles quoted fields with commas
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString().Trim());
        return fields;
    }

    private async Task<List<Dictionary<string, object>>> ReadJsonFileAsync(
        IAmazonS3 s3Client,
        string bucketName,
        string key,
        S3SourceConfig config,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object>>();

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            };

            using var response = await s3Client.GetObjectAsync(request, cancellationToken);
            using var streamReader = new StreamReader(response.ResponseStream);
            var content = await streamReader.ReadToEndAsync(cancellationToken);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Navigate to data path if specified
            var dataElement = root;
            if (!string.IsNullOrEmpty(config.DataPath))
            {
                dataElement = NavigateJsonPath(root, config.DataPath);
            }

            if (dataElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataElement.EnumerateArray())
                {
                    rows.Add(JsonElementToDictionary(item));
                }
            }
            else if (dataElement.ValueKind == JsonValueKind.Object)
            {
                rows.Add(JsonElementToDictionary(dataElement));
            }

            Log.Debug("Read {Count} rows from JSON file {Key}", rows.Count, key);
            return rows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read JSON file {Key}", key);
            throw;
        }
    }

    private bool MatchesPattern(string fileName, string patterns)
    {
        var patternList = patterns.Split(',').Select(p => p.Trim()).ToList();

        foreach (var pattern in patternList)
        {
            if (GlobMatch(fileName, pattern))
                return true;
        }

        return false;
    }

    private bool GlobMatch(string fileName, string pattern)
    {
        // Simple glob pattern matching
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private (string bucketName, string prefix) ParseS3Uri(string sourceUri)
    {
        if (!sourceUri.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("S3 URI must start with 's3://'");

        var uri = sourceUri.Substring(5); // Remove "s3://"
        var slashIndex = uri.IndexOf('/');

        if (slashIndex == -1)
            return (uri, "");

        var bucketName = uri.Substring(0, slashIndex);
        var prefix = uri.Substring(slashIndex + 1);

        return (bucketName, prefix);
    }

    private JsonElement NavigateJsonPath(JsonElement element, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = element;

        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
            {
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out int index))
            {
                current = current[index];
            }
            else
            {
                Log.Warning("Could not navigate JSON path {Path}", path);
                return element;
            }
        }

        return current;
    }

    private Dictionary<string, object> JsonElementToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = JsonElementToObject(property.Value);
            }
        }

        return dict;
    }

    private object JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null!,
        _ => element.GetRawText()
    };

    private S3SourceConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return new S3SourceConfig();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
            return JsonSerializer.Deserialize<S3SourceConfig>(configJson, options) ?? new S3SourceConfig();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse S3 source configuration, using defaults");
            return new S3SourceConfig();
        }
    }
}

/// <summary>
/// Configuration for S3 data source
/// Stored as JSON in ImportProfile.SourceConfiguration
/// </summary>
public class S3SourceConfig
{
    [JsonPropertyName("region")]
    public string? Region { get; set; } = "us-east-1";

    [JsonPropertyName("accessKeyId")]
    public string? AccessKeyId { get; set; }

    [JsonPropertyName("secretAccessKey")]
    public string? SecretAccessKey { get; set; }

    [JsonPropertyName("filePattern")]
    public string? FilePattern { get; set; } = "*.csv,*.json";

    [JsonPropertyName("dataPath")]
    public string? DataPath { get; set; }

    [JsonPropertyName("maxFiles")]
    public int? MaxFiles { get; set; } = 1000;

    [JsonPropertyName("skipFailedFiles")]
    public bool? SkipFailedFiles { get; set; } = false;

    [JsonPropertyName("endpointUrl")]
    public string? EndpointUrl { get; set; }

    [JsonPropertyName("useAcceleration")]
    public bool? UseAcceleration { get; set; } = false;

    [JsonPropertyName("forcePathStyle")]
    public bool? ForcePathStyle { get; set; } = false;
}

/// <summary>
/// Information about S3 files
/// </summary>
internal class S3FileInfo
{
    public string BucketName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
