using System.Text.RegularExpressions;

namespace Reef.Helpers;

/// <summary>
/// Matches a download token against the token segment of an export path (exports/{date}/{token}/{file}).
/// </summary>
public static partial class DownloadToken
{
    public static bool Matches(string? outputPath, string token) =>
        outputPath is not null && TokenSegment().Match(outputPath.Replace('\\', '/')) is { Success: true } match
        && string.Equals(match.Groups[1].Value, token, StringComparison.Ordinal);

    [GeneratedRegex(@"exports/[\d-]+/([^/]+)/")]
    private static partial Regex TokenSegment();
}
