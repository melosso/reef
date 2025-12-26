using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using System.Security.Claims;

namespace Reef.Core.Services;

/// <summary>
/// Circuit handler that captures authentication state during circuit initialization
/// </summary>
public class AuthenticationCircuitHandler : CircuitHandler
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthenticationCircuitHandler(
        AuthenticationStateProvider authenticationStateProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // Capture the authentication state from HttpContext when the circuit is initialized
        var httpContext = _httpContextAccessor.HttpContext;
        
        if (httpContext?.User?.Identity?.IsAuthenticated == true &&
            _authenticationStateProvider is ServerAuthenticationStateProvider serverAuthStateProvider)
        {
            // Set the user principal for this circuit
            var authState = new AuthenticationState(httpContext.User);
            serverAuthStateProvider.SetAuthenticationState(Task.FromResult(authState));
        }

        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }
}
