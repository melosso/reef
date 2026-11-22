using Reef.Core.Parsers;
using Reef.Core.Services;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class ParseNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["parse"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var sourceFormat = NodeConfig.Text(context.Config, "Format");
        var format = ImportRows.ParseFormatConfig(context.Config["Options"]?.ToJsonString());
        if (sourceFormat == "TSV")
            format.Delimiter = "\t";
        format.IsJsonLines = sourceFormat == "JSONL";
        var input = context.Input;
        if (input.Files.Count == 0)
            throw new NodeExecutionException("Parse needs files from the previous step");

        var rows = new List<Dictionary<string, object?>>();
        var failed = new List<string>();
        foreach (var file in input.Files)
        {
            await using var stream = File.OpenRead(file.Path);
            await foreach (var row in ImportParserFactory.Create(sourceFormat).ParseAsync(stream, format, ct))
            {
                if (!string.IsNullOrWhiteSpace(row.ParseError))
                    failed.Add($"{file.Name} line {row.LineNumber}: {row.ParseError}");
                else if (!row.IsSkipped)
                    rows.Add(row.Columns);
            }
        }
        return new NodeResult
        {
            Outputs = { [WorkflowPorts.Main] = WorkflowItems.FromRows(rows) },
            Warning = failed.Count == 0 ? null : $"{failed.Count} rows could not be read, first: {failed[0]}"
        };
    }
}
