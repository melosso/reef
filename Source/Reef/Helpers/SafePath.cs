namespace Reef.Helpers;

/// <summary>
/// Confines user supplied file names and subfolders to the folder Reef writes to.
/// </summary>
public static class SafePath
{
    public static bool IsContainedRelative(string? path) =>
        string.IsNullOrEmpty(path)
        || (!Path.IsPathRooted(path) && !path.StartsWith('~')
            && !path.Split('/', '\\').Any(segment => segment == ".."));

    public static void EnsureContainedRelative(string? path, string what)
    {
        if (!IsContainedRelative(path))
            throw new InvalidOperationException($"{what} must be a relative name without '..' segments.");
    }

    public static string Combine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        var prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException($"Path '{relative}' leaves the folder '{fullRoot}'.");
        return full;
    }
}
