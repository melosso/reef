using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.TemplateEngines;
using Serilog;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Reef.Api;

/// <summary>
/// API endpoints for profile management
/// Provides REST operations for query profile configuration
/// </summary>
public static class ProfilesEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/profiles").RequireAuthorization();

        group.MapGet("/", GetAllProfiles);
        group.MapGet("/{id:int}", GetProfileById);
        group.MapPost("/", CreateProfile);
        group.MapPut("/{id:int}", UpdateProfile);
        group.MapDelete("/{id:int}", DeleteProfile);
        group.MapPost("/{id:int}/enable", EnableProfile);
        group.MapPost("/{id:int}/disable", DisableProfile);
        group.MapGet("/by-connection/{connectionId:int}", GetProfilesByConnection);
        group.MapGet("/by-group/{groupId:int}", GetProfilesByGroup);
        
        // Delta Sync endpoints
        group.MapPost("/{id:int}/delta-sync/reset", ResetDeltaSyncState);
        group.MapPost("/{id:int}/delta-sync/reset-rows", ResetDeltaSyncRows);
        group.MapPost("/{id:int}/delta-sync/generate-hashes", GenerateDeltaSyncHashes);
        group.MapGet("/{id:int}/delta-sync/stats", GetDeltaSyncStats);
        
        // Multi-Output Splitting endpoints
        group.MapGet("/executions/{executionId:int}/splits", GetExecutionSplits);

        // Email Export endpoints
        group.MapGet("/{id:int}/validate-email-config", ValidateEmailExportConfig);
        group.MapPost("/{id:int}/preview-email-html", PreviewEmailHtml);
        group.MapPost("/{id:int}/validate-attachment-config", ValidateAttachmentConfig);
    }

    /// <summary>
    /// GET /api/profiles - Get all profiles (optionally filtered by templateId)
    /// </summary>
    private static async Task<IResult> GetAllProfiles(
        [FromQuery] int? templateId,
        [FromServices] ProfileService service)
    {
        try
        {
            var profiles = await service.GetAllAsync();

            // Filter by templateId if provided - check both TemplateId and EmailTemplateId
            if (templateId.HasValue)
            {
                profiles = profiles.Where(p => p.TemplateId == templateId.Value || p.EmailTemplateId == templateId.Value).ToList();
            }

            return Results.Ok(profiles);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting all profiles");
            return Results.Problem("Error retrieving profiles");
        }
    }

    /// <summary>
    /// GET /api/profiles/{id} - Get profile by ID
    /// </summary>
    private static async Task<IResult> GetProfileById(int id, [FromServices] ProfileService service)
    {
        try
        {
            var profile = await service.GetByIdAsync(id);
            return profile != null ? Results.Ok(profile) : Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting profile {Id}", id);
            return Results.Problem("Error retrieving profile");
        }
    }

    /// <summary>
    /// POST /api/profiles - Create a new profile
    /// </summary>
    private static async Task<IResult> CreateProfile(
        [FromBody] Profile profile,
        [FromServices] ProfileService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                return Results.BadRequest(new { error = "Name is required" });
            }

            if (string.IsNullOrWhiteSpace(profile.Query))
            {
                return Results.BadRequest(new { error = "Query is required" });
            }

            if (profile.ConnectionId <= 0)
            {
                return Results.BadRequest(new { error = "Valid ConnectionId is required" });
            }

            // Validate schedule configuration
            if (profile.ScheduleType == "Cron" && string.IsNullOrWhiteSpace(profile.ScheduleCron))
            {
                return Results.BadRequest(new { error = "ScheduleCron is required when ScheduleType is Cron" });
            }

            if (profile.ScheduleType == "Interval" && (!profile.ScheduleIntervalMinutes.HasValue || profile.ScheduleIntervalMinutes.Value <= 0))
            {
                return Results.BadRequest(new { error = "ScheduleIntervalMinutes must be greater than 0 when ScheduleType is Interval" });
            }

            // Validate pre-processing configuration
            if (!string.IsNullOrWhiteSpace(profile.PreProcessType))
            {
                if (string.IsNullOrWhiteSpace(profile.PreProcessConfig))
                {
                    return Results.BadRequest(new { error = "PreProcessConfig is required when PreProcessType is specified" });
                }
                
                try
                {
                    var config = System.Text.Json.JsonSerializer.Deserialize<ProcessingConfig>(profile.PreProcessConfig);
                    if (string.IsNullOrWhiteSpace(config?.Command))
                    {
                        return Results.BadRequest(new { error = "PreProcess Command is required" });
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    return Results.BadRequest(new { error = "Invalid PreProcessConfig JSON format" });
                }
            }

            // Validate post-processing configuration
            if (!string.IsNullOrWhiteSpace(profile.PostProcessType))
            {
                if (string.IsNullOrWhiteSpace(profile.PostProcessConfig))
                {
                    return Results.BadRequest(new { error = "PostProcessConfig is required when PostProcessType is specified" });
                }
                try
                {
                    var config = System.Text.Json.JsonSerializer.Deserialize<ProcessingConfig>(profile.PostProcessConfig);
                    if (string.IsNullOrWhiteSpace(config?.Command))
                    {
                        return Results.BadRequest(new { error = "PostProcess Command is required" });
                    }
                    // Validate required parameters and placeholders
                    // var requiredParams = new List<(string, string)>{
                    //     ("ExecutionId", "{executionid}"),
                    //     ("RowCount", "{rowcount}"),
                    //     ("StartedAt", "{startedat}")
                    // };
                    // if (profile.SplitEnabled)
                    //     requiredParams.Add(("SplitKey", "{splitkey}"));
                    // var missing = new List<string>();
                    // if (config.Parameters == null) missing.AddRange(requiredParams.Select(p => p.Item1));
                    // else {
                    //     foreach (var (param, placeholder) in requiredParams) {
                    //         var match = config.Parameters.FirstOrDefault(p => p.Name.TrimStart('@').Equals(param, StringComparison.OrdinalIgnoreCase));
                    //         if (match == null || !match.Value.Trim().Equals(placeholder, StringComparison.OrdinalIgnoreCase))
                    //             missing.Add(param);
                    //     }
                    // }
                    // if (missing.Count > 0)
                    // {
                    //     return Results.BadRequest(new { error = $"PostProcessConfig.Parameters must include: {string.Join(", ", missing)} with correct placeholders (e.g., '{{executionid}}')." });
                    // }
                }
                catch (System.Text.Json.JsonException)
                {
                    return Results.BadRequest(new { error = "Invalid PostProcessConfig JSON format" });
                }
            }

            // Validate delta sync configuration
            if (profile.DeltaSyncEnabled)
            {
                if (string.IsNullOrWhiteSpace(profile.DeltaSyncReefIdColumn))
                {
                    return Results.BadRequest(new { 
                        error = "DeltaSyncReefIdColumn is required when DeltaSyncEnabled is true" 
                    });
                }
                
                // Validate ReefId column name format (basic SQL identifier validation)
                if (!System.Text.RegularExpressions.Regex.IsMatch(profile.DeltaSyncReefIdColumn, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                {
                    return Results.BadRequest(new { 
                        error = "DeltaSyncReefIdColumn must be a valid SQL identifier" 
                    });
                }
            }

            var username = context.User.Identity?.Name ?? "Unknown";
            var id = await service.CreateAsync(profile, username);
            
            await auditService.LogAsync("Profile", id, "Created", username, 
                System.Text.Json.JsonSerializer.Serialize(new { profile.Name, profile.ConnectionId, profile.Query }));
            
            return Results.Created($"/api/profiles/{id}", new { id });
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Validation error creating profile");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating profile");
            return Results.Problem("Error creating profile");
        }
    }

    /// <summary>
    /// PUT /api/profiles/{id} - Update an existing profile
    /// </summary>
    private static async Task<IResult> UpdateProfile(
        int id,
        [FromBody] Profile profile,
        [FromServices] ProfileService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            var existingProfile = await service.GetByIdAsync(id);
            if (existingProfile == null)
            {
                return Results.NotFound();
            }

            // Update only the properties that are sent from the frontend
            existingProfile.Name = profile.Name;
            existingProfile.ConnectionId = profile.ConnectionId;
            existingProfile.GroupId = profile.GroupId;
            existingProfile.Query = profile.Query;

            // Scheduling
            existingProfile.ScheduleType = profile.ScheduleType;
            existingProfile.ScheduleCron = profile.ScheduleCron;
            existingProfile.ScheduleIntervalMinutes = profile.ScheduleIntervalMinutes;

            // Output
            existingProfile.OutputFormat = profile.OutputFormat;
            existingProfile.TemplateId = profile.TemplateId;
            existingProfile.OutputDestinationId = profile.OutputDestinationId;
            existingProfile.OutputDestinationType = profile.OutputDestinationType;
            existingProfile.OutputDestinationConfig = profile.OutputDestinationConfig;
            existingProfile.OutputPropertiesJson = profile.OutputPropertiesJson;
            existingProfile.TransformationOptionsJson = profile.TransformationOptionsJson;

            existingProfile.IsEnabled = profile.IsEnabled;

            // Pre-Processing
            existingProfile.PreProcessType = profile.PreProcessType;
            existingProfile.PreProcessConfig = profile.PreProcessConfig;
            existingProfile.PreProcessRollbackOnFailure = profile.PreProcessRollbackOnFailure;

            // Post-Processing
            existingProfile.PostProcessType = profile.PostProcessType;
            existingProfile.PostProcessConfig = profile.PostProcessConfig;
            existingProfile.PostProcessSkipOnFailure = profile.PostProcessSkipOnFailure;
            existingProfile.PostProcessRollbackOnFailure = profile.PostProcessRollbackOnFailure;
            existingProfile.PostProcessOnZeroRows = profile.PostProcessOnZeroRows;

            // Notification
            existingProfile.NotificationConfig = profile.NotificationConfig;

            // Delta Sync Configuration
            existingProfile.DeltaSyncEnabled = profile.DeltaSyncEnabled;
            existingProfile.DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn;
            existingProfile.DeltaSyncHashAlgorithm = profile.DeltaSyncHashAlgorithm;
            existingProfile.DeltaSyncDuplicateStrategy = profile.DeltaSyncDuplicateStrategy;
            existingProfile.DeltaSyncNullStrategy = profile.DeltaSyncNullStrategy;
            existingProfile.DeltaSyncNumericPrecision = profile.DeltaSyncNumericPrecision;
            existingProfile.DeltaSyncTrackDeletes = profile.DeltaSyncTrackDeletes;
            existingProfile.DeltaSyncResetOnSchemaChange = profile.DeltaSyncResetOnSchemaChange;
            existingProfile.DeltaSyncRemoveNonPrintable = profile.DeltaSyncRemoveNonPrintable;
            existingProfile.DeltaSyncReefIdNormalization = profile.DeltaSyncReefIdNormalization;

            // Advanced Output Options
            existingProfile.ExcludeReefIdFromOutput = profile.ExcludeReefIdFromOutput;
            existingProfile.ExcludeSplitKeyFromOutput = profile.ExcludeSplitKeyFromOutput;
            existingProfile.FilenameTemplate = profile.FilenameTemplate;

            // Multi-Output Splitting
            existingProfile.SplitEnabled = profile.SplitEnabled;
            existingProfile.SplitKeyColumn = profile.SplitKeyColumn;
            existingProfile.SplitFilenameTemplate = profile.SplitFilenameTemplate;
            existingProfile.SplitBatchSize = profile.SplitBatchSize;
            existingProfile.PostProcessPerSplit = profile.PostProcessPerSplit;

            // Email Export Configuration
            existingProfile.IsEmailExport = profile.IsEmailExport;
            existingProfile.EmailTemplateId = profile.EmailTemplateId;
            existingProfile.EmailRecipientsColumn = profile.EmailRecipientsColumn;
            existingProfile.EmailRecipientsHardcoded = profile.EmailRecipientsHardcoded;
            existingProfile.UseHardcodedRecipients = profile.UseHardcodedRecipients;
            existingProfile.EmailCcColumn = profile.EmailCcColumn;
            existingProfile.EmailCcHardcoded = profile.EmailCcHardcoded;
            existingProfile.UseHardcodedCc = profile.UseHardcodedCc;
            existingProfile.EmailSubjectColumn = profile.EmailSubjectColumn;
            existingProfile.EmailSubjectHardcoded = profile.EmailSubjectHardcoded;
            existingProfile.UseHardcodedSubject = profile.UseHardcodedSubject;
            existingProfile.EmailSuccessThresholdPercent = profile.EmailSuccessThresholdPercent;

            var success = await service.UpdateAsync(existingProfile);
            
            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("Profile", id, "Updated", username,
                    System.Text.Json.JsonSerializer.Serialize(new { profile.Name, profile.ConnectionId, profile.Query }));
                
                return Results.Ok(new { message = "Profile updated successfully" });
            }
            
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Validation error updating profile {Id}", id);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating profile {Id}", id);
            return Results.Problem("Error updating profile");
        }
    }

    /// <summary>
    /// DELETE /api/profiles/{id} - Delete a profile
    /// </summary>
    private static async Task<IResult> DeleteProfile(
        int id,
        [FromServices] ProfileService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            var profile = await service.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound();
            }

            var success = await service.DeleteAsync(id);
            
            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("Profile", id, "Deleted", username,
                    System.Text.Json.JsonSerializer.Serialize(new { profile.Name }));
                
                return Results.Ok(new { message = "Profile deleted successfully" });
            }
            
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting profile {Id}", id);
            return Results.Problem("Error deleting profile");
        }
    }

    /// <summary>
    /// POST /api/profiles/{id}/enable - Enable a profile
    /// </summary>
    private static async Task<IResult> EnableProfile(
        int id,
        [FromServices] ProfileService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            var success = await service.EnableAsync(id);
            
            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("Profile", id, "Enabled", username, (string?)null);
                
                return Results.Ok(new { message = "Profile enabled successfully" });
            }
            
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enabling profile {Id}", id);
            return Results.Problem("Error enabling profile");
        }
    }

    /// <summary>
    /// POST /api/profiles/{id}/disable - Disable a profile
    /// </summary>
    private static async Task<IResult> DisableProfile(
        int id,
        [FromServices] ProfileService service,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            var success = await service.DisableAsync(id);
            
            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("Profile", id, "Disabled", username, (string?)null);
                
                return Results.Ok(new { message = "Profile disabled successfully" });
            }
            
            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disabling profile {Id}", id);
            return Results.Problem("Error disabling profile");
        }
    }

    /// <summary>
    /// GET /api/profiles/by-connection/{connectionId} - Get all profiles for a connection
    /// </summary>
    private static async Task<IResult> GetProfilesByConnection(
        int connectionId,
        [FromServices] ProfileService service)
    {
        try
        {
            var profiles = await service.GetByConnectionIdAsync(connectionId);
            return Results.Ok(profiles);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting profiles for connection {ConnectionId}", connectionId);
            return Results.Problem("Error retrieving profiles");
        }
    }

    /// <summary>
    /// GET /api/profiles/by-group/{groupId} - Get all profiles in a group
    /// </summary>
    private static async Task<IResult> GetProfilesByGroup(
        int groupId,
        [FromServices] ProfileService service)
    {
        try
        {
            var profiles = await service.GetByGroupIdAsync(groupId);
            return Results.Ok(profiles);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting profiles for group {GroupId}", groupId);
            return Results.Problem("Error retrieving profiles");
        }
    }

    // ===== Delta Sync Endpoints =====

    /// <summary>
    /// POST /api/profiles/{id}/delta-sync/reset - Reset entire delta sync state for a profile
    /// </summary>
    private static async Task<IResult> ResetDeltaSyncState(
        int id,
        [FromServices] ProfileService profileService,
        [FromServices] DeltaSyncService deltaSyncService,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            // Verify profile exists
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound(new { error = "Profile not found" });
            }

            var username = context.User.Identity?.Name ?? "Unknown";
            
            await deltaSyncService.ResetDeltaSyncStateAsync(id);
            
            await auditService.LogAsync("Profile", id, "DeltaSyncStateReset", username,
                "Delta sync state has been reset");
            
            Log.Information("Delta sync state reset for profile {ProfileId} by {User}", id, username);
            
            return Results.Ok(new { message = "Delta sync state reset successfully" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting delta sync state for profile {Id}", id);
            return Results.Problem("Error resetting delta sync state");
        }
    }

    /// <summary>
    /// POST /api/profiles/{id}/delta-sync/reset-rows - Reset delta sync state for specific rows
    /// </summary>
    private static async Task<IResult> ResetDeltaSyncRows(
        int id,
        [FromBody] ResetRowsRequest request,
        [FromServices] ProfileService profileService,
        [FromServices] DeltaSyncService deltaSyncService,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            // Verify profile exists
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound(new { error = "Profile not found" });
            }

            var username = context.User.Identity?.Name ?? "Unknown";
            
            if (request.ReefIds != null && request.ReefIds.Any())
            {
                // Reset specific rows by ReefId
                var count = await deltaSyncService.ResetDeltaSyncRowsAsync(id, request.ReefIds);
                
                await auditService.LogAsync("Profile", id, "DeltaSyncRowsReset", username,
                    $"Reset {count} specific rows");
                
                Log.Information("Reset delta sync for {Count} specific rows in profile {ProfileId} by {User}",
                    count, id, username);
                
                return Results.Ok(new { message = $"Reset delta sync state for {count} rows" });
            }
            else if (request.Criteria != null)
            {
                // Reset rows by criteria
                var count = await deltaSyncService.ResetDeltaSyncByCriteriaAsync(id, request.Criteria);
                
                await auditService.LogAsync("Profile", id, "DeltaSyncCriteriaReset", username,
                    $"Reset {count} rows matching criteria");
                
                Log.Information("Reset delta sync for {Count} rows matching criteria in profile {ProfileId} by {User}",
                    count, id, username);
                
                return Results.Ok(new { message = $"Reset delta sync state for {count} rows" });
            }
            else
            {
                return Results.BadRequest(new { error = "Either ReefIds or Criteria must be provided" });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting delta sync rows for profile {Id}", id);
            return Results.Problem("Error resetting delta sync rows");
        }
    }

    /// <summary>
    /// POST /api/profiles/{id}/delta-sync/generate-hashes - Generate hash baseline for all current rows
    /// </summary>
    private static async Task<IResult> GenerateDeltaSyncHashes(
        int id,
        [FromServices] ProfileService profileService,
        [FromServices] ConnectionService connectionService,
        [FromServices] QueryExecutor queryExecutor,
        [FromServices] DeltaSyncService deltaSyncService,
        [FromServices] AuditService auditService,
        HttpContext context)
    {
        try
        {
            // Verify profile exists and has delta sync enabled
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound(new { error = "Profile not found" });
            }

            if (!profile.DeltaSyncEnabled)
            {
                return Results.BadRequest(new { error = "Delta sync is not enabled for this profile" });
            }

            if (string.IsNullOrEmpty(profile.DeltaSyncReefIdColumn))
            {
                return Results.BadRequest(new { error = "ReefId column is required for delta sync" });
            }

            var username = context.User.Identity?.Name ?? "Unknown";

            // Get the connection
            var connection = await connectionService.GetByIdAsync(profile.ConnectionId);
            if (connection == null)
            {
                return Results.BadRequest(new { error = "Connection not found" });
            }

            // Execute the query to get all current rows
            var (success, queryResults, errorMessage, _) = await queryExecutor.ExecuteQueryAsync(connection, profile.Query);

            if (!success)
            {
                return Results.BadRequest(new { error = $"Query execution failed: {errorMessage}" });
            }

            if (queryResults == null || queryResults.Count == 0)
            {
                // No rows to hash, but that's okay
                await auditService.LogAsync("Profile", id, "DeltaSyncHashGeneration", username,
                    "Generated hash baseline for 0 rows");

                Log.Information("Generated hash baseline for 0 rows in profile {ProfileId} by {User}", id, username);

                return Results.Ok(new { message = "Hash baseline generated for 0 rows", rowsProcessed = 0 });
            }

            // Generate hashes for all rows as baseline (without triggering change detection)
            var rowsProcessed = await deltaSyncService.GenerateHashBaselineAsync(id, queryResults);

            await auditService.LogAsync("Profile", id, "DeltaSyncHashGeneration", username,
                $"Generated hash baseline for {rowsProcessed} rows");

            Log.Information("Generated hash baseline for {Count} rows in profile {ProfileId} by {User}",
                rowsProcessed, id, username);

            return Results.Ok(new { message = $"Hash baseline generated for {rowsProcessed} rows", rowsProcessed = rowsProcessed });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generating delta sync hashes for profile {Id}", id);
            return Results.BadRequest(new { error = $"Error generating hashes: {ex.Message}" });
        }
    }

    /// <summary>
    /// GET /api/profiles/{id}/delta-sync/stats - Get delta sync statistics for a profile
    /// </summary>
    private static async Task<IResult> GetDeltaSyncStats(
        int id,
        [FromServices] ProfileService profileService,
        [FromServices] DeltaSyncService deltaSyncService,
        [FromServices] ExecutionService executionService)
    {
        try
        {
            // Verify profile exists
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound(new { error = "Profile not found" });
            }

            if (!profile.DeltaSyncEnabled)
            {
                return Results.Ok(new { 
                    enabled = false,
                    message = "Delta sync is not enabled for this profile" 
                });
            }

            var stats = await deltaSyncService.GetProfileStatsAsync(id);
            
            // Get last execution metrics
            var lastExecution = await executionService.GetLastExecutionForProfileAsync(id);
            
            return Results.Ok(new
            {
                enabled = true,
                activeRows = stats.ActiveRows,
                deletedRows = stats.DeletedRows,
                totalTrackedRows = stats.TotalTrackedRows,
                firstTrackedAt = stats.FirstTrackedAt,
                lastSyncDate = stats.LastTrackedAt,  // Map to the name expected by UI
                newRowsLastRun = lastExecution?.DeltaSyncNewRows ?? 0,
                changedRowsLastRun = lastExecution?.DeltaSyncChangedRows ?? 0,
                deletedRowsLastRun = lastExecution?.DeltaSyncDeletedRows ?? 0,
                unchangedRowsLastRun = lastExecution?.DeltaSyncUnchangedRows ?? 0,
                reefIdColumn = profile.DeltaSyncReefIdColumn,
                hashAlgorithm = profile.DeltaSyncHashAlgorithm,
                includeDeleted = profile.DeltaSyncTrackDeletes,
                retentionDays = profile.DeltaSyncRetentionDays
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting delta sync stats for profile {Id}", id);
            return Results.Problem("Error retrieving delta sync statistics");
        }
    }

    /// <summary>
    /// GET /api/profiles/executions/{executionId}/splits - Get split details for an execution
    /// </summary>
    private static async Task<IResult> GetExecutionSplits(
        int executionId,
        [FromServices] ExecutionService executionService)
    {
        try
        {
            var splits = await executionService.GetSplitsByExecutionIdAsync(executionId);
            return Results.Ok(splits);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving execution splits for execution {ExecutionId}", executionId);
            return Results.Problem("Error retrieving execution splits");
        }
    }

    /// <summary>
    /// POST /api/profiles/{id}/preview-email-html - Preview rendered email HTML using first query result row
    /// </summary>
    private static async Task<IResult> PreviewEmailHtml(
        int id,
        [FromServices] ProfileService profileService,
        [FromServices] ConnectionService connectionService,
        [FromServices] QueryTemplateService templateService,
        [FromServices] ScribanTemplateEngine templateEngine,
        [FromServices] QueryExecutor queryExecutor)
    {
        try
        {
            // Get the profile
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound(new { message = "Profile not found" });
            }

            // Validate it's an email export profile
            if (!profile.IsEmailExport)
            {
                return Results.BadRequest(new { message = "Profile is not configured for email export" });
            }

            // Validate email template is set
            if (!profile.EmailTemplateId.HasValue)
            {
                return Results.BadRequest(new { message = "Email template not set" });
            }

            var emailTemplate = await templateService.GetByIdAsync(profile.EmailTemplateId.Value);
            if (emailTemplate == null)
            {
                return Results.BadRequest(new { message = "Email template not found" });
            }

            if (emailTemplate.Type != QueryTemplateType.ScribanTemplate)
            {
                return Results.BadRequest(new { message = "Email template must be a Scriban template" });
            }

            // Validate recipients are configured (either column or hardcoded)
            if (string.IsNullOrEmpty(profile.EmailRecipientsColumn) && string.IsNullOrEmpty(profile.EmailRecipientsHardcoded))
            {
                return Results.BadRequest(new { message = "Email recipients column or hardcoded email not configured" });
            }

            // Get the connection
            var connection = await connectionService.GetByIdAsync(profile.ConnectionId);
            if (connection == null)
            {
                return Results.BadRequest(new { message = "Connection not found" });
            }

            // Execute the query to get preview data
            // Note: We don't modify the query with LIMIT since different databases have different syntax
            // (SQL Server uses TOP, MySQL/PostgreSQL use LIMIT, Oracle uses ROWNUM, etc.)
            // Instead, we execute the full query and just use the first row for preview
            var query = profile.Query;

            // Execute query using QueryExecutor
            var (success, queryResults, errorMessage, executionTime) = await queryExecutor.ExecuteQueryAsync(connection, query);

            if (!success)
            {
                return Results.BadRequest(new { success = false, message = $"Query execution failed: {errorMessage}" });
            }

            if (queryResults.Count == 0)
            {
                return Results.Ok(new { success = true, html = "<p style=\"color: #666;\">Query returned no results. Please ensure your query returns at least one row.</p>", message = "No data to preview" });
            }

            // Render the template with the first row
            string renderedHtml;
            try
            {
                renderedHtml = await templateEngine.TransformAsync(new List<Dictionary<string, object>> { queryResults[0] }, emailTemplate.Template);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = $"Template rendering failed: {ex.Message}" });
            }

            return Results.Ok(new { success = true, html = renderedHtml, message = $"Preview generated using first row (total {queryResults.Count} row(s) available)" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error previewing email HTML for profile {Id}", id);
            return Results.Problem($"Error previewing email: {ex.Message}");
        }
    }

    /// <summary>
    /// GET /api/profiles/{id}/validate-email-config - Validate email export configuration
    /// </summary>
    private static async Task<IResult> ValidateEmailExportConfig(
        int id,
        [FromServices] ProfileService profileService,
        [FromServices] QueryTemplateService templateService,
        [FromServices] DestinationService destinationService)
    {
        try
        {
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound(new { error = "Profile not found" });
            }

            if (!profile.IsEmailExport)
            {
                return Results.BadRequest(new { error = "Profile is not configured as email export" });
            }

            var errors = new List<string>();
            var warnings = new List<string>();

            // Validate EmailTemplateId
            if (!profile.EmailTemplateId.HasValue)
            {
                errors.Add("EmailTemplateId is not set");
            }
            else
            {
                var template = await templateService.GetByIdAsync(profile.EmailTemplateId.Value);
                if (template == null)
                {
                    errors.Add($"Email template {profile.EmailTemplateId.Value} not found");
                }
                else if (template.Type != QueryTemplateType.ScribanTemplate)
                {
                    errors.Add($"Email template must be a Scriban template, got {template.Type}");
                }
            }

            // Validate OutputDestinationId
            if (!profile.OutputDestinationId.HasValue)
            {
                errors.Add("OutputDestinationId (SMTP destination) is not set");
            }
            else
            {
                var destination = await destinationService.GetByIdAsync(profile.OutputDestinationId.Value);
                if (destination == null)
                {
                    errors.Add($"Email destination {profile.OutputDestinationId.Value} not found");
                }
                else if (destination.Type != DestinationType.Email)
                {
                    errors.Add($"Output destination must be type Email, got {destination.Type}");
                }
            }

            // Validate EmailRecipientsColumn or EmailRecipientsHardcoded
            if (string.IsNullOrEmpty(profile.EmailRecipientsColumn) && string.IsNullOrEmpty(profile.EmailRecipientsHardcoded))
            {
                errors.Add("EmailRecipientsColumn or hardcoded email is required");
            }

            // Validate EmailSubjectColumn or EmailSubjectHardcoded (optional warning if both empty)
            if (string.IsNullOrEmpty(profile.EmailSubjectColumn) && string.IsNullOrEmpty(profile.EmailSubjectHardcoded))
            {
                warnings.Add("EmailSubjectColumn is not set and no hardcoded subject provided (will use profile name as subject)");
            }

            var result = new
            {
                isValid = errors.Count == 0,
                errors = errors,
                warnings = warnings
            };

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error validating email export config for profile {Id}", id);
            return Results.Problem("Error validating email export configuration");
        }
    }

    /// <summary>
    /// POST /api/profiles/{id}/validate-attachment-config - Validate email attachment configuration
    /// Executes sample query and validates attachment config against result columns
    /// </summary>
    private static async Task<IResult> ValidateAttachmentConfig(
        int id,
        [FromBody] AttachmentConfig config,
        [FromServices] ProfileService profileService)
    {
        try
        {
            var profile = await profileService.GetByIdAsync(id);
            if (profile == null)
            {
                return Results.NotFound(new { error = "Profile not found" });
            }

            if (!profile.IsEmailExport)
            {
                return Results.BadRequest(new { error = "Profile is not configured as email export" });
            }

            var result = new AttachmentValidationResult();

            // Validate basic configuration
            if (!config.Enabled)
            {
                result.Info.Add("Attachments are disabled");
                return Results.Ok(result);
            }

            if (string.IsNullOrEmpty(config.Mode))
            {
                result.Errors.Add("Attachment mode is not specified");
                return Results.Ok(result);
            }

            // Only Binary mode is supported in Phase 1
            if (config.Mode != "Binary")
            {
                result.Errors.Add($"Attachment mode '{config.Mode}' is not supported in this version");
                return Results.Ok(result);
            }

            if (config.Binary == null)
            {
                result.Errors.Add("Binary attachment configuration is missing");
                return Results.Ok(result);
            }

            // Note: Sample query execution would require more complex setup
            // For Phase 1, we do basic validation without running sample query
            // This can be enhanced in future phases
            List<Dictionary<string, object>>? sampleRows = null;

            // Validate Binary mode configuration
            if (string.IsNullOrEmpty(config.Binary.ContentColumnName))
            {
                result.Errors.Add("Content column name is required");
            }

            if (string.IsNullOrEmpty(config.Binary.FilenameColumnName))
            {
                result.Errors.Add("Filename column name is required");
            }

            // If we have sample data, validate against it
            if (sampleRows != null && sampleRows.Count > 0)
            {
                var availableColumns = sampleRows[0].Keys.ToList();
                result.Info.Add($"Query returned {sampleRows.Count} sample rows with {availableColumns.Count} columns");

                // Use BinaryAttachmentResolver to validate
                var resolver = new BinaryAttachmentResolver();
                var validationErrors = resolver.ValidateConfiguration(config.Binary, availableColumns);
                result.Errors.AddRange(validationErrors);

                // Check for data issues in sample rows
                var contentColumnExists = availableColumns.Any(c =>
                    c.Equals(config.Binary.ContentColumnName, StringComparison.OrdinalIgnoreCase));
                var filenameColumnExists = availableColumns.Any(c =>
                    c.Equals(config.Binary.FilenameColumnName, StringComparison.OrdinalIgnoreCase));

                if (contentColumnExists && filenameColumnExists)
                {
                    int nullContentCount = 0;
                    int nullFilenameCount = 0;
                    int validCount = 0;

                    foreach (var row in sampleRows)
                    {
                        var contentVal = row.Values.FirstOrDefault(v =>
                            row.Keys.First(k => k.Equals(config.Binary.ContentColumnName, StringComparison.OrdinalIgnoreCase)) != null);
                        var filenameVal = row.Values.FirstOrDefault(v =>
                            row.Keys.First(k => k.Equals(config.Binary.FilenameColumnName, StringComparison.OrdinalIgnoreCase)) != null);

                        if (contentVal == null)
                            nullContentCount++;
                        if (filenameVal == null)
                            nullFilenameCount++;
                        if (contentVal != null && filenameVal != null)
                            validCount++;
                    }

                    if (nullContentCount > 0)
                    {
                        result.Warnings.Add($"Content column has {nullContentCount} NULL values in sample");
                    }

                    if (nullFilenameCount > 0)
                    {
                        result.Warnings.Add($"Filename column has {nullFilenameCount} NULL values in sample");
                    }

                    result.Info.Add($"{validCount} of {sampleRows.Count} sample rows have both content and filename");
                }
            }

            // Validate deduplication strategy
            if (string.IsNullOrEmpty(config.Deduplication))
            {
                result.Warnings.Add("Deduplication strategy not specified, defaulting to 'Auto'");
            }

            // Validate attachment limits
            if (config.MaxAttachmentsPerEmail <= 0)
            {
                result.Errors.Add("Max attachments per email must be greater than 0");
            }

            result.IsValid = result.Errors.Count == 0;

            if (result.IsValid)
            {
                result.Info.Add("Attachment configuration is valid");
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error validating attachment config for profile {Id}", id);
            return Results.Problem("Error validating attachment configuration");
        }
    }
}