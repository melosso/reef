namespace Reef.Helpers;

// Folder that contains export files, the project folder in development and the executable folder otherwise.
public static class ExportRoot
{
    public static string Path =>
        (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production") == "Development"
            ? System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..")), "exports")
            : System.IO.Path.Combine(AppContext.BaseDirectory, "exports");
}
