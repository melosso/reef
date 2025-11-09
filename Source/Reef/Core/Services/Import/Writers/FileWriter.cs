using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services.Import.Writers;

/// <summary>
/// FileWriter: Writes imported data to files (CSV, JSON, Parquet)
/// </summary>
public class FileWriter : IDataWriter
{
    private readonly Serilog.ILogger _logger = Log.ForContext<FileWriter>();

    /// <summary>
    /// Gets the destination type this writer handles
    /// </summary>
    public ImportDestinationType DestinationType => ImportDestinationType.File;

    /// <summary>
    /// Validate that the file can be written to
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        string destinationUri,
        string? destinationConfig,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationUri))
            return new ValidationResult { IsValid = false, ErrorMessage = "Destination URI (file path) is required" };

        try
        {
            // Test write permissions by checking if we can write to the directory
            var directory = Path.GetDirectoryName(destinationUri);
            if (string.IsNullOrEmpty(directory)) directory = ".";

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var testFile = Path.Combine(directory, ".reef_write_test");
            await File.WriteAllTextAsync(testFile, "test", cancellationToken);
            File.Delete(testFile);

            return new ValidationResult { IsValid = true };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "File validation failed for {Path}", destinationUri);
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"File path validation failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Write rows to file
    /// </summary>
    public async Task<WriteResult> WriteAsync(
        string destinationUri,
        string? destinationConfig,
        List<Dictionary<string, object>> rows,
        List<FieldMapping> fieldMappings,
        WriteMode mode = WriteMode.Insert,
        CancellationToken cancellationToken = default)
    {
        var result = new WriteResult();

        if (rows == null || rows.Count == 0)
        {
            _logger.Information("No rows to write");
            return result;
        }

        try
        {
            // Determine format from config or file extension
            var format = DetermineFormat(destinationUri, destinationConfig);

            // Create directory if needed
            var directory = Path.GetDirectoryName(destinationUri);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            switch (format.ToLowerInvariant())
            {
                case "csv":
                    await WriteCsvAsync(destinationUri, rows, fieldMappings, cancellationToken);
                    break;

                case "json":
                    await WriteJsonAsync(destinationUri, rows, fieldMappings, cancellationToken);
                    break;

                case "parquet":
                    // Parquet requires additional library, fall back to JSON
                    _logger.Warning("Parquet format requires additional library. Writing as JSON instead.");
                    await WriteJsonAsync(destinationUri, rows, fieldMappings, cancellationToken);
                    break;

                default:
                    throw new ArgumentException($"Unsupported format: {format}");
            }

            result.RowsWritten = rows.Count;
            _logger.Information("Successfully wrote {Count} rows to {Path}", rows.Count, destinationUri);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error writing to file: {Path}", destinationUri);
            result.RowsFailed = rows.Count;
            result.ErrorMessages = new List<string> { ex.Message };
        }

        return result;
    }

    /// <summary>
    /// Determine format from file extension or config
    /// </summary>
    private string DetermineFormat(string destinationUri, string? destinationConfig)
    {
        // Check config first
        if (!string.IsNullOrEmpty(destinationConfig))
        {
            try
            {
                var options = JsonSerializer.Deserialize<JsonElement>(destinationConfig);
                if (options.TryGetProperty("format", out var formatProp))
                {
                    return formatProp.GetString() ?? "json";
                }
            }
            catch { }
        }

        // Fallback to file extension
        var ext = Path.GetExtension(destinationUri).ToLowerInvariant().TrimStart('.');
        return ext switch
        {
            "csv" => "csv",
            "json" => "json",
            "parquet" => "parquet",
            _ => "json"  // Default to JSON
        };
    }

    /// <summary>
    /// Write rows as CSV
    /// </summary>
    private async Task WriteCsvAsync(
        string filePath,
        List<Dictionary<string, object>> rows,
        List<FieldMapping> fieldMappings,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return;

        var csv = new StringBuilder();
        var headers = rows.First().Keys.ToList();

        // Write header
        csv.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        // Write rows
        foreach (var row in rows)
        {
            var values = headers.Select(h => row.ContainsKey(h) ? EscapeCsv(row[h]) : "");
            csv.AppendLine(string.Join(",", values));
        }

        await File.WriteAllTextAsync(filePath, csv.ToString(), Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Write rows as JSON
    /// </summary>
    private async Task WriteJsonAsync(
        string filePath,
        List<Dictionary<string, object>> rows,
        List<FieldMapping> fieldMappings,
        CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(rows, options);
        await File.WriteAllTextAsync(filePath, json + Environment.NewLine, Encoding.UTF8, cancellationToken);
    }

    /// <summary>
    /// Escape CSV field values
    /// </summary>
    private static string EscapeCsv(object value)
    {
        if (value == null) return "";

        var str = value.ToString() ?? "";

        // If contains comma, newline, or quote, wrap in quotes and escape quotes
        if (str.Contains(",") || str.Contains("\n") || str.Contains("\""))
        {
            return $"\"{str.Replace("\"", "\"\"")}\"";
        }

        return str;
    }
}
