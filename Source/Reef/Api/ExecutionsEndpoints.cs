// Source/Reef/Api/ExecutionsEndpoints.cs
// API endpoints for execution management and monitoring

using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;
using Serilog;

namespace Reef.Api;

/// <summary>
/// API endpoints for execution management and monitoring
/// Provides operations for triggering executions and viewing execution history
/// </summary>
public static class ExecutionsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/executions").RequireAuthorization();

    group.MapGet("/", GetAllExecutions);
    group.MapGet("/{id:int}", GetExecutionById);
    group.MapGet("/{id:int}/test", GetExecutionById);
    group.MapPost("/execute/{profileId:int}", ExecuteProfile);
    group.MapGet("/by-profile/{profileId:int}", GetExecutionsByProfile);
    group.MapGet("/recent", GetRecentExecutions);
    group.MapGet("/download/{token}", DownloadExecutionFile); 
    }
    
    /// <summary>
    /// GET /api/executions/download/{token} - Securely download execution output by token
    /// </summary>
    private static async Task<IResult> DownloadExecutionFile(
        string token,
        HttpContext context,
        [FromServices] ExecutionService service)
    {
        // Find execution by a secure, non-enumerable token (e.g., a GUID or hash stored in OutputPath or a separate column)
        // For this example, assume OutputPath contains a unique token as part of the path, e.g., exports/{date}/{token}/file.csv
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        string outputRoot;
        if (environment == "Development")
        {
            var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
            outputRoot = Path.Combine(projectDir, "exports");
        }
        else
        {
            outputRoot = Path.Combine(AppContext.BaseDirectory, "exports");
        }

        // Find the execution by token (search OutputPath for the token)
        var executions = await service.GetRecentExecutionsAsync(1000); // Or implement a direct lookup by token
        var execution = executions.FirstOrDefault(e => e.OutputPath != null && e.OutputPath.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (execution == null || string.IsNullOrWhiteSpace(execution.OutputPath))
            return Results.NotFound();

        // Authorization: Only allow the user who triggered the execution or an admin
        var user = context.User;
        var isAdmin = user.IsInRole("Admin");
        var username = user.Identity?.Name ?? "";
        if (!isAdmin && !string.Equals(username, execution.TriggeredBy, StringComparison.OrdinalIgnoreCase))
            return Results.Forbid();

        // Prevent path traversal
        var filePath = Path.GetFullPath(Path.Combine(outputRoot, Path.GetRelativePath("exports", execution.OutputPath)));
        if (!filePath.StartsWith(outputRoot))
            return Results.Forbid();

        if (!System.IO.File.Exists(filePath))
            return Results.NotFound();

        var fileName = Path.GetFileName(filePath);
        var contentType = "application/octet-stream";
        return Results.File(System.IO.File.OpenRead(filePath), contentType, fileName);
    }

    /// <summary>
    /// GET /api/executions - Get all executions with pagination and filtering
    /// </summary>
    private static async Task<IResult> GetAllExecutions(
        [FromServices] ExecutionService service,
        [FromQuery] int? profileId = null,
        [FromQuery] int? jobId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            if (page < 1)
            {
                return Results.BadRequest(new { error = "Page must be at least 1" });
            }

            if (pageSize < 1 || pageSize > 100)
            {
                return Results.BadRequest(new { error = "Page size must be between 1 and 100" });
            }

            var result = await service.GetExecutionsPagedAsync(
                profileId, jobId, status, fromDate, toDate, search, page, pageSize);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting executions");
            return Results.Problem("Error retrieving executions");
        }
    }

    /// <summary>
    /// GET /api/executions/{id} - Get execution by ID
    /// </summary>
    private static async Task<IResult> GetExecutionById(
        int id, 
        [FromServices] ExecutionService service)
    {
        try
        {
            var execution = await service.GetExecutionByIdAsync(id);
            return execution != null ? Results.Ok(execution) : Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting execution {Id}", id);
            return Results.Problem("Error retrieving execution");
        }
    }

    /// <summary>
    /// POST /api/executions/execute/{profileId} - Execute a profile
    /// Accepts optional JSON body with parameters and triggeredBy fields
    /// </summary>
    private static async Task<IResult> ExecuteProfile(
        int profileId,
        HttpContext context,
        [FromServices] ExecutionService service,
        [FromServices] ProfileService profileService,
        [FromServices] ConnectionService connectionService,
        [FromServices] AuditService auditService,
        [FromQuery] bool testMode = false,
        [FromQuery] int? localDestinationId = null,
        [FromBody] ExecuteProfileRequest? request = null)
    {
        try
        {
            var username = context.User.Identity?.Name ?? "Unknown";
            var triggeredBy = request?.TriggeredBy ?? username;

            // Validate profile exists and connection is active
            var profile = await profileService.GetByIdAsync(profileId);
            if (profile == null)
            {
                Log.Warning("Profile {ProfileId} not found", profileId);
                return Results.NotFound(new { message = "Profile not found" });
            }

            var connection = await connectionService.GetByIdAsync(profile.ConnectionId);
            if (connection == null)
            {
                Log.Warning("Connection {ConnectionId} for profile {ProfileId} not found",
                    profile.ConnectionId, profileId);
                return Results.BadRequest(new { message = "Connection not found" });
            }

            if (!connection.IsActive)
            {
                Log.Warning("Execution blocked: Connection {ConnectionId} ({ConnectionName}) is disabled",
                    connection.Id, connection.Name);

                await auditService.LogAsync("Profile", profileId, "ExecutionBlocked", username,
                    System.Text.Json.JsonSerializer.Serialize(new {
                        reason = "Connection is disabled",
                        connectionId = connection.Id,
                        connectionName = connection.Name
                    }),
                    context);

                return Results.BadRequest(new {
                    message = $"Cannot execute profile: Connection '{connection.Name}' is currently disabled"
                });
            }

            // For email profile testing: use local destination override instead of email
            int? destinationOverride = null;
            if (testMode && profile.IsEmailExport && localDestinationId.HasValue)
            {
                Log.Information("Test mode: Email profile {ProfileId} will use local destination {LocalDestinationId} instead of email",
                    profileId, localDestinationId);
                destinationOverride = localDestinationId.Value;
            }

            Log.Information("Triggering execution of profile {ProfileId} by {TriggeredBy}",
                profileId, triggeredBy);

            var (executionId, success, outputPath, errorMessage) = await service.ExecuteProfileAsync(
                profileId, request?.Parameters, triggeredBy, null, destinationOverride);

            if (!success)
            {
                Log.Warning("Profile execution failed: {ErrorMessage}", errorMessage);
                
                await auditService.LogAsync("Profile", profileId, "ExecutionFailed", username,
                    System.Text.Json.JsonSerializer.Serialize(new { executionId, errorMessage }), 
                    context);

                return Results.Ok(new 
                { 
                    executionId, 
                    status = "Failed", 
                    error = errorMessage 
                });
            }

            await auditService.LogAsync("Profile", profileId, "Executed", username,
                System.Text.Json.JsonSerializer.Serialize(new { executionId, outputPath }), 
                context);

            return Results.Ok(new 
            { 
                executionId, 
                status = "Success", 
                outputPath 
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing profile {ProfileId}", profileId);
            return Results.Problem("Error executing profile");
        }
    }

    /// <summary>
    /// GET /api/executions/by-profile/{profileId} - Get executions for a specific profile
    /// </summary>
    private static async Task<IResult> GetExecutionsByProfile(
        int profileId,
        [FromServices] ExecutionService service,
        [FromQuery] int limit = 50)
    {
        try
        {
            if (limit < 1 || limit > 500)
            {
                return Results.BadRequest(new { error = "Limit must be between 1 and 500" });
            }

            var executions = await service.GetExecutionsByProfileIdAsync(profileId, limit);
            return Results.Ok(executions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting executions for profile {ProfileId}", profileId);
            return Results.Problem("Error retrieving executions");
        }
    }

    /// <summary>
    /// GET /api/executions/recent - Get recent executions (last 100)
    /// </summary>
    private static async Task<IResult> GetRecentExecutions(
        [FromServices] ExecutionService service)
    {
        try
        {
            var executions = await service.GetRecentExecutionsAsync(100);
            return Results.Ok(executions);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting recent executions");
            return Results.Problem("Error retrieving recent executions");
        }
    }
}

/// <summary>
/// Request model for profile execution
/// </summary>
public class ExecuteProfileRequest
{
    public Dictionary<string, string>? Parameters { get; set; }
    public string? TriggeredBy { get; set; }
}