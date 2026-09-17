using Microsoft.AspNetCore.Mvc;
using Reef.Core.Services;
using Serilog;

namespace Reef.Api;

public static class RecipesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/recipes").RequireAuthorization();

        g.MapGet("/", GetAvailable);
        g.MapPost("/{key}/start", Start);
        g.MapPost("/{key}/reconfigure", Reconfigure);
        g.MapGet("/runs/{runId:int}", GetRunState);
        g.MapPost("/runs/{runId:int}/steps/{stepKey}/save", SaveStep);
        g.MapPost("/runs/{runId:int}/steps/{stepKey}/skip", SkipStep);
        g.MapPost("/runs/{runId:int}/flow-groups/{flowGroup}/skip", SkipFlowGroup);
        g.MapPost("/runs/{runId:int}/steps/{stepKey}/verify", VerifyStep);
        g.MapPost("/runs/{runId:int}/complete", Complete);
        g.MapPost("/runs/{runId:int}/abandon", Abandon);
        g.MapGet("/runs/{runId:int}/reusable-destination", GetReusableDestination);
        g.MapPost("/runs/{runId:int}/steps/{stepKey}/webhook", RegisterWebhook);
    }

    private static async Task<IResult> GetAvailable(
        [FromServices] RecipeService svc,
        HttpContext ctx)
    {
        try { return Results.Ok(await svc.GetAvailableRecipesAsync(GetUserId(ctx))); }
        catch (Exception ex) { return ServerError("retrieving available recipes", ex); }
    }

    private static async Task<IResult> Start(
        string key,
        [FromServices] RecipeService svc,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var run = await svc.StartRecipeAsync(key, GetUserId(ctx), cloneFromRunId: null, ct);
            var state = await svc.GetRunStateAsync(run.Id, ct);
            return Results.Created($"/api/recipes/runs/{run.Id}", state);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"starting recipe '{key}'", ex); }
    }

    private static async Task<IResult> Reconfigure(
        string key,
        [FromBody] ReconfigureRequest request,
        [FromServices] RecipeService svc,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var run = await svc.StartRecipeAsync(key, GetUserId(ctx), request.RunId, ct);
            var state = await svc.GetRunStateAsync(run.Id, ct);
            return Results.Created($"/api/recipes/runs/{run.Id}", state);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"reconfiguring recipe '{key}'", ex); }
    }

    private static async Task<IResult> GetRunState(
        int runId,
        [FromServices] RecipeService svc,
        CancellationToken ct)
    {
        try { return Results.Ok(await svc.GetRunStateAsync(runId, ct)); }
        catch (InvalidOperationException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"retrieving recipe run {runId}", ex); }
    }

    private static async Task<IResult> SaveStep(
        int runId,
        string stepKey,
        [FromBody] Dictionary<string, object?> stepParams,
        [FromServices] RecipeService svc,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var result = await svc.ExecuteStepAsync(runId, stepKey, stepParams, GetUserId(ctx), ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"saving step '{stepKey}' for run {runId}", ex); }
    }

    private static async Task<IResult> SkipStep(
        int runId,
        string stepKey,
        [FromServices] RecipeService svc,
        CancellationToken ct)
    {
        try
        {
            var result = await svc.SkipStepAsync(runId, stepKey, ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"skipping step '{stepKey}' for run {runId}", ex); }
    }

    private static async Task<IResult> SkipFlowGroup(
        int runId,
        string flowGroup,
        [FromServices] RecipeService svc,
        CancellationToken ct)
    {
        try
        {
            var result = await svc.SkipFlowGroupAsync(runId, Uri.UnescapeDataString(flowGroup), ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"skipping flow group '{flowGroup}' for run {runId}", ex); }
    }

    private static async Task<IResult> VerifyStep(
        int runId,
        string stepKey,
        [FromServices] RecipeService svc,
        CancellationToken ct)
    {
        try
        {
            var result = await svc.VerifyStepAsync(runId, stepKey, ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"verifying step '{stepKey}' for run {runId}", ex); }
    }

    private static async Task<IResult> Complete(
        int runId,
        [FromServices] RecipeService svc,
        CancellationToken ct)
    {
        try
        {
            await svc.CompleteRunAsync(runId, ct);
            return Results.Ok(new { message = "Recipe completed." });
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"completing recipe run {runId}", ex); }
    }

    private static async Task<IResult> Abandon(
        int runId,
        [FromServices] RecipeService svc,
        CancellationToken ct)
    {
        try
        {
            await svc.AbandonRunAsync(runId, ct);
            return Results.Ok(new { message = "Recipe abandoned." });
        }
        catch (Exception ex) { return ServerError($"abandoning recipe run {runId}", ex); }
    }

    private static async Task<IResult> GetReusableDestination(
        int runId,
        [FromServices] RecipeService svc,
        CancellationToken ct)
    {
        try
        {
            var destinationId = await svc.GetReusableDestinationIdAsync(runId, ct);
            return Results.Ok(new { destinationId });
        }
        catch (InvalidOperationException ex) { return Results.NotFound(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"retrieving reusable destination for run {runId}", ex); }
    }

    private static async Task<IResult> RegisterWebhook(
        int runId,
        string stepKey,
        [FromServices] RecipeService svc,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var (webhookId, url) = await svc.RegisterTrackingWebhookAsync(runId, ctx.Request.Scheme, ctx.Request.Host.ToString(), ct);
            return Results.Ok(new { webhookId, url });
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { return ServerError($"registering Tracking Link webhook for run {runId}", ex); }
    }

    private static int? GetUserId(HttpContext ctx)
    {
        var claim = ctx.User?.FindFirst("sub") ?? ctx.User?.FindFirst("userId");
        return claim is not null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    private static IResult ServerError(string context, Exception ex)
    {
        Log.Error(ex, "RecipesEndpoints: error {Context}", context);
        return Results.Problem($"Error {context}: {ex.Message}");
    }

    public class ReconfigureRequest
    {
        public required int RunId { get; set; }
    }
}
