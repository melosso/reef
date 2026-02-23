using System.Runtime.CompilerServices;
using System.Text;
using Reef.Core.Models;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Reef.Core.Parsers;

/// <summary>
/// Parses YAML data streams. Supports both a list of objects and a single object.
/// Uses YamlDotNet (already a project dependency).
/// </summary>
public class YamlImportParser : IImportParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public async IAsyncEnumerable<ParsedRow> ParseAsync(
        Stream content,
        ImportFormatConfig config,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string yaml;
        using (var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            yaml = await reader.ReadToEndAsync(ct);
        }

        object? parsed = null;
        ParsedRow? parseErrorRow = null;
        try
        {
            parsed = Deserializer.Deserialize(yaml);
        }
        catch (Exception ex)
        {
            parseErrorRow = new ParsedRow { LineNumber = 0, ParseError = $"YAML parse error: {ex.Message}" };
        }

        if (parseErrorRow != null) { yield return parseErrorRow; yield break; }

        if (parsed == null)
        {
            Log.Warning("YAML parser: document is empty or null");
            yield break;
        }

        // Normalise to a list of dictionaries
        List<Dictionary<string, object?>>? rows = null;
        ParsedRow? structureErrorRow = null;
        try
        {
            rows = NormaliseToRows(parsed, config.DataRootPath);
        }
        catch (Exception ex)
        {
            structureErrorRow = new ParsedRow { LineNumber = 0, ParseError = $"YAML structure error: {ex.Message}" };
        }

        if (structureErrorRow != null) { yield return structureErrorRow; yield break; }

        int lineNumber = 0;
        foreach (var row in rows!)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;
            yield return new ParsedRow { LineNumber = lineNumber, Columns = row };
        }
    }

    private static List<Dictionary<string, object?>> NormaliseToRows(object parsed, string? dataRootPath)
    {
        object? target = parsed;

        if (!string.IsNullOrWhiteSpace(dataRootPath))
        {
            // Simple dot-separated path navigation
            var path = dataRootPath.TrimStart('$').TrimStart('.');
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (target is IDictionary<object, object> dict)
                {
                    if (!dict.TryGetValue(segment, out target))
                        throw new InvalidOperationException($"Key '{segment}' not found in YAML object");
                }
                else
                {
                    throw new InvalidOperationException($"Cannot navigate into {target?.GetType().Name} at segment '{segment}'");
                }
            }
        }

        if (target is List<object> list)
        {
            return list.Select(item => FlattenYaml(item)).ToList();
        }

        if (target is IDictionary<object, object> singleObject)
        {
            return new List<Dictionary<string, object?>> { FlattenYaml(singleObject) };
        }

        throw new InvalidOperationException($"Expected a list or object, got {target?.GetType().Name}");
    }

    private static Dictionary<string, object?> FlattenYaml(object? node)
    {
        if (node is IDictionary<object, object> dict)
        {
            var result = new Dictionary<string, object?>();
            foreach (var kvp in dict)
            {
                var key = kvp.Key?.ToString() ?? "null";
                // Nested objects → serialize as string for simplicity
                result[key] = kvp.Value is IDictionary<object, object> or List<object>
                    ? System.Text.Json.JsonSerializer.Serialize(ConvertYamlForJson(kvp.Value))
                    : kvp.Value;
            }
            return result;
        }

        // Scalar value — wrap it
        return new Dictionary<string, object?> { ["value"] = node };
    }

    private static object? ConvertYamlForJson(object? node)
    {
        return node switch
        {
            IDictionary<object, object> d => d.ToDictionary(k => k.Key.ToString()!, v => ConvertYamlForJson(v.Value)),
            List<object> l => l.Select(ConvertYamlForJson).ToList(),
            _ => node
        };
    }
}
