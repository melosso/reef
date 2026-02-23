using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Parsers;

/// <summary>
/// Parses JSON and JSONL (newline-delimited JSON) data streams.
/// Supports a DataRootPath expression to locate the array within the document.
/// </summary>
public class JsonImportParser : IImportParser
{
    public async IAsyncEnumerable<ParsedRow> ParseAsync(
        Stream content,
        ImportFormatConfig config,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (config.IsJsonLines)
        {
            await foreach (var row in ParseJsonLinesAsync(content, config, ct))
                yield return row;
            yield break;
        }

        // Read full document
        string json;
        using (var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
        {
            json = await reader.ReadToEndAsync(ct);
        }

        List<Dictionary<string, object?>>? rows = null;
        ParsedRow? parseErrorRow = null;
        try
        {
            rows = ExtractRows(json, config.DataRootPath);
        }
        catch (Exception ex)
        {
            parseErrorRow = new ParsedRow { LineNumber = 0, ParseError = $"JSON parse error: {ex.Message}" };
        }

        if (parseErrorRow != null) { yield return parseErrorRow; yield break; }

        int lineNumber = 0;
        foreach (var row in rows!)
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;
            yield return new ParsedRow { LineNumber = lineNumber, Columns = row };
        }
    }

    private async IAsyncEnumerable<ParsedRow> ParseJsonLinesAsync(
        Stream content,
        ImportFormatConfig config,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        int lineNumber = 0;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line)) continue;

            Dictionary<string, object?>? row = null;
            ParsedRow? errorRow = null;
            try
            {
                row = JsonToDict(JsonDocument.Parse(line).RootElement);
            }
            catch (Exception ex)
            {
                errorRow = new ParsedRow { LineNumber = lineNumber, ParseError = $"Line {lineNumber}: {ex.Message}" };
            }

            if (errorRow != null) { yield return errorRow; continue; }

            yield return new ParsedRow { LineNumber = lineNumber, Columns = row! };
        }
    }

    private static List<Dictionary<string, object?>> ExtractRows(string json, string? dataRootPath)
    {
        using var doc = JsonDocument.Parse(json);
        var element = doc.RootElement;

        if (!string.IsNullOrWhiteSpace(dataRootPath))
        {
            element = NavigatePath(element, dataRootPath);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Select(e => JsonToDict(e))
                .ToList();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            return new List<Dictionary<string, object?>> { JsonToDict(element) };
        }

        throw new InvalidOperationException($"Expected JSON array or object, got {element.ValueKind}");
    }

    private static JsonElement NavigatePath(JsonElement root, string path)
    {
        // Simple $ prefix stripping, then dot-separated navigation
        if (path.StartsWith("$.")) path = path[2..];
        else if (path.StartsWith("$")) path = path[1..];

        var current = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Cannot navigate into {current.ValueKind} at segment '{segment}'");

            if (!current.TryGetProperty(segment, out current))
                throw new InvalidOperationException($"Path segment '{segment}' not found");
        }

        return current;
    }

    internal static Dictionary<string, object?> JsonToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();

        if (element.ValueKind != JsonValueKind.Object)
        {
            dict["value"] = ConvertValue(element);
            return dict;
        }

        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertValue(prop.Value);
        }

        return dict;
    }

    private static object? ConvertValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.TryGetInt64(out long l) ? (object)l
                                : element.TryGetDouble(out double d) ? d
                                : element.GetDecimal(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Array => element.GetRawText(),
        JsonValueKind.Object => element.GetRawText(),
        _ => element.GetRawText()
    };
}
