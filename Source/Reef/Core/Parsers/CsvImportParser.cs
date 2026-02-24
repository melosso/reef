using System.Runtime.CompilerServices;
using System.Text;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Parsers;

/// <summary>
/// Streaming CSV/TSV parser.
/// Does not require an external library — uses StreamReader with RFC-4180 compliant field parsing.
/// </summary>
public class CsvImportParser : IImportParser
{
    public async IAsyncEnumerable<ParsedRow> ParseAsync(
        Stream content,
        ImportFormatConfig config,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var encoding = GetEncoding(config.Encoding);
        char delimiter = string.IsNullOrEmpty(config.Delimiter) ? ',' : config.Delimiter[0];
        char quoteChar = string.IsNullOrEmpty(config.QuoteChar) ? '"' : config.QuoteChar[0];

        using var reader = new StreamReader(content, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        // Skip header rows if configured
        for (int i = 0; i < config.SkipRows && !reader.EndOfStream; i++)
        {
            await reader.ReadLineAsync(ct);
        }

        List<string>? headers = null;
        int lineNumber = config.SkipRows;

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            lineNumber++;

            if (line == null) break;
            if (config.TrimWhitespace) line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            List<string>? fields = null;
            ParsedRow? errorRow = null;
            try
            {
                fields = ParseCsvLine(line, delimiter, quoteChar, config.TrimWhitespace);
            }
            catch (Exception ex)
            {
                errorRow = new ParsedRow
                {
                    LineNumber = lineNumber,
                    ParseError = $"Failed to parse line {lineNumber}: {ex.Message}"
                };
            }

            if (errorRow != null) { yield return errorRow; continue; }

            if (headers == null)
            {
                if (config.HasHeader)
                {
                    headers = fields!;
                    continue; // header row consumed
                }
                else
                {
                    // Auto-generate column names: Col1, Col2, ...
                    headers = Enumerable.Range(1, fields!.Count).Select(i => $"Col{i}").ToList();
                }
            }

            var row = new ParsedRow { LineNumber = lineNumber };

            for (int i = 0; i < headers.Count; i++)
            {
                string value = i < fields!.Count ? fields[i] : string.Empty;

                // Treat configured null-value string as null
                object? typedValue = (config.NullValue != null && value == config.NullValue)
                    ? (object?)null
                    : value;

                row.Columns[headers[i]] = typedValue;
            }

            yield return row;
        }
    }

    // ── RFC-4180 CSV line parser ──

    private static List<string> ParseCsvLine(string line, char delimiter, char quote, bool trim)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == quote)
                {
                    if (i + 1 < line.Length && line[i + 1] == quote)
                    {
                        // Escaped quote
                        current.Append(quote);
                        i += 2;
                        continue;
                    }
                    inQuotes = false;
                    i++;
                    continue;
                }
                current.Append(c);
                i++;
            }
            else
            {
                if (c == quote && current.Length == 0)
                {
                    inQuotes = true;
                    i++;
                    continue;
                }
                if (c == delimiter)
                {
                    var field = current.ToString();
                    fields.Add(trim ? field.Trim() : field);
                    current.Clear();
                    i++;
                    continue;
                }
                current.Append(c);
                i++;
            }
        }

        var lastField = current.ToString();
        fields.Add(trim ? lastField.Trim() : lastField);
        return fields;
    }

    private static Encoding GetEncoding(string? name) => name?.ToUpperInvariant() switch
    {
        "UTF-8" or "UTF8" or null => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        "UTF-16" or "UTF16" or "UNICODE" => Encoding.Unicode,
        "ASCII" => Encoding.ASCII,
        "ISO-8859-1" or "LATIN1" => Encoding.Latin1,
        _ => Encoding.GetEncoding(name!)
    };
}
