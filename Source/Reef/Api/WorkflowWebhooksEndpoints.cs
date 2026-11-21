using Microsoft.AspNetCore.Mvc;
using Reef.Core.Parsers;
using Reef.Core.PublicIds;
using Reef.Core.Services;
using Reef.Core.Workflows;
using Reef.Core.Workflows.Engine;
using Reef.Core.Workflows.Models;
using Serilog;

namespace Reef.Api;

public static class WorkflowWebhooksEndpoints
{
    private const int MaxRequestsPerHour = 100;

    public static void Map(WebApplication app)
    {
        app.MapPost("/api/workflows/{id:ulid(Workflows)}/webhooks/{node}/token", CreateToken).RequireAuthorization();
        app.MapPost("/webhooks/workflows/{token}", Trigger);
    }

    private static async Task<IResult> CreateToken(int id, string node, HttpContext context, [FromServices] WorkflowService workflows,
        [FromServices] AuditService audit, CancellationToken ct)
    {
        var token = WebhookService.GenerateWebhookToken();
        try
        {
            await workflows.SetWebhookTokenHashAsync(id, node, WebhookService.ComputeTokenHash(token), ct);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        await audit.LogAsync("Workflow", id, "WebhookTokenCreated", context.User.Identity?.Name ?? "Unknown", node, context);
        return Results.Ok(new { url = $"/webhooks/workflows/{token}", token });
    }

    // shortcut answers after the run ends, queue runs when callers time out
    private static async Task<IResult> Trigger(string token, HttpRequest request, [FromServices] WorkflowService workflows,
        [FromServices] WebhookService webhooks, [FromServices] WorkflowEngine engine, [FromServices] PublicIdMap ids, CancellationToken ct)
    {
        if (!WebhookService.IsValidTokenFormat(token) || await workflows.FindWebhookAsync(WebhookService.ComputeTokenHash(token), ct) is not var (workflow, node))
            return Results.NotFound(new { error = "webhook_not_found", message = "Webhook token is invalid or does not exist" });
        if (!workflow.Active)
            return Results.Json(new { error = "workflow_inactive", message = "Turn on the workflow to accept webhook calls" }, statusCode: StatusCodes.Status403Forbidden);
        if (!webhooks.CheckRateLimit($"workflow_{workflow.Id}_{node.Id}", MaxRequestsPerHour, 1))
            return Results.Json(new { error = "rate_limit_exceeded", message = $"Webhook allows {MaxRequestsPerHour} calls per hour" }, statusCode: StatusCodes.Status429TooManyRequests);

        WorkflowItems items;
        try
        {
            items = await ReadItemsAsync(request, ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = "invalid_body", message = $"Request body is not valid JSON: {ex.Message}" });
        }

        var run = await engine.RunAsync(workflow.Id!.Value, node.Id, items, ct);
        Log.Information("Webhook started workflow {WorkflowId} run {RunId}", workflow.Id, run.Id);
        return Results.Ok(new { executionId = ids.ToUlid("WorkflowExecutions", run.Id), status = run.Status, error = run.ErrorMessage });
    }

    private static async Task<WorkflowItems> ReadItemsAsync(HttpRequest request, CancellationToken ct)
    {
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, ct);
        if (body.Length == 0)
            return new WorkflowItems();
        body.Position = 0;
        var rows = new List<Dictionary<string, object?>>();
        await foreach (var row in ImportParserFactory.Create("JSON").ParseAsync(body, ImportRows.ParseFormatConfig(null), ct))
        {
            if (!string.IsNullOrWhiteSpace(row.ParseError))
                throw new InvalidOperationException(row.ParseError);
            rows.Add(row.Columns);
        }
        return WorkflowItems.FromRows(rows);
    }
}
