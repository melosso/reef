namespace Reef.Helpers;

// Destinations keep subfolders below the staging root
public static class TempPaths
{
    public const string WorkflowRoot = "reef-workflows";

    public static string Relative(string sourcePath)
    {
        try
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(sourcePath);
            if (!full.StartsWith(temp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(sourcePath);

            var parts = full[(temp.Length + 1)..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            var skip = parts[0] switch
            {
                "reef_splits" => 1,
                WorkflowRoot => 3,
                var first when parts.Length > 1 && first.Length <= 2 && int.TryParse(first, out _) => 1,
                _ => 0
            };
            return parts.Length > skip ? string.Join(Path.DirectorySeparatorChar, parts.Skip(skip)) : Path.GetFileName(sourcePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.GetFileName(sourcePath);
        }
    }
}
