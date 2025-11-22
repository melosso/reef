// Source/Reef/Core/Formatters/YamlFormatter.cs
// YAML formatter for data export

using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Reef.Core.Formatters;

/// <summary>
/// Formats query results as YAML
/// </summary>
public class YamlFormatter : IFormatter
{
    public async Task<(bool Success, long FileSizeBytes, string? ErrorMessage)> FormatAsync(
        List<Dictionary<string, object>> data, string outputPath)
    {
        try
        {
            // Convert DBNull to null and format special types
            var sanitizedData = data.Select(row => row.ToDictionary(
                kvp => kvp.Key,
                kvp => SanitizeValue(kvp.Value)
            )).ToList();

            // Create YAML serializer
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            // Serialize to YAML
            var yaml = serializer.Serialize(sanitizedData);

            // Write to file
            await File.WriteAllTextAsync(outputPath, yaml);

            var fileInfo = new FileInfo(outputPath);
            var fileSize = fileInfo.Length;

            Log.Information("Formatted {RowCount} rows as YAML ({FileSize} bytes)", data.Count, fileSize);
            return (true, fileSize, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error formatting data as YAML");
            return (false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Sanitize value for YAML serialization
    /// </summary>
    private object? SanitizeValue(object? value)
    {
        if (value == null || value is DBNull)
        {
            return null;
        }

        // Handle DateTime
        if (value is DateTime dt)
        {
            return dt.ToString("o"); // ISO 8601 format
        }

        // Handle DateTimeOffset
        if (value is DateTimeOffset dto)
        {
            return dto.ToString("o"); // ISO 8601 format
        }

        // Handle byte arrays
        if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        // Handle Guid
        if (value is Guid guid)
        {
            return guid.ToString();
        }

        // Handle decimals and doubles
        if (value is decimal || value is double || value is float)
        {
            return value;
        }

        // Handle boolean
        if (value is bool b)
        {
            return b;
        }

        // Handle numeric types
        if (value is int || value is long || value is short || value is byte)
        {
            return value;
        }

        // Default: convert to string
        return value.ToString();
    }
}