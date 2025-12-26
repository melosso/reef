using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Reef.Core.Security;
using System.Security.Claims;

namespace Reef.Core.Services;

/// <summary>
/// Authentication state provider for Blazor Server that works with JWT tokens
/// Persists authentication state from initial HTTP request into SignalR circuit
/// </summary>
public class HttpContextAuthenticationStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IServiceScopeFactory _scopeFactory;

    public HttpContextAuthenticationStateProvider(
        IHttpContextAccessor httpContextAccessor,
        JwtTokenService jwtTokenService,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory)
        : base(loggerFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _jwtTokenService = jwtTokenService;
        _scopeFactory = scopeFactory;
    }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // Return true to keep the authentication state valid
        return Task.FromResult(authenticationState.User.Identity?.IsAuthenticated ?? false);
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // If we have HttpContext (initial request), use it
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                return new AuthenticationState(httpContext.User);
            }

            // If no HttpContext (SignalR circuit), try to get token from cookies
            if (httpContext?.Request?.Cookies != null &&
                httpContext.Request.Cookies.TryGetValue("reef_token", out var token) &&
                !string.IsNullOrEmpty(token))
            {
                var principal = _jwtTokenService.ValidateToken(token);
                if (principal != null)
                {
                    return new AuthenticationState(principal);
                }
            }

            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
        catch
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }
}
