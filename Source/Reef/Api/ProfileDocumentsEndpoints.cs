using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Reef.Core.ProfileDocuments;
using Reef.Core.ProfileDocuments.Catalogs;
using Reef.Core.ProfileDocuments.Exceptions;
using Reef.Core.ProfileDocuments.Models;
using Reef.Core.Services;

namespace Reef.Api;

public static class ProfileDocumentsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/profile-documents").RequireAuthorization();

        group.MapGet("/{kind}/catalog", GetCatalog);
        group.MapGet("/{kind}/new", GetNew);
        group.MapGet("/{kind}/{id:int}", Get);
        group.MapPost("/{kind}", Create);
        group.MapPut("/{kind}/{id:int}", Update);
        group.MapPost("/{kind}/flatten", Flatten);
    }

    private static IResult GetCatalog(string kind) =>
        ProfileCatalogs.Find(kind) is { } catalog ? Results.Ok(catalog) : Results.NotFound();

    private static IResult GetNew(string kind, [FromServices] ProfileDocumentService service) =>
        ProfileCatalogs.Find(kind) is { } catalog ? Results.Ok(service.New(catalog)) : Results.NotFound();

    private static async Task<IResult> Get(string kind, int id, [FromServices] ProfileDocumentService service, CancellationToken ct)
    {
        if (ProfileCatalogs.Find(kind) is not { } catalog)
            return Results.NotFound();
        var document = await service.GetAsync(catalog, id, ct);
        return document is null ? Results.NotFound() : Results.Ok(document);
    }

    private static async Task<IResult> Flatten(string kind, [FromQuery] int? id, ProfileDocument document,
        [FromServices] ProfileDocumentService service, CancellationToken ct)
    {
        if (ProfileCatalogs.Find(kind) is not { } catalog)
            return Results.NotFound();
        try
        {
            return Results.Ok(await service.FlattenAsync(catalog, id, document, ct));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static Task<IResult> Create(string kind, ProfileDocument document, HttpContext context,
        [FromServices] ProfileDocumentService service, [FromServices] AuditService audit, CancellationToken ct) =>
        Save(kind, null, document, context, service, audit, ct);

    private static Task<IResult> Update(string kind, int id, ProfileDocument document, HttpContext context,
        [FromServices] ProfileDocumentService service, [FromServices] AuditService audit, CancellationToken ct) =>
        Save(kind, id, document, context, service, audit, ct);

    private static async Task<IResult> Save(string kind, int? id, ProfileDocument document, HttpContext context,
        ProfileDocumentService service, AuditService audit, CancellationToken ct)
    {
        if (ProfileCatalogs.Find(kind) is not { } catalog)
            return Results.NotFound();

        var userId = int.TryParse(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
        try
        {
            var saved = await service.SaveAsync(catalog, id, document, userId, ct);
            await audit.LogAsync(catalog.Kind == ProfileKinds.Import ? "ImportProfile" : "Profile", saved.Id ?? 0,
                id is null ? "Created" : "Updated", context.User.Identity?.Name ?? "Unknown", (string?)null, context);
            return id is null ? Results.Created($"/api/profile-documents/{catalog.Kind}/{saved.Id}", saved) : Results.Ok(saved);
        }
        catch (ProfileDocumentValidationException ex)
        {
            return Results.BadRequest(new { error = ex.Message, errors = ex.Errors });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message, errors = new[] { new ProfileDocumentError(ex.Message) } });
        }
    }
}
