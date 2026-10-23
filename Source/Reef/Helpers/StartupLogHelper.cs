using System.Reflection;
using Serilog;

namespace Reef.Helpers;

/// <summary>
/// Startup banner and hosting URL logging
/// </summary>
public static class StartupLogHelper
{
    public static string Version { get; } = typeof(StartupLogHelper).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    public static void LogApplicationBanner()
    {
        Log.Information("");
        Log.Information("Reef {Version} - Data export and integration platform built on .NET", Version);
        Log.Information("");
        Log.Debug("Host {Host} ({OS}, .NET {DotNet})", Environment.MachineName, Environment.OSVersion.Platform, Environment.Version);
    }

    public static void LogHostingSummary(WebApplication app, int port, bool localhostOnly)
    {
        var urls = app.Urls.Count > 0 ? app.Urls.ToList() : [$"http://{(localhostOnly ? "127.0.0.1" : "*")}:{port}"];
        Log.Information("");
        Log.Information("Application is hosted on the following URLs:");
        foreach (var url in urls)
            Log.Information("   {Url}", url);

        if (!app.Environment.IsProduction())
            Log.Warning("Application Environment: {Environment}", app.Environment.EnvironmentName);
    }
}
