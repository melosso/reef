namespace Reef.Core.Services;

/// <summary>
/// Log context for profile executions. The output templates render ProfileTag as a "[CODE] " prefix.
/// </summary>
public static class ExecutionLogContext
{
    // Distinct from ProfileCode: message templates that name {ProfileCode} would replace the prefix.
    public const string ProfileProperty = "ProfileTag";

    public static string Prefix(string code) => $"[{code}] ";
}
