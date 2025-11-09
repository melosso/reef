using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import.DataSourceExecutors;

/// <summary>
/// Executor for fetching data from FTP servers
/// Supports CSV and JSON files with directory filtering
/// Note: Full SFTP support (SSH.NET) can be added in Phase 3/4
/// </summary>
public class FtpDataSourceExecutor : IDataSourceExecutor
{
    public DataSourceType SourceType => DataSourceType.Ftp;

    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(FtpDataSourceExecutor));
    private static readonly HttpClient SharedHttpClient = new();

    /// <summary>
    /// Executes FTP retrieval and returns all rows from matching files
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
            Log.Information("Starting FTP import from {Uri}", sourceUri);

            // Parse URI to get host, port, path
            var uri = new Uri(sourceUri);
            var ftpUri = BuildFtpUri(uri, config);

            // List and download files
            var files = await ListFilesAsync(ftpUri, config, cancellationToken);

            if (!files.Any())
            {
                Log.Warning("No files found at {Uri}", sourceUri);
                return allRows;
            }

            Log.Information("Found {Count} matching files", files.Count);

            // Process each file
            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Log.Debug("Processing file {Path}", filePath);

                    var fileUri = new Uri(ftpUri, filePath);
                    var rows = await ReadFileAsync(fileUri, config, cancellationToken);
                    allRows.AddRange(rows);

                    Log.Debug("Read {Count} rows from {Path} (total: {Total})", rows.Count, filePath, allRows.Count);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to process file {Path}", filePath);
                    if (config.SkipFailedFiles != true)
                        throw;
                }
            }

            watch.Stop();
            Log.Information("FTP import completed: {Count} rows in {ElapsedMs}ms", allRows.Count, watch.ElapsedMilliseconds);

            return allRows;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("FTP import was cancelled after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "FTP import failed after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Validates that the FTP server is accessible
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
            Log.Information("Validating FTP connection to {Uri}", sourceUri);

            var uri = new Uri(sourceUri);
            var ftpUri = BuildFtpUri(uri, config);

            // Try to list root directory
            using var request = new HttpRequestMessage(HttpMethod.Get, ftpUri);
            SetupFtpCredentials(request, config);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            watch.Stop();
            Log.Information("FTP validation successful");

            return new ValidationResult
            {
                IsValid = true,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "FTP validation error");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
    }

    // ===== Helper Methods =====

    private Uri BuildFtpUri(Uri sourceUri, FtpSourceConfig config)
    {
        var scheme = config.UseSftp == true ? "sftp" : "ftp";
        var port = sourceUri.Port > 0 ? sourceUri.Port : (config.UseSftp == true ? 22 : 21);
        var host = sourceUri.Host;
        var path = sourceUri.PathAndQuery;

        return new Uri($"{scheme}://{host}:{port}{path}");
    }

    private async Task<List<string>> ListFilesAsync(
        Uri ftpUri,
        FtpSourceConfig config,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();

        try
        {
            // Download directory listing
            var listingUri = new Uri(ftpUri, "./");
            using var request = new HttpRequestMessage(HttpMethod.Get, listingUri);
            SetupFtpCredentials(request, config);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var listing = Encoding.UTF8.GetString(data);

            // Parse FTP directory listing (simplified)
            var lines = listing.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fileName = ExtractFileNameFromFtpListing(line);
                if (!string.IsNullOrEmpty(fileName) && MatchesPattern(fileName, config.FilePattern ?? "*.csv,*.json"))
                {
                    files.Add(fileName);
                }
            }

            return files;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to list FTP files");
            throw;
        }
    }

    private async Task<List<Dictionary<string, object>>> ReadFileAsync(
        Uri fileUri,
        FtpSourceConfig config,
        CancellationToken cancellationToken)
    {
        var fileExtension = Path.GetExtension(fileUri.LocalPath).ToLowerInvariant();

        return fileExtension switch
        {
            ".csv" => await ReadCsvFileAsync(fileUri, config, cancellationToken),
            ".json" => await ReadJsonFileAsync(fileUri, config, cancellationToken),
            _ => throw new NotSupportedException($"File format {fileExtension} not supported")
        };
    }

    private async Task<List<Dictionary<string, object>>> ReadCsvFileAsync(
        Uri fileUri,
        FtpSourceConfig config,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object>>();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, fileUri);
            SetupFtpCredentials(request, config);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            using var stream = new MemoryStream(data);
            using var streamReader = new StreamReader(stream);

            // Read header
            var headerLine = await streamReader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(headerLine))
                return rows;

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

            Log.Debug("Read {Count} rows from CSV file {Path}", rows.Count, fileUri.LocalPath);
            return rows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read CSV file {Path}", fileUri.LocalPath);
            throw;
        }
    }

    private async Task<List<Dictionary<string, object>>> ReadJsonFileAsync(
        Uri fileUri,
        FtpSourceConfig config,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object>>();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, fileUri);
            SetupFtpCredentials(request, config);

            using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            using var stream = new MemoryStream(data);
            using var streamReader = new StreamReader(stream);
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

            Log.Debug("Read {Count} rows from JSON file {Path}", rows.Count, fileUri.LocalPath);
            return rows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read JSON file {Path}", fileUri.LocalPath);
            throw;
        }
    }

    private string ExtractFileNameFromFtpListing(string line)
    {
        // Simple extraction - last token in FTP listing
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : string.Empty;
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
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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

    private void SetupFtpCredentials(HttpRequestMessage request, FtpSourceConfig config)
    {
        if (!string.IsNullOrEmpty(config.Username))
        {
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{config.Username}:{config.Password ?? string.Empty}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
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

    private FtpSourceConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return new FtpSourceConfig();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
            return JsonSerializer.Deserialize<FtpSourceConfig>(configJson, options) ?? new FtpSourceConfig();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse FTP source configuration, using defaults");
            return new FtpSourceConfig();
        }
    }
}

/// <summary>
/// Configuration for FTP data source
/// Stored as JSON in ImportProfile.SourceConfiguration
/// </summary>
public class FtpSourceConfig
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("filePattern")]
    public string? FilePattern { get; set; } = "*.csv,*.json";

    [JsonPropertyName("dataPath")]
    public string? DataPath { get; set; }

    [JsonPropertyName("skipFailedFiles")]
    public bool? SkipFailedFiles { get; set; } = false;

    [JsonPropertyName("useSftp")]
    public bool? UseSftp { get; set; } = false;
}
