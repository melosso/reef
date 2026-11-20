namespace Reef.Core.Workflows.Engine;

public static class FileNames
{
    public static string Generate(string? template, string workflowName, string extension, int executionId, string? splitKey = null)
    {
        if (string.IsNullOrWhiteSpace(template))
            return splitKey is null ? $"reef_workflow_{executionId}_{Guid.NewGuid():N}.{extension}" : $"{Sanitize(workflowName)}_{Sanitize(splitKey)}.{extension}";
        var now = DateTime.UtcNow;
        return template
            .Replace("{profile}", Sanitize(workflowName), StringComparison.OrdinalIgnoreCase)
            .Replace("{workflow}", Sanitize(workflowName), StringComparison.OrdinalIgnoreCase)
            .Replace("{splitkey}", Sanitize(splitKey ?? ""), StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", now.ToString("yyyyMMdd_HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{guid}", Guid.NewGuid().ToString("N"), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", extension, StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
}
