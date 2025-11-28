// Source/Reef/Api/SystemInfoEndpoints.cs
// Public API endpoints for system information accessible to all authenticated users

using Microsoft.AspNetCore.Mvc;
using Reef.Core.Services;
using Serilog;
using System.Reflection;

namespace Reef.Api;

/// <summary>
/// API endpoints for system information
/// These endpoints are accessible to all authenticated users (non-sensitive data only)
/// </summary>
public static class SystemInfoEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/system").RequireAuthorization();

        group.MapGet("/info", GetSystemInfo);
    }

    /// <summary>
    /// GET /api/system/info - Get public system information
    /// Returns non-sensitive information accessible to all authenticated users
    /// </summary>
    private static async Task<IResult> GetSystemInfo(
        HttpContext context,
        [FromServices] AdminService service)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "Unknown";

            // Extract the informational version if available (this includes the semantic version from .csproj)
            var informationalVersion = assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as AssemblyInformationalVersionAttribute;

            var semanticVersion = informationalVersion?.InformationalVersion ?? version;

            // Get uptime information
            var metrics = await service.GetSystemMetricsAsync() as dynamic;
            var uptimeDays = metrics?.system?.uptimeDays ?? 0;

            return Results.Ok(new
            {
                version = semanticVersion,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                    ?? "Production",
                timestamp = DateTime.UtcNow,
                uptime = new
                {
                    days = uptimeDays
                },
                license = new
                {
                    type = "AGPL 3.0",
                    description = "Open source with copyleft provisions"
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving system information");
            return Results.Problem("Error retrieving system information");
        }
    }
}
