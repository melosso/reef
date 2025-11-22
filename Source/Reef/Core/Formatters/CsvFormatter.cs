using Serilog;
using System.Text;

namespace Reef.Core.Formatters;

/// <summary>
/// CSV formatter for query results
/// Outputs data as CSV with header row and proper escaping
/// </summary>
public class CsvFormatter : IFormatter
{
    /// <summary>
    /// Format data as CSV and write to file
    /// </summary>
    public async Task<(bool Success, long FileSizeBytes, string? ErrorMessage)> FormatAsync(
        List<Dictionary<string, object>> data, 
        string outputPath)
    {
        try
        {
            Log.Debug("Formatting {RowCount} rows as CSV to {OutputPath}", data.Count, outputPath);

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(fileStream, new UTF8Encoding(true)); // UTF-8 with BOM

            if (data.Count == 0)
            {
                await writer.WriteLineAsync("No data");
                await writer.FlushAsync();
                return (true, fileStream.Length, null);
            }

            // Write header row
            var headers = data[0].Keys.ToList();
            var headerLine = string.Join(",", headers.Select(EscapeCsvValue));
            await writer.WriteLineAsync(headerLine);

            // Write data rows
            foreach (var row in data)
            {
                var values = headers.Select(header =>
                {
                    if (row.TryGetValue(header, out var value))
                    {
                        return ConvertValueToCsvString(value);
                    }
                    return string.Empty;
                });

                var line = string.Join(",", values.Select(EscapeCsvValue));
                await writer.WriteLineAsync(line);
            }

            await writer.FlushAsync();
            var fileSize = fileStream.Length;

            Log.Debug("CSV formatting completed. File size: {FileSizeBytes} bytes", fileSize);
            return (true, fileSize, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error formatting data as CSV");
            return (false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Convert value to string for CSV
    /// </summary>
    private string ConvertValueToCsvString(object value)
    {
        if (value == null || value == DBNull.Value)
        {
            return string.Empty;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Escape CSV value by wrapping in quotes if needed
    /// Handles commas, quotes, and newlines
    /// </summary>
    private string EscapeCsvValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Check if value needs escaping
        var needsEscaping = value.Contains(',') || 
                           value.Contains('"') || 
                           value.Contains('\n') || 
                           value.Contains('\r');

        if (!needsEscaping)
        {
            return value;
        }

        // Escape quotes by doubling them
        var escaped = value.Replace("\"", "\"\"");

        // Wrap in quotes
        return $"\"{escaped}\"";
    }
}