using Microsoft.AspNetCore.Http;

namespace Reef.Core.Services;

/// <summary>
/// Service for accessing authentication context in Blazor components
/// </summary>
public class BlazorAuthService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BlazorAuthService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Gets the current authenticated username
    /// </summary>
    public string GetCurrentUsername()
    {
        return _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";
    }

    /// <summary>
    /// Gets the first character of the username for avatar display
    /// </summary>
    public string GetUserInitial()
    {
        var username = GetCurrentUsername();
        return string.IsNullOrEmpty(username) ? "?" : username[0].ToString().ToUpper();
    }

    /// <summary>
    /// Checks if the current user is authenticated
    /// </summary>
    public bool IsAuthenticated()
    {
        return _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
    }

    /// <summary>
    /// Checks if the current user has the Admin role
    /// </summary>
    public bool IsAdmin()
    {
        return _httpContextAccessor.HttpContext?.User?.IsInRole("Admin") ?? false;
    }

    /// <summary>
    /// Gets the user ID from claims
    /// </summary>
    public int? GetUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("UserId")?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }
}
