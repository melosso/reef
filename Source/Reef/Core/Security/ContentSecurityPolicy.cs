using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Reef.Core.Security;

/// <summary>
/// Builds the Content-Security-Policy header. Inline scripts of the SPA shell are allowed by SHA-256 hash.
/// </summary>
public static partial class ContentSecurityPolicy
{
    public static string Build(string? shellHtml)
    {
        var hashes = shellHtml is null
            ? []
            : InlineScript().Matches(shellHtml)
                .Select(m => m.Groups["body"].Value)
                .Where(body => !string.IsNullOrWhiteSpace(body))
                .Select(body => $" 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)))}'");

        return string.Join("; ",
            "default-src 'self'",
            $"script-src 'self'{string.Concat(hashes)}",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data: https:",
            "font-src 'self' data:",
            "connect-src 'self' https://api.github.com https://raw.githubusercontent.com",
            "frame-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'") + ";";
    }

    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();
}
