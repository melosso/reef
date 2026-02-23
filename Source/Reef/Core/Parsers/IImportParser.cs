using Reef.Core.Models;

namespace Reef.Core.Parsers;

/// <summary>
/// Abstraction for parsing a raw data stream into typed rows.
/// Uses IAsyncEnumerable for streaming support with large files.
/// </summary>
public interface IImportParser
{
    /// <summary>
    /// Parse the given stream into rows.
    /// Each row contains a column-name → value dictionary.
    /// ParsedRow.ParseError is non-null when a row could not be parsed.
    /// </summary>
    IAsyncEnumerable<ParsedRow> ParseAsync(
        Stream content,
        ImportFormatConfig config,
        CancellationToken ct = default);
}
