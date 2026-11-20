using Reef.Core.Services;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class MappingNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["mapping"];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var profile = ImportNodeProfile.From("mapping", context.Config, context.Workflow.Name);
        var mappings = ImportRows.ParseColumnMappings(profile.ColumnMappingsJson);
        var rows = context.Input.Items.Select(r => ImportRows.ApplyColumnMapping(r, mappings, profile, [])).OfType<Dictionary<string, object?>>().ToList();
        var columns = mappings.Count == 0 ? null
            : mappings.Where(m => !string.IsNullOrWhiteSpace(m.TargetColumn)).Select(m => new WorkflowColumn(m.TargetColumn,
                string.IsNullOrWhiteSpace(m.DataType) ? "string" : m.DataType)).DistinctBy(c => c.Name).ToList();
        return Task.FromResult(NodeResult.Main(WorkflowItems.FromRows(rows, columns)));
    }
}
