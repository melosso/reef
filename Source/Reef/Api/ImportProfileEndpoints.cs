using Reef.Core.Abstractions;
using Reef.Core.Services.Import;
using Reef.Core.Models;
using ILogger = Serilog.ILogger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Dapper;

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

        // GET /api/imports/quarantine - Get all quarantined rows
        group.MapGet("/quarantine", GetAllQuarantined)
            .WithName("GetQuarantinedRows")
            .WithSummary("Get all quarantined rows");

        // GET /api/imports/quarantine/{id} - Get quarantined row by ID
        group.MapGet("/quarantine/{id}", GetQuarantinedById)
            .WithName("GetQuarantinedRow")
            .WithSummary("Get quarantined row by ID");
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

    private static async Task<IResult> GetExecutionStatus(
        int id,
        [FromServices] IConfiguration config)
    {
        try
        {
            var connectionString = config.GetConnectionString("Reef") ?? "Data Source=Reef.db;Cache=Shared";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT
                    e.Id,
                    e.ImportProfileId as ProfileId,
                    p.Name as ProfileName,
                    e.Status,
                    e.StartedAt as StartTime,
                    e.CompletedAt as EndTime,
                    e.ExecutionTimeMs,
                    e.RowsRead,
                    e.RowsWritten,
                    e.RowsSkipped,
                    e.RowsFailed,
                    e.DeltaSyncNewRows,
                    e.DeltaSyncChangedRows,
                    e.DeltaSyncUnchangedRows,
                    e.ErrorMessage,
                    e.CurrentStage,
                    e.TriggeredBy
                FROM ImportExecutions e
                LEFT JOIN ImportProfiles p ON e.ImportProfileId = p.Id
                WHERE e.Id = @Id";

            var execution = await connection.QueryFirstOrDefaultAsync(query, new { Id = id });

            if (execution == null)
            {
                return Results.NotFound(new { success = false, message = "Execution not found" });
            }

            return Results.Ok(new
            {
                success = true,
                data = execution
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting execution status {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get all execution histories for imports (extended endpoints)
    /// </summary>
    public static void MapImportExecutionHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/imports/executions")
            .WithTags("Imports - Execution History");

        // GET /api/imports/executions - List execution histories with pagination/filtering
        group.MapGet("/", GetAllExecutions)
            .WithName("GetImportExecutions")
            .WithSummary("Get all import executions with filters");

        // GET /api/imports/executions/{id}/details - Get detailed execution info
        group.MapGet("/{id}/details", GetExecutionDetails)
            .WithName("GetExecutionDetails")
            .WithSummary("Get detailed execution information");

        // GET /api/imports/executions/{id}/logs - Get execution logs
        group.MapGet("/{id}/logs", GetExecutionLogs)
            .WithName("GetExecutionLogs")
            .WithSummary("Get execution logs/audit trail");

        // GET /api/imports/executions/{id}/errors - Get errors from execution
        group.MapGet("/{id}/errors", GetExecutionErrors)
            .WithName("GetExecutionErrors")
            .WithSummary("Get errors from execution");
    }

    /// <summary>
    /// Get all executions
    /// </summary>
    private static async Task<IResult> GetAllExecutions(
        [FromServices] IConfiguration config,
        [FromQuery] int? profileId = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            var connectionString = config.GetConnectionString("Reef") ?? "Data Source=Reef.db;Cache=Shared";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var where = new List<string> { "1=1" };
            var parameters = new DynamicParameters();

            if (profileId.HasValue)
            {
                where.Add("ImportProfileId = @ProfileId");
                parameters.Add("@ProfileId", profileId.Value);
            }

            if (!string.IsNullOrEmpty(status))
            {
                where.Add("Status = @Status");
                parameters.Add("@Status", status);
            }

            var whereClause = string.Join(" AND ", where);
            var offset = (page - 1) * pageSize;

            // Get total count
            var countQuery = $"SELECT COUNT(*) FROM ImportExecutions WHERE {whereClause}";
            var totalCount = await connection.QueryFirstOrDefaultAsync<int>(countQuery, parameters);

            // Get paginated data with profile names
            var query = $@"
                SELECT
                    e.Id,
                    e.ImportProfileId as ProfileId,
                    p.Name as ProfileName,
                    e.Status,
                    e.StartedAt as StartTime,
                    e.CompletedAt as EndTime,
                    e.ExecutionTimeMs,
                    e.RowsRead,
                    e.RowsWritten,
                    e.RowsSkipped,
                    e.RowsFailed,
                    e.DeltaSyncNewRows,
                    e.DeltaSyncChangedRows,
                    e.DeltaSyncUnchangedRows,
                    e.ErrorMessage,
                    e.CurrentStage,
                    e.TriggeredBy
                FROM ImportExecutions e
                LEFT JOIN ImportProfiles p ON e.ImportProfileId = p.Id
                WHERE {whereClause}
                ORDER BY e.StartedAt DESC
                LIMIT @PageSize OFFSET @Offset";

            parameters.Add("@PageSize", pageSize);
            parameters.Add("@Offset", offset);

            var executions = await connection.QueryAsync(query, parameters);

            return Results.Ok(new
            {
                success = true,
                data = executions,
                total = totalCount,
                page = page,
                pageSize = pageSize,
                totalPages = (totalCount + pageSize - 1) / pageSize
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting executions");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get execution details
    /// </summary>
    private static async Task<IResult> GetExecutionDetails(int id, [FromServices] IConfiguration config)
    {
        try
        {
            var connectionString = config.GetConnectionString("Reef") ?? "Data Source=Reef.db;Cache=Shared";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT
                    e.Id,
                    e.ImportProfileId as ProfileId,
                    p.Name as ProfileName,
                    e.Status,
                    e.StartedAt as StartTime,
                    e.CompletedAt as EndTime,
                    e.ExecutionTimeMs,
                    e.RowsRead,
                    e.RowsWritten,
                    e.RowsSkipped,
                    e.RowsFailed,
                    e.DeltaSyncNewRows,
                    e.DeltaSyncChangedRows,
                    e.DeltaSyncUnchangedRows,
                    e.ErrorMessage,
                    e.ErrorDetails,
                    e.CurrentStage,
                    e.StageDetails,
                    e.TriggeredBy,
                    e.JobId
                FROM ImportExecutions e
                LEFT JOIN ImportProfiles p ON e.ImportProfileId = p.Id
                WHERE e.Id = @Id";

            var execution = await connection.QueryFirstOrDefaultAsync(query, new { Id = id });

            if (execution == null)
            {
                return Results.NotFound(new { success = false, message = "Execution not found" });
            }

            return Results.Ok(new
            {
                success = true,
                data = execution
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting execution details {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get execution logs
    /// </summary>
    private static async Task<IResult> GetExecutionLogs(int id, [FromServices] IConfiguration config)
    {
        try
        {
            var connectionString = config.GetConnectionString("Reef") ?? "Data Source=Reef.db;Cache=Shared";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT
                    e.Id,
                    e.Status,
                    e.CurrentStage as Stage,
                    e.StageDetails,
                    e.StartedAt as Timestamp,
                    e.ExecutionTimeMs,
                    'Pipeline execution log' as Message
                FROM ImportExecutions e
                WHERE e.Id = @Id";

            var execution = await connection.QueryFirstOrDefaultAsync(query, new { Id = id });

            if (execution == null)
            {
                return Results.NotFound(new { success = false, message = "Execution not found" });
            }

            var logs = new List<dynamic>
            {
                new
                {
                    timestamp = execution.Timestamp,
                    level = "info",
                    message = $"Execution started",
                    stage = "Initialize"
                },
                new
                {
                    timestamp = execution.Timestamp,
                    level = "info",
                    message = $"Current stage: {execution.Stage ?? "Unknown"}",
                    stage = execution.Stage
                },
                new
                {
                    timestamp = execution.Timestamp,
                    level = "info",
                    message = $"Execution {execution.Status.ToLower()}",
                    stage = "Complete"
                }
            };

            if (!string.IsNullOrEmpty(execution.StageDetails))
            {
                logs.Add(new
                {
                    timestamp = execution.Timestamp,
                    level = "debug",
                    message = execution.StageDetails,
                    stage = execution.Stage
                });
            }

            return Results.Ok(new
            {
                success = true,
                data = logs
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting execution logs {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get execution errors
    /// </summary>
    private static async Task<IResult> GetExecutionErrors(int id, [FromServices] IConfiguration config)
    {
        try
        {
            var connectionString = config.GetConnectionString("Reef") ?? "Data Source=Reef.db;Cache=Shared";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT
                    e.Id,
                    e.Status,
                    e.ErrorMessage,
                    e.ErrorDetails,
                    e.RowsFailed,
                    e.StartedAt as Timestamp
                FROM ImportExecutions e
                WHERE e.Id = @Id";

            var execution = await connection.QueryFirstOrDefaultAsync(query, new { Id = id });

            if (execution == null)
            {
                return Results.NotFound(new { success = false, message = "Execution not found" });
            }

            var errors = new List<dynamic>();

            if (!string.IsNullOrEmpty(execution.ErrorMessage))
            {
                errors.Add(new
                {
                    timestamp = execution.Timestamp,
                    type = "ExecutionError",
                    message = execution.ErrorMessage,
                    details = execution.ErrorDetails ?? string.Empty,
                    rowsAffected = execution.RowsFailed ?? 0
                });
            }

            return Results.Ok(new
            {
                success = true,
                data = errors,
                errorCount = errors.Count,
                failedRows = execution.RowsFailed ?? 0
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting execution errors {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get all quarantined rows
    /// </summary>
    private static async Task<IResult> GetAllQuarantined(
        [FromServices] IConfiguration config,
        [FromQuery] int? profileId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? errorType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            var connectionString = config.GetConnectionString("Reef") ?? "Data Source=Reef.db;Cache=Shared";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var where = new List<string> { "1=1" };
            var parameters = new DynamicParameters();

            if (profileId.HasValue)
            {
                where.Add("ImportProfileId = @ProfileId");
                parameters.Add("@ProfileId", profileId.Value);
            }

            if (!string.IsNullOrEmpty(status))
            {
                where.Add("Status = @Status");
                parameters.Add("@Status", status);
            }

            if (!string.IsNullOrEmpty(errorType))
            {
                where.Add("ErrorType = @ErrorType");
                parameters.Add("@ErrorType", errorType);
            }

            var whereClause = string.Join(" AND ", where);
            var offset = (page - 1) * pageSize;

            // Get total count
            var countQuery = $"SELECT COUNT(*) FROM ImportQuarantine WHERE {whereClause}";
            var totalCount = await connection.QueryFirstOrDefaultAsync<int>(countQuery, parameters);

            // Get paginated data with profile names
            var query = $@"
                SELECT
                    q.Id,
                    q.ImportExecutionId as ExecutionId,
                    q.ImportProfileId as ProfileId,
                    p.Name as ProfileName,
                    q.RowData,
                    q.ErrorType,
                    q.ErrorMessage,
                    q.ErrorDetails,
                    q.SourceRowNumber,
                    q.QuarantinedAt,
                    q.ReviewedAt,
                    q.ReviewedBy,
                    q.ReviewNotes,
                    q.Status,
                    q.ResolutionAction
                FROM ImportQuarantine q
                LEFT JOIN ImportProfiles p ON q.ImportProfileId = p.Id
                WHERE {whereClause}
                ORDER BY q.QuarantinedAt DESC
                LIMIT @PageSize OFFSET @Offset";

            parameters.Add("@PageSize", pageSize);
            parameters.Add("@Offset", offset);

            var quarantined = await connection.QueryAsync(query, parameters);

            return Results.Ok(new
            {
                success = true,
                data = quarantined,
                total = totalCount,
                page = page,
                pageSize = pageSize,
                totalPages = (totalCount + pageSize - 1) / pageSize
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting quarantined rows");
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Get quarantined row by ID
    /// </summary>
    private static async Task<IResult> GetQuarantinedById(int id, [FromServices] IConfiguration config)
    {
        try
        {
            var connectionString = config.GetConnectionString("Reef") ?? "Data Source=Reef.db;Cache=Shared";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var query = @"
                SELECT
                    q.Id,
                    q.ImportExecutionId as ExecutionId,
                    q.ImportProfileId as ProfileId,
                    p.Name as ProfileName,
                    q.RowData,
                    q.ErrorType,
                    q.ErrorMessage,
                    q.ErrorDetails,
                    q.SourceRowNumber,
                    q.QuarantinedAt,
                    q.ReviewedAt,
                    q.ReviewedBy,
                    q.ReviewNotes,
                    q.Status,
                    q.ResolutionAction
                FROM ImportQuarantine q
                LEFT JOIN ImportProfiles p ON q.ImportProfileId = p.Id
                WHERE q.Id = @Id";

            var quarantined = await connection.QueryFirstOrDefaultAsync(query, new { Id = id });

            if (quarantined == null)
            {
                return Results.NotFound(new { success = false, message = "Quarantined row not found" });
            }

            return Results.Ok(new
            {
                success = true,
                data = quarantined
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting quarantined row {Id}", id);
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
    }
}
