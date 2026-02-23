using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Sources;

/// <summary>
/// Fetches data from an HTTP/HTTPS REST API.
/// Supports GET/POST, various auth types, and offset/page/cursor/link pagination.
/// </summary>
public class HttpApiSource : IImportSource
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };

    public async Task<List<ImportSourceFile>> FetchAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var config = ParseConfig(profile);
        if (string.IsNullOrWhiteSpace(config.Url))
            throw new InvalidOperationException("HTTP source URL is not configured");

        byte[] data;

        if (profile.HttpPaginationEnabled && !string.IsNullOrWhiteSpace(profile.HttpPaginationConfig))
        {
            data = await FetchPaginatedAsync(config, profile, ct);
        }
        else
        {
            data = await FetchSingleAsync(config, profile.HttpDataRootPath, ct);
        }

        var stream = new MemoryStream(data);
        return new List<ImportSourceFile>
        {
            new ImportSourceFile
            {
                Identifier = config.Url!,
                Content = stream,
                SizeBytes = data.Length,
                ContentType = "application/json"
            }
        };
    }

    public Task<List<SourceFileInfo>> ListFilesAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        // HTTP sources don't have directory browsing; return the endpoint as a single item
        var config = ParseConfig(profile);
        return Task.FromResult(new List<SourceFileInfo>
        {
            new SourceFileInfo
            {
                Name = "API Response",
                Path = config.Url ?? "(not configured)"
            }
        });
    }

    public Task<bool> ArchiveAsync(
        ImportProfile profile,
        string fileIdentifier,
        CancellationToken ct = default)
    {
        // HTTP sources do not support archiving
        return Task.FromResult(false);
    }

    public async Task<(bool Success, string? Message)> TestAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var config = ParseConfig(profile);
        if (string.IsNullOrWhiteSpace(config.Url))
            return (false, "No URL configured");

        try
        {
            using var request = BuildRequest(config, config.Url!, null);
            request.Method = HttpMethod.Head; // try HEAD first to avoid downloading data
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
                return (true, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            // Fall back to GET for APIs that don't support HEAD
            using var getRequest = BuildRequest(config, config.Url!, null);
            using var getResponse = await _http.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            return (getResponse.IsSuccessStatusCode,
                $"HTTP {(int)getResponse.StatusCode} {getResponse.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return (false, $"HTTP test failed: {ex.Message}");
        }
    }

    // ── Private ──────────────────────────────────────────────

    private async Task<byte[]> FetchSingleAsync(HttpConfig config, string? dataRootPath, CancellationToken ct)
    {
        using var request = BuildRequest(config, config.Url!, config.Body);
        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);

        // If a data root path is configured, extract that sub-element and re-serialise
        if (!string.IsNullOrWhiteSpace(dataRootPath))
        {
            var json = Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);
            var element = NavigatePath(doc.RootElement, dataRootPath);
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(element));
        }

        return bytes;
    }

    private async Task<byte[]> FetchPaginatedAsync(HttpConfig config, ImportProfile profile, CancellationToken ct)
    {
        ImportPaginationConfig? pagCfg;
        try
        {
            pagCfg = JsonSerializer.Deserialize<ImportPaginationConfig>(profile.HttpPaginationConfig!);
        }
        catch
        {
            pagCfg = null;
        }

        if (pagCfg == null || pagCfg.Type == "None")
            return await FetchSingleAsync(config, profile.HttpDataRootPath, ct);

        var allRows = new List<object>();
        int page = 0;

        while (page < pagCfg.MaxPages)
        {
            ct.ThrowIfCancellationRequested();

            var url = BuildPagedUrl(config.Url!, pagCfg, page);
            using var request = BuildRequest(config, url, config.Body);
            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HTTP pagination page {page}: {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // Extract data array from response
            var dataElement = doc.RootElement;
            if (!string.IsNullOrWhiteSpace(profile.HttpDataRootPath))
            {
                dataElement = NavigatePath(dataElement, profile.HttpDataRootPath);
            }

            if (dataElement.ValueKind == JsonValueKind.Array)
            {
                var rows = dataElement.EnumerateArray().ToList();
                if (rows.Count == 0 && pagCfg.StopOnEmptyPage)
                {
                    Log.Debug("HttpApiSource: empty page at {Page}, stopping pagination", page);
                    break;
                }

                allRows.AddRange(rows.Select(r => (object)Parsers.JsonImportParser.JsonToDict(r)));
            }
            else if (dataElement.ValueKind == JsonValueKind.Object)
            {
                allRows.Add(Parsers.JsonImportParser.JsonToDict(dataElement));
            }

            // Handle cursor/link pagination
            if (pagCfg.Type == "Cursor" && !string.IsNullOrWhiteSpace(pagCfg.NextLinkPath))
            {
                var next = GetStringFromPath(doc.RootElement, pagCfg.NextLinkPath);
                if (string.IsNullOrWhiteSpace(next)) break;
                config = config with { Url = next }; // follow next URL
            }
            else if (pagCfg.Type == "Link" && !string.IsNullOrWhiteSpace(pagCfg.NextLinkPath))
            {
                var next = GetStringFromPath(doc.RootElement, pagCfg.NextLinkPath);
                if (string.IsNullOrWhiteSpace(next)) break;
                config = config with { Url = next };
            }

            page++;
        }

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(allRows));
    }

    private static string BuildPagedUrl(string baseUrl, ImportPaginationConfig cfg, int page)
    {
        var uri = new UriBuilder(baseUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        if (cfg.Type is "Offset" or "Page")
        {
            if (!string.IsNullOrWhiteSpace(cfg.LimitParam))
                query[cfg.LimitParam] = cfg.Limit.ToString();

            if (!string.IsNullOrWhiteSpace(cfg.PageParam))
            {
                var offset = cfg.Type == "Offset" ? page * cfg.Limit : page;
                query[cfg.PageParam] = offset.ToString();
            }
        }

        uri.Query = query.ToString();
        return uri.ToString();
    }

    private static string? GetStringFromPath(JsonElement root, string path)
    {
        try
        {
            var el = NavigatePath(root, path);
            return el.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement NavigatePath(JsonElement root, string path)
    {
        if (path.StartsWith("$.")) path = path[2..];
        else if (path.StartsWith("$")) path = path[1..];

        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.TryGetProperty(segment, out current))
                throw new KeyNotFoundException($"Path segment '{segment}' not found");
        }

        return current;
    }

    private static HttpRequestMessage BuildRequest(HttpConfig config, string url, string? body)
    {
        var method = config.Method?.ToUpperInvariant() switch
        {
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            _ => HttpMethod.Get
        };

        var req = new HttpRequestMessage(method, url);

        // Authentication
        if (config.AuthType?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == true
            && !string.IsNullOrWhiteSpace(config.AuthToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AuthToken);
        }
        else if (config.AuthType?.Equals("Basic", StringComparison.OrdinalIgnoreCase) == true
                 && !string.IsNullOrWhiteSpace(config.AuthToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", config.AuthToken);
        }

        // Custom headers
        if (config.Headers != null)
        {
            foreach (var (k, v) in config.Headers)
                req.Headers.TryAddWithoutValidation(k, v);
        }

        // Body
        if (!string.IsNullOrWhiteSpace(body) && method != HttpMethod.Get)
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return req;
    }

    private HttpConfig ParseConfig(ImportProfile profile)
    {
        var json = profile.SourceConfig ?? "{}";
        try
        {
            var cfg = JsonSerializer.Deserialize<HttpConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new HttpConfig();
            // Profile-level overrides
            if (!string.IsNullOrWhiteSpace(profile.HttpMethod)) cfg.Method = profile.HttpMethod;
            if (!string.IsNullOrWhiteSpace(profile.HttpBodyTemplate)) cfg.Body = profile.HttpBodyTemplate;
            return cfg;
        }
        catch
        {
            return new HttpConfig { Method = profile.HttpMethod ?? "GET" };
        }
    }

    private sealed record HttpConfig
    {
        public string? Url { get; init; }
        public string Method { get; set; } = "GET";
        public string? AuthType { get; init; }
        public string? AuthToken { get; init; }
        public Dictionary<string, string>? Headers { get; init; }
        public string? Body { get; set; }
    }
}
