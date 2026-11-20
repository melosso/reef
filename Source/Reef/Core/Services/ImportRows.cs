using System.Text.Json;
using Reef.Core.Models;

namespace Reef.Core.Services;

public static class ImportRows
{
    public static Dictionary<string, object?>? ApplyColumnMapping(
        Dictionary<string, object?> sourceColumns,
        List<ImportColumnMapping> mappings,
        bool autoMapColumns,
        bool skipUnmappedColumns,
        List<TargetColumnInfo> schema)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (mappings.Count > 0)
        {
            foreach (var m in mappings)
            {
                object? value = null;

                if (sourceColumns.TryGetValue(m.SourceColumn, out var raw))
                    value = raw;
                else if (!string.IsNullOrWhiteSpace(m.DefaultValue))
                    value = m.DefaultValue;

                if (value is null && m.SkipOnNull)
                    continue;

                if (value is null && !string.IsNullOrWhiteSpace(m.DefaultValue))
                    value = m.DefaultValue;

                result[m.TargetColumn] = CastValue(value, m.DataType);
            }
        }
        else if (autoMapColumns)
        {
            var schemaNames = schema.Select(s => s.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, val) in sourceColumns)
            {
                if (schemaNames.Contains(col) || !skipUnmappedColumns)
                    result[col] = val;
            }

            if (result.Count == 0 && sourceColumns.Count > 0)
            {
                foreach (var (col, val) in sourceColumns)
                    result[col] = val;
            }
        }
        else
        {
            foreach (var (col, val) in sourceColumns)
                result[col] = val;
        }

        return result.Count > 0 ? result : null;
    }

    public static object? CastValue(object? value, string? dataType)
    {
        if (value is null) return null;
        var str = value.ToString()!;

        return dataType?.ToLowerInvariant() switch
        {
            "int" or "integer" or "long" =>
                long.TryParse(str, out var l) ? l : null,
            "decimal" or "float" or "double" or "number" =>
                decimal.TryParse(str, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            "bool" or "boolean" =>
                str is "1" or "true" or "yes" or "True" or "TRUE" or "YES" ? true :
                str is "0" or "false" or "no" or "False" or "FALSE" or "NO" ? false : null,
            "datetime" or "date" =>
                DateTime.TryParse(str, null, System.Globalization.DateTimeStyles.RoundtripKind,
                    out var dt) ? dt : null,
            _ => value
        };
    }

    public static ImportFormatConfig ParseFormatConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ImportFormatConfig();
        try { return JsonSerializer.Deserialize<ImportFormatConfig>(json, CaseInsensitive) ?? new ImportFormatConfig(); }
        catch { return new ImportFormatConfig(); }
    }

    // Editor stores camelCase keys
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public static List<ImportColumnMapping> ParseColumnMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<ImportColumnMapping>>(json, CaseInsensitive) ?? []; }
        catch { return []; }
    }
}
