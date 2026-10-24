using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Reef.Core.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyValidator validator) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "ApiKey";
    public const string Header = "X-Api-Key";
    private const string WriteClaim = "reef:write";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var presented) || string.IsNullOrEmpty(presented))
            return AuthenticateResult.NoResult();

        var key = await validator.ValidateAsync(presented.ToString());
        if (key is null)
            return AuthenticateResult.Fail("Invalid API key");

        // The User role keeps admin endpoints and privileged settings out of reach
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, $"apikey:{key.Name}"),
            new Claim(ClaimTypes.Role, "User"),
            new Claim(WriteClaim, ApiKeyValidator.CanWrite(key.Permissions) ? "true" : "false")
        ], Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme));
    }

    public static bool MayCall(AuthorizationHandlerContext context) =>
        context.User.Identity?.AuthenticationType != Scheme
        || context.User.HasClaim(WriteClaim, "true")
        || context.Resource is HttpContext http && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method));
}
