namespace Reef.Helpers;

// Reef:ExportsPath, relative to the executable or the project folder in development
public static class ExportRoot
{
    private static string? _path;

    private static string Environment => System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

    public static string Path => _path ??= Resolve(null, Environment, AppContext.BaseDirectory);

    public static void Configure(IConfiguration configuration) =>
        _path = Resolve(configuration["Reef:ExportsPath"], Environment, AppContext.BaseDirectory);

    public static string Resolve(string? configured, string environment, string baseDirectory)
    {
        var root = environment == "Development"
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, "..", "..", ".."))
            : baseDirectory;
        var path = string.IsNullOrWhiteSpace(configured) ? "exports" : configured;
        return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path));
    }
}
