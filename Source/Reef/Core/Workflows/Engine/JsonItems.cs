using Reef.Core.Parsers;
using Reef.Core.Services;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine;

public static class JsonItems
{
    public static async Task<WorkflowItems> ReadAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, ct);
        if (buffer.Length == 0)
            return new WorkflowItems();
        buffer.Position = 0;
        var rows = new List<Dictionary<string, object?>>();
        await foreach (var row in ImportParserFactory.Create("JSON").ParseAsync(buffer, ImportRows.ParseFormatConfig(null), ct))
        {
            if (!string.IsNullOrWhiteSpace(row.ParseError))
                throw new InvalidOperationException(row.ParseError);
            rows.Add(row.Columns);
        }
        return WorkflowItems.FromRows(rows);
    }
}
