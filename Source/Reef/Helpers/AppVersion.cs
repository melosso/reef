using System.Reflection;

namespace Reef.Helpers;

// MinVer sets AssemblyVersion to the major only
public static class AppVersion
{
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}
