namespace Reef.Core.Workflows.Models;

public record WorkflowColumn(string Name, string Type)
{
    public static List<WorkflowColumn> Infer(List<Dictionary<string, object?>> rows) =>
        rows.Count == 0 ? [] : [.. rows[0].Select(c => new WorkflowColumn(c.Key, TypeOf(rows.Select(r => r.GetValueOrDefault(c.Key)).FirstOrDefault(v => v is not null and not DBNull))))];

    private static string TypeOf(object? value) => value switch
    {
        null => "string",
        bool => "bool",
        byte or short or int or long or sbyte or ushort or uint or ulong => "int",
        float or double or decimal => "decimal",
        DateTime or DateTimeOffset or DateOnly => "datetime",
        byte[] => "binary",
        _ => "string"
    };
}
