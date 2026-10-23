using Microsoft.AspNetCore.Mvc;
using Reef.Core.Services;

namespace Reef.Api;

/// <summary>
/// Home page data for every signed-in user
/// </summary>
public static class DashboardEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard").RequireAuthorization();

        group.MapGet("/attention", async ([FromServices] AttentionService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));
    }
}
