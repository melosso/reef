using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Reef.Core.ProfileDocuments;
using Reef.Core.ProfileDocuments.Catalogs;
using Reef.Core.ProfileDocuments.Exceptions;
using Reef.Core.ProfileDocuments.Models;
using Reef.Core.Services;
using Reef.Core.PublicIds;

namespace Reef.Api;

public static class ProfileDocumentsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/profile-documents").RequireAuthorization();

        group.MapGet("/{kind}/catalog", GetCatalog);
        group.MapGet("/{kind}/new", GetNew);
        group.MapGet("/{kind}/{id:ulid(kind)}", Get);
        group.MapPost("/{kind}", Create);
        group.MapPut("/{kind}/{id:ulid(kind)}", Update);
        group.MapPost("/{kind}/flatten", Flatten);
    }

    private static IResult GetCatalog(string kind) =>
        ProfileCatalogs.Find(kind) is { } catalog ? Results.Ok(catalog) : Results.NotFound();

    private static IResult GetNew(string kind, [FromServices] ProfileDocumentService service, [FromServices] ProfileDocumentIds documentIds) =>
        ProfileCatalogs.Find(kind) is { } catalog ? Results.Ok(documentIds.ToPublic(catalog, service.New(catalog))) : Results.NotFound();

    private static async Task<IResult> Get(string kind, int id, [FromServices] ProfileDocumentService service,
        [FromServices] ProfileDocumentIds documentIds, CancellationToken ct)
    {
        if (ProfileCatalogs.Find(kind) is not { } catalog)
            return Results.NotFound();
        var document = await service.GetAsync(catalog, id, ct);
        return document is null ? Results.NotFound() : Results.Ok(documentIds.ToPublic(catalog, document));
    }

    private static async Task<IResult> Flatten(string kind, [FromQuery] string? id, ProfileDocument document,
        [FromServices] ProfileDocumentService service, [FromServices] ProfileDocumentIds documentIds, [FromServices] PublicIdMap ids, CancellationToken ct)
    {
        if (ProfileCatalogs.Find(kind) is not { } catalog)
            return Results.NotFound();
        var table = catalog.Kind == ProfileKinds.Import ? PublicIdTables.ImportProfiles : PublicIdTables.Profiles;
        long internalId = 0;
        if (id is not null && !ids.TryToId(table, id, out internalId))
            return Results.NotFound();
        try
        {
            var entity = await service.FlattenAsync(catalog, id is null ? null : (int)internalId, documentIds.ToInternal(catalog, document), ct);
            return Results.Ok(documentIds.ToPublic(entity, table));
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static Task<IResult> Create(string kind, ProfileDocument document, HttpContext context,
        [FromServices] ProfileDocumentService service, [FromServices] ProfileDocumentIds documentIds, [FromServices] AuditService audit, CancellationToken ct) =>
        Save(kind, null, document, context, service, documentIds, audit, ct);

    private static Task<IResult> Update(string kind, int id, ProfileDocument document, HttpContext context,
        [FromServices] ProfileDocumentService service, [FromServices] ProfileDocumentIds documentIds, [FromServices] AuditService audit, CancellationToken ct) =>
        Save(kind, id, document, context, service, documentIds, audit, ct);

    private static async Task<IResult> Save(string kind, int? id, ProfileDocument document, HttpContext context,
        ProfileDocumentService service, ProfileDocumentIds documentIds, AuditService audit, CancellationToken ct)
    {
        if (ProfileCatalogs.Find(kind) is not { } catalog)
            return Results.NotFound();

        var userId = int.TryParse(context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
        try
        {
            var saved = documentIds.ToPublic(catalog, await service.SaveAsync(catalog, id, documentIds.ToInternal(catalog, document), userId, ct));
            await audit.LogAsync(catalog.Kind == ProfileKinds.Import ? "ImportProfile" : "Profile", saved.Id ?? 0,
                id is null ? "Created" : "Updated", context.User.Identity?.Name ?? "Unknown", (string?)null, context);
            return id is null ? Results.Created($"/api/profile-documents/{catalog.Kind}", saved) : Results.Ok(saved);
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
