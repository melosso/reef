// Source/Reef/Core/Destinations/HttpDestination.cs
// HTTP/HTTPS REST API destination for posting files

using Serilog;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Reef.Core.Destinations;

/// <summary>
/// Destination for posting files to HTTP/HTTPS REST APIs
/// Supports various authentication methods and custom headers
/// </summary>
public class HttpDestination : IDestination
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(300) // Default 5 minute timeout
    };

    /// <summary>
    /// Save file to HTTP/HTTPS REST API destination
    /// </summary>
    /// <param name="sourcePath">Path to the source file to upload</param>
    /// <param name="destinationConfig">JSON configuration for the destination</param>
    /// <returns>Tuple containing success status, final path/URL, and error message if failed</returns>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(
        string sourcePath,
        string destinationConfig)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return (false, null, $"Source file not found: {sourcePath}");
            }

            // Parse configuration
            var config = JsonSerializer.Deserialize<HttpConfig>(destinationConfig);
            if (config == null)
            {
                return (false, null, "Invalid HTTP destination configuration");
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.Url))
            {
                return (false, null, "URL is required for HTTP destination");
            }

            // Extract relative path from temp file (preserves subdirectories from filename template)
            var fileRelativePath = ExtractRelativePathFromTemp(sourcePath);
            var fileName = Path.GetFileName(fileRelativePath);
            var fileBytes = await File.ReadAllBytesAsync(sourcePath);

            // Send the file to the API
            var (success, message) = await PostFileAsync(config, fileName, fileBytes);

            if (success)
            {
                Log.Information("Successfully posted file to HTTP endpoint: {Url}", config.Url);
                // Return the HTTP response message as FinalPath (it will be captured as OutputMessage)
                return (true, message, null);
            }
            else
            {
                return (false, null, message);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HTTP SaveAsync failed");
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Post file to HTTP endpoint
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> PostFileAsync(
        HttpConfig config, 
        string fileName, 
        byte[] fileBytes)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, config.Url);

            // Add authentication
            AddAuthentication(request, config);

            // Add custom headers
            if (config.Headers != null)
            {
                foreach (var header in config.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Determine content type
            var contentType = config.ContentType ?? GetContentTypeFromFileName(fileName);

            // Build request content based on upload format
            switch (config.UploadFormat?.ToLower())
            {
                case "multipart":
                case "form-data":
                    // Multipart form data (file upload)
                    var multipartContent = new MultipartFormDataContent();
                    var fileContent = new ByteArrayContent(fileBytes);
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    
                    var fieldName = config.FileFieldName ?? "file";
                    multipartContent.Add(fileContent, fieldName, fileName);

                    // Add additional form fields if specified
                    if (config.FormFields != null)
                    {
                        foreach (var field in config.FormFields)
                        {
                            multipartContent.Add(new StringContent(field.Value), field.Key);
                        }
                    }

                    request.Content = multipartContent;
                    break;

                case "json":
                    // JSON payload - intelligent content handling
                    string jsonContent;
                    
                    // Try to detect and send as JSON directly
                    if (TryConvertToJson(fileName, fileBytes, contentType, config, out jsonContent!))
                    {
                        // Successfully converted to JSON (either already JSON or text-based format)
                        Log.Debug("Sending content as JSON payload ({Length} bytes)", jsonContent.Length);
                    }
                    else
                    {
                        // Binary/unknown format - wrap with base64 encoding
                        Log.Debug("Binary content detected, wrapping in JSON envelope with base64 encoding");
                        var jsonPayload = new Dictionary<string, object>
                        {
                            ["fileName"] = fileName,
                            ["fileData"] = Convert.ToBase64String(fileBytes),
                            ["contentType"] = contentType
                        };

                        // Add custom JSON fields if specified
                        if (config.JsonFields != null)
                        {
                            foreach (var field in config.JsonFields)
                            {
                                jsonPayload[field.Key] = field.Value;
                            }
                        }

                        jsonContent = JsonSerializer.Serialize(jsonPayload);
                    }

                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                    break;

                case "raw":
                case "binary":
                default:
                    // Raw binary upload
                    request.Content = new ByteArrayContent(fileBytes);
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    
                    // Add filename header if specified
                    if (!string.IsNullOrWhiteSpace(config.FileNameHeader))
                    {
                        request.Headers.TryAddWithoutValidation(config.FileNameHeader, fileName);
                    }
                    break;
            }

            // Set timeout if specified
            using var cts = new CancellationTokenSource(
                TimeSpan.FromSeconds(config.TimeoutSeconds ?? 300));

            // Send request
            var response = await _httpClient.SendAsync(request, cts.Token);

            // Check response
            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                Log.Debug("HTTP POST response: {StatusCode} - {Body}", 
                    response.StatusCode, responseBody);
                
                // Return success with response body as the message
                var successMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n\n{responseBody}";
                return (true, successMessage);
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                var errorMessage = $"HTTP {response.StatusCode}: {errorBody}";
                Log.Warning("HTTP POST failed: {Error}", errorMessage);
                return (false, errorMessage);
            }
        }
        catch (TaskCanceledException ex)
        {
            Log.Error(ex, "HTTP request timed out");
            return (false, "Request timed out");
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "HTTP request failed");
            return (false, $"HTTP request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error posting to HTTP endpoint");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Add authentication to the request
    /// </summary>
    private void AddAuthentication(HttpRequestMessage request, HttpConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.AuthType))
        {
            return;
        }

        switch (config.AuthType.ToLower())
        {
            case "bearer":
                if (!string.IsNullOrWhiteSpace(config.AuthToken))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", config.AuthToken);
                }
                break;

            case "basic":
                if (!string.IsNullOrWhiteSpace(config.Username) && 
                    !string.IsNullOrWhiteSpace(config.Password))
                {
                    var credentials = Convert.ToBase64String(
                        Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"));
                    request.Headers.Authorization = 
                        new AuthenticationHeaderValue("Basic", credentials);
                }
                break;

            case "apikey":
                if (!string.IsNullOrWhiteSpace(config.ApiKeyHeader) && 
                    !string.IsNullOrWhiteSpace(config.AuthToken))
                {
                    request.Headers.TryAddWithoutValidation(config.ApiKeyHeader, config.AuthToken);
                }
                break;

            case "token":
                if (!string.IsNullOrWhiteSpace(config.AuthToken))
                {
                    request.Headers.Authorization = 
                        new AuthenticationHeaderValue("Token", config.AuthToken);
                }
                break;

            case "custom":
                // Custom auth handled via Headers in config
                break;
        }
    }

    /// <summary>
    /// Get content type based on file extension
    /// </summary>
    private string GetContentTypeFromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".yaml" => "application/x-yaml",
            ".yml" => "application/x-yaml",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Intelligently convert file content to JSON format
    /// Handles JSON, XML, CSV, HTML, and other text formats
    /// </summary>
    private bool TryConvertToJson(string fileName, byte[] fileBytes, string contentType, 
        HttpConfig config, out string jsonString)
    {
        jsonString = string.Empty;

        try
        {
            // Try to decode as UTF-8 text
            var textContent = Encoding.UTF8.GetString(fileBytes);
            
            // Check if content is valid UTF-8 (no replacement characters)
            if (textContent.Contains('\uFFFD') && fileBytes.Length > 100)
            {
                // Likely binary content, not text
                Log.Debug("Binary content detected, not converting to JSON");
                return false;
            }

            // Trim whitespace for parsing
            var trimmedContent = textContent.Trim();
            
            if (string.IsNullOrEmpty(trimmedContent))
            {
                // Empty file - return empty JSON array
                jsonString = "[]";
                Log.Debug("Empty file, returning empty JSON array");
                return true;
            }

            // Strategy 1: Try to parse as JSON directly
            if (TryParseAsJson(trimmedContent, out jsonString!))
            {
                Log.Debug("File is already valid JSON ({Length} bytes)", jsonString.Length);
                return true;
            }

            // Strategy 2: Convert text-based formats to JSON
            if (TryConvertTextToJson(fileName, trimmedContent, contentType, config, out jsonString!))
            {
                Log.Debug("Converted text content to JSON ({Length} bytes)", jsonString.Length);
                return true;
            }

            // Not a supported text format
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to convert {FileName} to JSON", fileName);
            return false;
        }
    }

    /// <summary>
    /// Try to parse content as valid JSON
    /// </summary>
    private bool TryParseAsJson(string content, out string jsonString)
    {
        jsonString = content;
        
        try
        {
            // Validate it's valid JSON by parsing
            using var doc = JsonDocument.Parse(content);
            return true;
        }
        catch
        {
            jsonString = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Try to convert text-based formats (CSV, XML, HTML, etc.) to JSON
    /// </summary>
    private bool TryConvertTextToJson(string fileName, string content, string contentType, 
        HttpConfig config, out string jsonString)
    {
        jsonString = string.Empty;

        // For simplicity, wrap text content in a JSON object
        // This handles CSV, XML, HTML, and other text formats
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        // Create a JSON wrapper with metadata and content
        var wrapper = new Dictionary<string, object>
        {
            ["fileName"] = fileName,
            ["contentType"] = contentType,
            ["format"] = extension.TrimStart('.').ToUpper(),
            ["content"] = content,
            ["size"] = content.Length
        };

        // Add custom JSON fields if specified
        if (config.JsonFields != null)
        {
            foreach (var field in config.JsonFields)
            {
                wrapper[field.Key] = field.Value;
            }
        }

        jsonString = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions 
        { 
            WriteIndented = false 
        });
        
        return true;
    }

    /// <summary>
    /// Extract relative path from temp file path
    /// E.g., "C:\Temp\1\csv\file.csv" -> "csv\file.csv" (strips process folder)
    /// E.g., "C:\Temp\reef_splits\orders\order_A.csv" -> "orders\order_A.csv"
    /// </summary>
    private string ExtractRelativePathFromTemp(string sourcePath)
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var tempPathNormalized = Path.GetFullPath(tempPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Handle reef_splits subdirectory
            var reefSplitsPath = Path.Combine(tempPath, "reef_splits");
            if (sourcePath.StartsWith(reefSplitsPath, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath.Substring(reefSplitsPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            // Handle regular temp files
            if (sourcePath.StartsWith(tempPathNormalized, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = sourcePath.Substring(tempPathNormalized.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Windows .NET temp path often has process-specific folders like "\1\", "\2\", etc.
                // Strip the first folder if it's a single digit (process isolation folder)
                var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && parts[0].Length <= 2 && int.TryParse(parts[0], out _))
                {
                    // Skip the first part (process folder) and rejoin
                    return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1));
                }

                return relativePath;
            }

            // Fallback: just return the filename
            return Path.GetFileName(sourcePath);
        }
        catch
        {
            // If anything goes wrong, fall back to just the filename
            return Path.GetFileName(sourcePath);
        }
    }

    /// <summary>
    /// Test HTTP connection
    /// </summary>
    public async Task<bool> TestConnectionAsync(HttpConfig config)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(config.Url))
            {
                return false;
            }

            // Create a test request
            var testData = Encoding.UTF8.GetBytes("test");
            var result = await PostFileAsync(config, "test.txt", testData);
            
            return result.Success;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HTTP connection test failed");
            return false;
        }
    }
}

/// <summary>
/// Configuration for HTTP destination
/// </summary>
public class HttpConfig
{
    /// <summary>
    /// Target URL for the HTTP POST request
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// Upload format: "multipart" (form-data), "json", "raw"/"binary"
    /// Default: "raw"
    /// </summary>
    public string? UploadFormat { get; set; } = "raw";

    /// <summary>
    /// Content type for the file (auto-detected if not specified)
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Authentication type: "bearer", "basic", "apikey", "token", "custom", or null for none
    /// </summary>
    public string? AuthType { get; set; }

    /// <summary>
    /// Authentication token (for bearer/token/apikey auth)
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Username (for basic auth)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password (for basic auth)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// API key header name (for apikey auth, e.g., "X-API-Key")
    /// </summary>
    public string? ApiKeyHeader { get; set; }

    /// <summary>
    /// Custom HTTP headers
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Field name for file in multipart form (default: "file")
    /// </summary>
    public string? FileFieldName { get; set; }

    /// <summary>
    /// Additional form fields for multipart upload
    /// </summary>
    public Dictionary<string, string>? FormFields { get; set; }

    /// <summary>
    /// Additional JSON fields for JSON upload
    /// </summary>
    public Dictionary<string, object>? JsonFields { get; set; }

    /// <summary>
    /// Header name to send the filename (for raw/binary upload)
    /// </summary>
    public string? FileNameHeader { get; set; }

    /// <summary>
    /// Request timeout in seconds (default: 300)
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}
