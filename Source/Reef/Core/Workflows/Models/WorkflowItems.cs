namespace Reef.Core.Workflows.Models;

public class WorkflowItems
{
    public List<WorkflowColumn> Columns { get; init; } = new();
    public List<Dictionary<string, object?>> Items { get; init; } = new();
    public List<WorkflowFile> Files { get; init; } = new();
    public WorkflowSplit? Split { get; init; }

    public static WorkflowItems FromRows(IEnumerable<Dictionary<string, object?>> rows, List<WorkflowColumn>? columns = null)
    {
        var items = rows.ToList();
        return new() { Items = items, Columns = columns ?? WorkflowColumn.Infer(items) };
    }

    public WorkflowItems WithFiles(List<WorkflowFile> files) => new() { Columns = Columns, Items = Items, Files = files, Split = Split };

    // One batch per split value
    public IEnumerable<WorkflowBatch> Batches()
    {
        if (Split is null)
        {
            yield return new(null, [.. Items.Select(Row)]);
            yield break;
        }
        foreach (var group in Items.GroupBy(r => SplitKey(r.GetValueOrDefault(Split.Column))))
            yield return new(group.Key, [.. group.Select(r => Row(r).Where(c => !Split.ExcludeColumn || c.Key != Split.Column).ToDictionary())]);
    }

    private static Dictionary<string, object> Row(Dictionary<string, object?> row) => row.ToDictionary(c => c.Key, c => c.Value ?? DBNull.Value);

    private static string SplitKey(object? value) => value is null or DBNull ? "_NULL_" : value.ToString()?.Trim() is { Length: > 0 } text ? text : "_EMPTY_";

    public WorkflowItems WithRows(IEnumerable<Dictionary<string, object?>> rows) => new() { Columns = Columns, Items = rows.ToList(), Files = Files, Split = Split };
}
