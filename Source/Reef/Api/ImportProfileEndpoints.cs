using Reef.Core.Abstractions;
using Reef.Core.Services.Import;
using Reef.Core.Models;
using ILogger = Serilog.ILogger;

namespace Reef.Api;

/// <summary>
/// Minimal API endpoints for import profile management
/// </summary>
public static class ImportProfileEndpoints
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ImportProfileEndpoints));

    public static void MapImportEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/imports")
            .WithTags("Imports");

        // GET /api/imports/profiles - Get all import profiles
        group.MapGet("/profiles", GetAllProfiles)
            .WithName("GetImportProfiles")
            .WithSummary("Get all import profiles");

        // GET /api/imports/profiles/{id} - Get import profile by ID
        group.MapGet("/profiles/{id}", GetImportProfileById)
            .WithName("GetImportProfile")
            .WithSummary("Get import profile by ID");

        // POST /api/imports/profiles - Create new import profile
        group.MapPost("/profiles", CreateImportProfile)
            .WithName("CreateImportProfile")
            .WithSummary("Create new import profile");

        // PUT /api/imports/profiles/{id} - Update import profile
        group.MapPut("/profiles/{id}", UpdateImportProfile)
            .WithName("UpdateImportProfile")
            .WithSummary("Update import profile");

        // DELETE /api/imports/profiles/{id} - Delete import profile
        group.MapDelete("/profiles/{id}", DeleteImportProfile)
            .WithName("DeleteImportProfile")
            .WithSummary("Delete import profile");

        // POST /api/imports/profiles/{id}/execute - Execute import
        group.MapPost("/profiles/{id}/execute", ExecuteImport)
            .WithName("ExecuteImport")
            .WithSummary("Execute import profile");

        // GET /api/imports/executions/{id} - Get import execution status
        group.MapGet("/executions/{id}", GetExecutionStatus)
            .WithName("GetExecutionStatus")
            .WithSummary("Get import execution status");
    }

    private static async Task<IResult> GetAllProfiles(IImportProfileService service)
    {
        try
        {
            var profiles = await service.GetAllAsync();
            return Results.Ok(new
            {
                success = true,
                data = profiles,
                count = profiles.Count
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting import profiles");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> GetImportProfileById(int id, IImportProfileService service)
    {
        try
        {
            var profile = await service.GetByIdAsync(id);
            if (profile == null)
                return Results.NotFound(new { success = false, message = "Profile not found" });

            return Results.Ok(new { success = true, data = profile });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting import profile {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> CreateImportProfile(
        ImportProfile profile,
        IImportProfileService service,
        HttpContext httpContext)
    {
        try
        {
            var createdBy = httpContext.User?.Identity?.Name ?? "System";
            var id = await service.CreateAsync(profile, createdBy);

            return Results.Created($"/api/imports/profiles/{id}", new
            {
                success = true,
                message = "Profile created successfully",
                id = id
            });
        }
        catch (ArgumentException ex)
        {
            Log.Warning(ex, "Validation error creating import profile");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating import profile");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> UpdateImportProfile(
        int id,
        ImportProfile profile,
        IImportProfileService service)
    {
        try
        {
            profile.Id = id;
            var updated = await service.UpdateAsync(profile);

            if (!updated)
                return Results.NotFound(new { success = false, message = "Profile not found" });

            return Results.Ok(new { success = true, message = "Profile updated successfully" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating import profile {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> DeleteImportProfile(
        int id,
        IImportProfileService service)
    {
        try
        {
            var deleted = await service.DeleteAsync(id);

            if (!deleted)
                return Results.NotFound(new { success = false, message = "Profile not found" });

            return Results.Ok(new { success = true, message = "Profile deleted successfully" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting import profile {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> ExecuteImport(
        int id,
        IImportProfileService profileService,
        IImportExecutionService executionService,
        HttpContext httpContext)
    {
        try
        {
            // Verify profile exists
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
                return Results.NotFound(new { success = false, message = "Profile not found" });

            // Execute import
            var triggeredBy = httpContext.User?.Identity?.Name ?? "System";
            var result = await executionService.ExecuteAsync(id, triggeredBy);

            return Results.Accepted($"/api/imports/executions/{result.ExecutionId}", new
            {
                success = true,
                message = "Import execution started",
                executionId = result.ExecutionId,
                status = result.Status
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing import {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    private static Task<IResult> GetExecutionStatus(
        int id,
        IImportExecutionService executionService)
    {
        try
        {
            // Note: In full implementation, this would query ImportExecutions table
            // For MVP, we'll return a placeholder
            return Task.FromResult<IResult>(Results.Ok(new
            {
                success = true,
                data = new
                {
                    id = id,
                    status = "Completed",
                    rowsRead = 0,
                    rowsWritten = 0
                }
            }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting execution status {Id}", id);
            return Task.FromResult<IResult>(Results.BadRequest(new { success = false, message = ex.Message }));
        }
    }
}
