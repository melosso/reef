using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Reef.Core.Nodes.Exceptions;
using Reef.Core.Security;
using Reef.Core.Services;
using Reef.Core.Workflows;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Engine;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Api;

public static class WorkflowsEndpoints
{
    private const int TestRows = 100;

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/workflows").RequireAuthorization();

        group.MapGet("/", List);
        group.MapGet("/catalog", () => Results.Ok(WorkflowNodeCatalog.Catalog));
        group.MapGet("/{id:ulid(Workflows)}", Get);
        group.MapPost("/", Create);
        group.MapPut("/{id:ulid(Workflows)}", Update);
        group.MapDelete("/{id:ulid(Workflows)}", Delete);
        group.MapPost("/{id:ulid(Workflows)}/run", Run);
        group.MapPost("/{id:ulid(Workflows)}/nodes/{node}/test", Test);
        group.MapGet("/{id:ulid(Workflows)}/executions", (int id, [FromServices] WorkflowRunStore runs, CancellationToken ct) => runs.ListAsync(id, 50, ct));
        app.MapGet("/api/workflow-executions", async ([FromQuery] int? workflowId, [FromQuery] string? status, [FromServices] WorkflowRunStore runs, CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int pageSize = 25) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var (data, total) = await runs.PageAsync(page, pageSize, workflowId, string.IsNullOrWhiteSpace(status) ? null : status, ct);
            return Results.Ok(new { data, totalCount = total, page, pageSize, totalPages = Math.Max(1, (total + pageSize - 1) / pageSize) });
        }).RequireAuthorization();
        app.MapGet("/api/workflow-executions/{id:ulid(WorkflowExecutions)}", async (int id, [FromServices] WorkflowRunStore runs, CancellationToken ct) =>
            await runs.GetAsync(id, ct) is { } run ? Results.Ok(run) : Results.NotFound()).RequireAuthorization();
    }


    private static async Task<IResult> Run(int id, [FromQuery] string? trigger, HttpContext context, [FromServices] WorkflowEngine engine,
        [FromServices] AuditService audit)
    {
        try
        {
            var run = await engine.RunAsync(id, trigger, ct: CancellationToken.None);
            await audit.LogAsync("Workflow", id, "Run", context.User.Identity?.Name ?? "Unknown", (string?)null, context);
            return Results.Ok(run);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> Test(int id, string node, HttpRequest request, [FromServices] NodeTester tester, CancellationToken ct)
    {
        try
        {
            var result = await tester.TestAsync(id, node, await JsonItems.ReadAsync(request.Body, ct), ct);
            return Results.Ok(new
            {
                outputs = result.Outputs.ToDictionary(o => o.Key, o => new { o.Value.Columns, Items = SampleRows.Take(o.Value.Items, TestRows), o.Value.Files, Total = o.Value.Items.Count }),
                warning = result.Warning
            });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is InvalidOperationException or NodeExecutionException or System.Text.Json.JsonException)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> List([FromServices] WorkflowService service, [FromServices] WorkflowIds ids, CancellationToken ct) =>
        Results.Ok((await service.ListAsync(ct)).Select(ids.ToPublic));

    private static async Task<IResult> Get(int id, [FromServices] WorkflowService service, [FromServices] WorkflowIds ids, CancellationToken ct) =>
        await service.GetAsync(id, ct) is { } workflow ? Results.Ok(ids.ToPublic(workflow)) : Results.NotFound();

    private static Task<IResult> Create(WorkflowDocument document, HttpContext context, [FromServices] WorkflowService service,
        [FromServices] WorkflowIds ids, [FromServices] AuditService audit, [FromServices] PrivilegedSettingsGuard guard, CancellationToken ct) =>
        Save(null, document, context, service, ids, audit, guard, ct);

    private static Task<IResult> Update(int id, WorkflowDocument document, HttpContext context, [FromServices] WorkflowService service,
        [FromServices] WorkflowIds ids, [FromServices] AuditService audit, [FromServices] PrivilegedSettingsGuard guard, CancellationToken ct) =>
        Save(id, document, context, service, ids, audit, guard, ct);

    private static async Task<IResult> Delete(int id, HttpContext context, [FromServices] WorkflowService service, [FromServices] AuditService audit, CancellationToken ct)
    {
        if (!await service.DeleteAsync(id, ct))
            return Results.NotFound();
        await audit.LogAsync("Workflow", id, "Deleted", context.User.Identity?.Name ?? "Unknown", (string?)null, context);
        return Results.NoContent();
    }

    private static async Task<IResult> Save(int? id, WorkflowDocument document, HttpContext context, WorkflowService service,
        WorkflowIds ids, AuditService audit, PrivilegedSettingsGuard guard, CancellationToken ct)
    {
        var userId = int.TryParse(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
        try
        {
            guard.Check(document, id is null ? null : await service.GetAsync(id.Value, ct));
            var saved = await service.SaveAsync(id, ids.ToInternal(document), userId, ct);
            await audit.LogAsync("Workflow", saved.Id ?? 0, id is null ? "Created" : "Updated", context.User.Identity?.Name ?? "Unknown", (string?)null, context);
            return id is null ? Results.Created("/api/workflows", ids.ToPublic(saved)) : Results.Ok(ids.ToPublic(saved));
        }
        catch (NodeValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex) when (UniqueName.Violated(ex))
        {
            return UniqueName.Conflict(ex);
        }
    }
}
