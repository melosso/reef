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
    private Task<AuthenticationState>? _cachedAuthenticationState;

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

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;

        // If we have HttpContext with authenticated user, cache and return it
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var authState = new AuthenticationState(httpContext.User);
            _cachedAuthenticationState = Task.FromResult(authState);
            return _cachedAuthenticationState;
        }

        // If we have a cached state (from previous HTTP request), use it
        // This is for SignalR circuits where HttpContext may not be available
        if (_cachedAuthenticationState != null)
        {
            return _cachedAuthenticationState;
        }

        // No authentication available - return unauthenticated user
        var unauthenticatedState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        return Task.FromResult(unauthenticatedState);
    }
}
