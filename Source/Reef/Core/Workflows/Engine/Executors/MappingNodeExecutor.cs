using Reef.Core.Services;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class MappingNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["mapping"];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var mappings = ImportRows.ParseColumnMappings(context.Config["Columns"]?.ToJsonString());
        var keepOthers = NodeConfig.Flag(context.Config, "AutoMap");
        var mapped = mappings.Select(m => m.SourceColumn).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = context.Input.Items.Select(row =>
        {
            var result = ImportRows.ApplyColumnMapping(row, mappings, false, false, []) ?? [];
            if (keepOthers)
                foreach (var (column, value) in row.Where(c => !mapped.Contains(c.Key)))
                    result.TryAdd(column, value);
            return result;
        }).Where(r => r.Count > 0).ToList();

        var columns = mappings.Select(m => new WorkflowColumn(m.TargetColumn, string.IsNullOrWhiteSpace(m.DataType) ? "string" : m.DataType)).DistinctBy(c => c.Name).ToList();
        if (keepOthers)
            columns.AddRange(context.Input.Columns.Where(c => !mapped.Contains(c.Name)));
        return Task.FromResult(NodeResult.Main(WorkflowItems.FromRows(rows, columns)));
    }
}
