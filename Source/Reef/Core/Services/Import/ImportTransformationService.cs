using System.Text.Json;
using Serilog;
using Reef.Core.Models;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import;

/// <summary>
/// Service for row-level data transformation during import
/// Supports field mapping, Scriban templates, type conversion, and default values
/// </summary>
public class ImportTransformationService
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ImportTransformationService));

    /// <summary>
    /// Transform rows based on field mappings and validation rules
    /// </summary>
    public Task<List<Dictionary<string, object>>> TransformRowsAsync(
        List<Dictionary<string, object>> sourceRows,
        ImportProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (!sourceRows.Any())
            return Task.FromResult(sourceRows);

        var transformedRows = new List<Dictionary<string, object?>>();
        var fieldMappings = ParseFieldMappings(profile.FieldMappingsJson);

        if (!fieldMappings.Any())
        {
            // No mappings - return rows as-is
            return Task.FromResult(sourceRows);
        }

        foreach (var sourceRow in sourceRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var transformedRow = new Dictionary<string, object?>();

                foreach (var mapping in fieldMappings)
                {
                    var value = TransformFieldValue(sourceRow, mapping, sourceRow);
                    transformedRow[mapping.DestinationColumn] = value;
                }

                transformedRows.Add(transformedRow);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to transform row, using source as-is");
                transformedRows.Add(sourceRow.ToDictionary(k => k.Key, v => (object?)v.Value));
            }
        }

        Log.Debug("Transformed {Count} rows", transformedRows.Count);
        return Task.FromResult(transformedRows.Cast<Dictionary<string, object>>().ToList());
    }

    /// <summary>
    /// Transform a single field value based on mapping configuration
    /// </summary>
    private object? TransformFieldValue(
        Dictionary<string, object> sourceRow,
        FieldMapping mapping,
        Dictionary<string, object> context)
    {
        object? sourceValue = null;

        // Get source value
        if (sourceRow.TryGetValue(mapping.SourceColumn, out var value))
        {
            sourceValue = value;
        }
        else if (sourceValue == null && !string.IsNullOrEmpty(mapping.DefaultValue))
        {
            sourceValue = mapping.DefaultValue;
        }

        // Apply transformation template if provided
        if (!string.IsNullOrEmpty(mapping.TransformationTemplate))
        {
            try
            {
                sourceValue = EvaluateTemplate(mapping.TransformationTemplate, sourceRow, sourceValue);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply transformation template for {Column}", mapping.SourceColumn);
            }
        }

        // Apply type conversion
        if (sourceValue != null && mapping.DataType != FieldDataType.String)
        {
            sourceValue = ConvertType(sourceValue, mapping.DataType);
        }

        // Apply default if null and required
        if (sourceValue == null && !string.IsNullOrEmpty(mapping.DefaultValue))
        {
            sourceValue = mapping.DefaultValue;
        }

        return sourceValue;
    }

    /// <summary>
    /// Evaluate Scriban template with row context
    /// </summary>
    private object? EvaluateTemplate(
        string template,
        Dictionary<string, object> context,
        object? sourceValue)
    {
        try
        {
            // Simple template evaluation - can be extended with Scriban library
            // For now, support basic variable substitution
            var result = template
                .Replace("{{value}}", sourceValue?.ToString() ?? "")
                .Replace("{{now}}", DateTime.UtcNow.ToString("O"))
                .Replace("{{today}}", DateTime.UtcNow.ToString("yyyy-MM-dd"));

            // Support simple context variable substitution: {{fieldName}}
            foreach (var kvp in context)
            {
                result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value?.ToString() ?? "");
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Template evaluation failed");
            return sourceValue;
        }
    }

    /// <summary>
    /// Convert value to target type
    /// </summary>
    private object? ConvertType(object? value, FieldDataType targetType)
    {
        if (value == null || value == DBNull.Value)
            return null;

        try
        {
            return targetType switch
            {
                FieldDataType.String => value.ToString(),
                FieldDataType.Integer => Convert.ToInt64(value),
                FieldDataType.Decimal => Convert.ToDecimal(value),
                FieldDataType.Boolean => ConvertToBoolean(value),
                FieldDataType.DateTime => ConvertToDateTime(value),
                FieldDataType.Json => value is string s ? JsonDocument.Parse(s) : value,
                FieldDataType.Binary => value is string s2 ? Convert.FromBase64String(s2) : (byte[]?)value,
                _ => value
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Type conversion failed from {Type} to {TargetType}",
                value.GetType().Name, targetType);
            return value;
        }
    }

    private bool ConvertToBoolean(object value)
    {
        return value switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       s.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                       s.Equals("yes", StringComparison.OrdinalIgnoreCase),
            int i => i != 0,
            long l => l != 0,
            _ => false
        };
    }

    private DateTime ConvertToDateTime(object value)
    {
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string s => DateTime.TryParse(s, out var dt) ? dt : throw new InvalidCastException(),
            _ => throw new InvalidCastException()
        };
    }

    private List<FieldMapping> ParseFieldMappings(string? fieldMappingsJson)
    {
        if (string.IsNullOrEmpty(fieldMappingsJson))
            return new List<FieldMapping>();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };

            using var doc = JsonDocument.Parse(fieldMappingsJson);
            var mappings = new List<FieldMapping>();

            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var mapping = new FieldMapping
                {
                    SourceColumn = element.GetProperty("sourceColumn").GetString() ?? "",
                    DestinationColumn = element.GetProperty("destinationColumn").GetString() ?? "",
                    DataType = element.TryGetProperty("dataType", out var dt) &&
                              Enum.TryParse<FieldDataType>(dt.GetString(), true, out var enumVal)
                        ? enumVal : FieldDataType.String,
                    DefaultValue = element.TryGetProperty("defaultValue", out var dv) ? dv.GetString() : null,
                    TransformationTemplate = element.TryGetProperty("transformationTemplate", out var tt) ? tt.GetString() : null,
                    Required = element.TryGetProperty("required", out var r) && r.GetBoolean()
                };

                mappings.Add(mapping);
            }

            return mappings;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse field mappings");
            return new List<FieldMapping>();
        }
    }
}
