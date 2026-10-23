namespace Reef.Core.Security;

/// <summary>
/// Decides which SPA page requests require a signed-in session. Every page except the public ones requires the reef_token cookie.
/// </summary>
public sealed class SpaPageGuard(JwtTokenService tokens, TokenUserValidator users)
{
    // Legacy paths stay public. The SPA rewrites them to /login and /logout.
    private static readonly string[] PublicPages = ["/", "/login", "/logout", "/index", "/logoff"];

    public static bool IsPublic(PathString path) =>
        PublicPages.Contains(path.Value?.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/", StringComparer.OrdinalIgnoreCase);

    public async Task<bool> IsSignedInAsync(HttpContext context) =>
        context.Request.Cookies.TryGetValue("reef_token", out var token)
        && !string.IsNullOrEmpty(token)
        && tokens.ValidateToken(token) is { } principal
        && await users.IsCurrentAsync(principal);
}
