using Serilog;
using System.Text.Json;

namespace Reef.Core.Formatters;

/// <summary>
/// JSON formatter for query results
/// Outputs data as a formatted JSON array with proper type handling
/// </summary>
public class JsonFormatter : IFormatter
{
    /// <summary>
    /// Format data as JSON and write to file
    /// </summary>
    public async Task<(bool Success, long FileSizeBytes, string? ErrorMessage)> FormatAsync(
        List<Dictionary<string, object>> data, 
        string outputPath)
    {
        try
        {
            Log.Debug("Formatting {RowCount} rows as JSON to {OutputPath}", data.Count, outputPath);

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Configure JSON serialization options
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null, // Keep original property names
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            };

            // Convert data for JSON serialization
            var jsonData = data.Select(row =>
            {
                var jsonRow = new Dictionary<string, object?>();
                foreach (var kvp in row)
                {
                    jsonRow[kvp.Key] = ConvertValueForJson(kvp.Value);
                }
                return jsonRow;
            }).ToList();

            // Write to file
            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(fileStream, jsonData, options);
            await fileStream.FlushAsync();

            var fileSize = fileStream.Length;

            Log.Debug("JSON formatting completed. File size: {FileSizeBytes} bytes", fileSize);
            return (true, fileSize, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error formatting data as JSON");
            return (false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Convert value to JSON-compatible type
    /// Handles DBNull, DateTime, and other special types
    /// </summary>
    private object? ConvertValueForJson(object value)
    {
        if (value == null || value == DBNull.Value)
        {
            return null;
        }

        // Handle DateTime serialization (ISO 8601 format)
        if (value is DateTime dateTime)
        {
            return dateTime.ToString("O"); // ISO 8601 format
        }

        // Handle DateTimeOffset
        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("O");
        }

        // Handle byte arrays as base64
        if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        // Handle Guid
        if (value is Guid guid)
        {
            return guid.ToString();
        }

        // Return value as-is for primitive types
        return value;
    }
}