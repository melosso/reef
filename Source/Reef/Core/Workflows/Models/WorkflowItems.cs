namespace Reef.Core.Workflows.Models;

public class WorkflowItems
{
    public List<WorkflowColumn> Columns { get; init; } = new();
    public List<Dictionary<string, object?>> Items { get; init; } = new();
    public List<WorkflowFile> Files { get; init; } = new();

    public static WorkflowItems FromRows(IEnumerable<Dictionary<string, object?>> rows, List<WorkflowColumn>? columns = null)
    {
        var items = rows.ToList();
        return new() { Items = items, Columns = columns ?? WorkflowColumn.Infer(items) };
    }

    public WorkflowItems WithRows(IEnumerable<Dictionary<string, object?>> rows) => new() { Columns = Columns, Items = rows.ToList(), Files = Files };
}
