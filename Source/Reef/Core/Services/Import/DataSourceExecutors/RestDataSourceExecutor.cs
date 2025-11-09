using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import.DataSourceExecutors;

/// <summary>
/// Executor for fetching data from REST APIs
/// Supports pagination (cursor, offset/limit, page number), authentication, and retry logic
/// </summary>
public class RestDataSourceExecutor : IDataSourceExecutor
{
    public DataSourceType SourceType => DataSourceType.RestApi;

    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(RestDataSourceExecutor));
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public RestDataSourceExecutor(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Executes a REST API request and retrieves all rows through pagination
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
            Log.Information("Starting REST API import from {Uri}", sourceUri);

            var pageNumber = 1;
            var hasMore = true;
            var totalRowsFetched = 0;

            while (hasMore && totalRowsFetched < (config.MaxRows ?? int.MaxValue))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageUrl = BuildPageUrl(sourceUri, config, pageNumber);
                Log.Debug("Fetching page {Page} from {Url}", pageNumber, pageUrl);

                var rows = await FetchPageAsync(pageUrl, config, cancellationToken);

                if (rows.Count == 0)
                {
                    hasMore = false;
                    Log.Debug("No more rows returned from API");
                }
                else
                {
                    allRows.AddRange(rows);
                    totalRowsFetched += rows.Count;
                    Log.Debug("Fetched {Count} rows (total: {Total})", rows.Count, totalRowsFetched);

                    // Check if we've fetched enough rows
                    if (config.MaxRows != null && totalRowsFetched >= config.MaxRows)
                    {
                        allRows = allRows.Take(config.MaxRows.Value).ToList();
                        hasMore = false;
                    }

                    // Offset/limit pagination
                    if (config.PaginationType == PaginationType.Offset && rows.Count < (config.PageSize ?? 100))
                    {
                        hasMore = false;
                    }
                    // Cursor-based pagination
                    else if (config.PaginationType == PaginationType.Cursor)
                    {
                        config.Cursor = ExtractCursor(rows, config);
                        hasMore = !string.IsNullOrEmpty(config.Cursor);
                    }

                    pageNumber++;
                }
            }

            watch.Stop();
            Log.Information("REST API import completed: {Count} rows in {ElapsedMs}ms", allRows.Count, watch.ElapsedMilliseconds);

            return allRows;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("REST API import was cancelled after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "REST API import failed after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Validates that the REST API is accessible
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
            Log.Information("Validating REST API connection to {Uri}", sourceUri);

            var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            ApplyHeaders(request, config);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            watch.Stop();

            if (response.IsSuccessStatusCode)
            {
                Log.Information("REST API validation successful: {Status}", response.StatusCode);
                return new ValidationResult
                {
                    IsValid = true,
                    ResponseTimeMs = watch.ElapsedMilliseconds
                };
            }
            else
            {
                var errorMsg = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                Log.Warning("REST API validation failed: {Error}", errorMsg);
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = errorMsg,
                    ResponseTimeMs = watch.ElapsedMilliseconds
                };
            }
        }
        catch (HttpRequestException ex)
        {
            watch.Stop();
            var errorMsg = $"Connection error: {ex.Message}";
            Log.Error(ex, "REST API validation failed");
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
            Log.Error(ex, "REST API validation error");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
    }

    // ===== Helper Methods =====

    private async Task<List<Dictionary<string, object>>> FetchPageAsync(
        string pageUrl,
        RestSourceConfig config,
        CancellationToken cancellationToken)
    {
        int retryCount = 0;
        while (retryCount <= (config.MaxRetries ?? 3))
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                ApplyHeaders(request, config);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && retryCount < (config.MaxRetries ?? 3))
                    {
                        var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                        Log.Warning("Rate limited, retrying after {Delay}ms", retryAfter.TotalMilliseconds);
                        await Task.Delay(retryAfter, cancellationToken);
                        retryCount++;
                        continue;
                    }

                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return ParseResponse(content, config);
            }
            catch (HttpRequestException ex) when (retryCount < (config.MaxRetries ?? 3))
            {
                retryCount++;
                var delay = (int)Math.Pow(2, retryCount) * 1000;  // Exponential backoff
                Log.Warning(ex, "Request failed, retrying in {Delay}ms (attempt {Attempt}/{Max})", delay, retryCount + 1, (config.MaxRetries ?? 3) + 1);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new HttpRequestException($"Failed to fetch page after {retryCount} retries");
    }

    private void ApplyHeaders(HttpRequestMessage request, RestSourceConfig config)
    {
        // Add custom headers
        if (config.Headers != null)
        {
            foreach (var kvp in config.Headers)
            {
                request.Headers.Add(kvp.Key, kvp.Value);
            }
        }

        // Add Bearer token if provided
        if (!string.IsNullOrEmpty(config.BearerToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.BearerToken);
        }

        // Add User-Agent if not already present
        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.Add("User-Agent", "Reef-ImportService/1.0");
        }

        // Add timeout
        request.Headers.Add("Connection", "keep-alive");
    }

    private List<Dictionary<string, object>> ParseResponse(string content, RestSourceConfig config)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var rows = new List<Dictionary<string, object>>();

        // Navigate to the data array using JsonPath
        var dataElement = root;
        if (!string.IsNullOrEmpty(config.ResponseDataPath))
        {
            dataElement = NavigateJsonPath(root, config.ResponseDataPath);
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
            // Single object response
            rows.Add(JsonElementToDictionary(dataElement));
        }

        return rows;
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

    private string BuildPageUrl(string sourceUri, RestSourceConfig config, int pageNumber)
    {
        var url = sourceUri;

        if (config.PaginationType == PaginationType.Page)
        {
            var separator = url.Contains('?') ? '&' : '?';
            var pageParam = config.PageParamName ?? "page";
            url += $"{separator}{pageParam}={pageNumber}";

            if (config.PageSize.HasValue)
            {
                var sizeParam = config.PageSizeParamName ?? "pageSize";
                url += $"&{sizeParam}={config.PageSize.Value}";
            }
        }
        else if (config.PaginationType == PaginationType.Offset)
        {
            var separator = url.Contains('?') ? '&' : '?';
            var offset = (pageNumber - 1) * (config.PageSize ?? 100);
            var offsetParam = config.OffsetParamName ?? "offset";
            var limitParam = config.LimitParamName ?? "limit";
            url += $"{separator}{offsetParam}={offset}&{limitParam}={config.PageSize ?? 100}";
        }
        else if (config.PaginationType == PaginationType.Cursor && !string.IsNullOrEmpty(config.Cursor))
        {
            var separator = url.Contains('?') ? '&' : '?';
            var cursorParam = config.CursorParamName ?? "cursor";
            url += $"{separator}{cursorParam}={config.Cursor}";
        }

        return url;
    }

    private string? ExtractCursor(List<Dictionary<string, object>> rows, RestSourceConfig config)
    {
        if (rows.Count == 0 || string.IsNullOrEmpty(config.CursorFieldName))
            return null;

        var lastRow = rows.Last();
        if (lastRow.TryGetValue(config.CursorFieldName, out var cursor))
        {
            return cursor?.ToString();
        }

        return null;
    }

    private RestSourceConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return new RestSourceConfig();

        try
        {
            return JsonSerializer.Deserialize<RestSourceConfig>(configJson, _jsonOptions) ?? new RestSourceConfig();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse REST source configuration, using defaults");
            return new RestSourceConfig();
        }
    }
}

/// <summary>
/// Configuration for REST API data source
/// Stored as JSON in ImportProfile.SourceConfiguration
/// </summary>
public class RestSourceConfig
{
    [JsonPropertyName("paginationType")]
    public PaginationType PaginationType { get; set; } = PaginationType.Page;

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; set; } = 100;

    [JsonPropertyName("pageParamName")]
    public string? PageParamName { get; set; }

    [JsonPropertyName("offsetParamName")]
    public string? OffsetParamName { get; set; }

    [JsonPropertyName("limitParamName")]
    public string? LimitParamName { get; set; }

    [JsonPropertyName("pageSizeParamName")]
    public string? PageSizeParamName { get; set; }

    [JsonPropertyName("cursorParamName")]
    public string? CursorParamName { get; set; }

    [JsonPropertyName("cursorFieldName")]
    public string? CursorFieldName { get; set; }

    [JsonPropertyName("cursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; set; }

    [JsonPropertyName("bearerToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BearerToken { get; set; }

    [JsonPropertyName("headers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("responseDataPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseDataPath { get; set; }

    [JsonPropertyName("maxRetries")]
    public int? MaxRetries { get; set; } = 3;

    [JsonPropertyName("maxRows")]
    public int? MaxRows { get; set; }
}

public enum PaginationType
{
    Page,           // page=1, pageSize=100
    Offset,         // offset=0, limit=100
    Cursor          // cursor=abc123
}
