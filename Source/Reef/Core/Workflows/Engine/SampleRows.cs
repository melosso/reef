namespace Reef.Core.Workflows.Engine;

// Keeps run samples small for binary and long values
public static class SampleRows
{
    private const int MaxText = 500;

    public static IEnumerable<Dictionary<string, object?>> Take(IEnumerable<Dictionary<string, object?>> rows, int count) =>
        rows.Take(count).Select(row => row.ToDictionary(c => c.Key, c => Shorten(c.Value)));

    private static object? Shorten(object? value) => value switch
    {
        byte[] bytes => $"[binary {bytes.Length} bytes]",
        string text when text.Length > MaxText => string.Concat(text.AsSpan(0, MaxText), "…"),
        _ => value
    };
}
