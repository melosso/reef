using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;
using Serilog;

namespace Reef.Api;

/// <summary>
/// API endpoints for managing profile groups (folders/categories)
/// </summary>
public static class GroupsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/groups").RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapGet("/tree", GetTree);
        group.MapGet("/{id:int}", GetById);
        group.MapGet("/{id:int}/profiles", GetProfiles);
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapPost("/{id:int}/move", MoveGroup);
        group.MapDelete("/{id:int}", Delete);
    }

    /// <summary>
    /// GET /api/groups - Get all profile groups
    /// </summary>
    private static async Task<IResult> GetAll([FromServices] GroupService service)
    {
        try
        {
            var groups = await service.GetAllAsync();
            return Results.Ok(groups);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving profile groups");
            return Results.Problem("Error retrieving profile groups");
        }
    }

    /// <summary>
    /// GET /api/groups/tree - Get hierarchical tree structure
    /// </summary>
    private static async Task<IResult> GetTree([FromServices] GroupService service)
    {
        try
        {
            var tree = await service.GetTreeAsync();
            return Results.Ok(tree);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving group tree");
            return Results.Problem("Error retrieving group tree");
        }
    }

    /// <summary>
    /// GET /api/groups/{id} - Get specific profile group
    /// </summary>
    private static async Task<IResult> GetById(
        int id,
        [FromServices] GroupService service)
    {
        try
        {
            var group = await service.GetByIdAsync(id);
            
            if (group == null)
            {
                return Results.NotFound();
            }

            return Results.Ok(group);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving profile group {Id}", id);
            return Results.Problem("Error retrieving profile group");
        }
    }

    /// <summary>
    /// GET /api/groups/{id}/profiles - Get all profiles in a group
    /// </summary>
    private static async Task<IResult> GetProfiles(
        int id,
        [FromServices] GroupService service)
    {
        try
        {
            var group = await service.GetByIdAsync(id);
            if (group == null)
            {
                return Results.NotFound();
            }

            var profiles = await service.GetProfilesInGroupAsync(id);
            return Results.Ok(profiles);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving profiles for group {Id}", id);
            return Results.Problem("Error retrieving profiles for group");
        }
    }

    /// <summary>
    /// POST /api/groups - Create new profile group
    /// Body: { "name": "Production", "parentId": null, "description": "...", "sortOrder": 0 }
    /// </summary>
    private static async Task<IResult> Create(
        HttpContext context,
        [FromBody] CreateGroupRequest request,
        [FromServices] GroupService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Group name is required" });
            }

            if (request.Name.Length > 100)
            {
                return Results.BadRequest(new { error = "Group name must be 100 characters or less" });
            }

            var group = new ProfileGroup
            {
                Name = request.Name.Trim(),
                ParentId = request.ParentId,
                Description = request.Description?.Trim(),
                SortOrder = request.SortOrder
            };

            var username = context.User.Identity?.Name ?? "Unknown";
            var groupId = await service.CreateAsync(group, username);

            // Audit log
            var changes = System.Text.Json.JsonSerializer.Serialize(request);
            await auditService.LogAsync("ProfileGroup", groupId, "Created", username, changes, context);

            return Results.Ok(new { id = groupId, message = "Group created successfully" });
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid group creation request");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating profile group");
            return Results.Problem("Error creating profile group");
        }
    }

    /// <summary>
    /// PUT /api/groups/{id} - Update profile group
    /// Body: { "name": "Updated Name", "parentId": 1, "description": "...", "sortOrder": 5 }
    /// </summary>
    private static async Task<IResult> Update(
        int id,
        HttpContext context,
        [FromBody] UpdateGroupRequest request,
        [FromServices] GroupService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "Group name is required" });
            }

            if (request.Name.Length > 100)
            {
                return Results.BadRequest(new { error = "Group name must be 100 characters or less" });
            }

            var existing = await service.GetByIdAsync(id);
            if (existing == null)
            {
                return Results.NotFound();
            }

            existing.Name = request.Name.Trim();
            existing.ParentId = request.ParentId;
            existing.Description = request.Description?.Trim();
            existing.SortOrder = request.SortOrder;

            var success = await service.UpdateAsync(existing);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                var changes = System.Text.Json.JsonSerializer.Serialize(request);
                await auditService.LogAsync("ProfileGroup", id, "Updated", username, changes, context);
                
                return Results.Ok(new { message = "Group updated successfully" });
            }

            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid group update request for {Id}", id);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating profile group {Id}", id);
            return Results.Problem("Error updating profile group");
        }
    }

    /// <summary>
    /// POST /api/groups/{id}/move - Move group to new parent
    /// Body: { "parentId": 5 } or { "parentId": null }
    /// </summary>
    private static async Task<IResult> MoveGroup(
        int id,
        HttpContext context,
        [FromBody] MoveGroupRequest request,
        [FromServices] GroupService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            var success = await service.MoveToGroupAsync(id, request.ParentId);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                var changes = System.Text.Json.JsonSerializer.Serialize(request);
                await auditService.LogAsync("ProfileGroup", id, "Moved", username, changes, context);
                
                return Results.Ok(new { message = "Group moved successfully" });
            }

            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid move operation for group {Id}", id);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error moving profile group {Id}", id);
            return Results.Problem("Error moving profile group");
        }
    }

    /// <summary>
    /// DELETE /api/groups/{id} - Delete profile group
    /// </summary>
    private static async Task<IResult> Delete(
        int id,
        HttpContext context,
        [FromServices] GroupService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            var success = await service.DeleteAsync(id);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("ProfileGroup", id, "Deleted", username, null, context);
                
                return Results.Ok(new { message = "Group deleted successfully" });
            }

            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Cannot delete group {Id}", id);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting profile group {Id}", id);
            return Results.Problem("Error deleting profile group");
        }
    }
}

/// <summary>
/// Request model for creating a profile group
/// </summary>
public record CreateGroupRequest
{
    public required string Name { get; init; }
    public int? ParentId { get; init; }
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// Request model for updating a profile group
/// </summary>
public record UpdateGroupRequest
{
    public required string Name { get; init; }
    public int? ParentId { get; init; }
    public string? Description { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// Request model for moving a group to a new parent
/// </summary>
public record MoveGroupRequest
{
    public int? ParentId { get; init; }
}