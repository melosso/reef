using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Formatters;
using Reef.Core.Destinations;
using Reef.Core.Models;
using Reef.Core.TemplateEngines;
using Serilog;
using System.Diagnostics;

namespace Reef.Core.Services;

/// <summary>
/// Service for orchestrating profile execution
/// Handles the complete execution pipeline from query to output delivery
/// </summary>
public class ExecutionService
{
    private readonly string _connectionString;
    private readonly QueryExecutor _queryExecutor;
    private readonly DestinationService _destinationService;
    private readonly ProfileService _profileService;
    private readonly ConnectionService _connectionService;
    private readonly AuditService _auditService;
    private readonly QueryTemplateService _templateService;
    private readonly ITemplateEngine _templateEngine;
    private readonly DeltaSyncService _deltaSyncService;
    private readonly EmailExportService _emailExportService;
    private readonly EmailApprovalService _emailApprovalService;
    private readonly NotificationService _notificationService;

    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;

    public ExecutionService(
        DatabaseConfig config,
        QueryExecutor queryExecutor,
        DestinationService destinationService,
        ProfileService profileService,
        ConnectionService connectionService,
        AuditService auditService,
        QueryTemplateService templateService,
        DeltaSyncService deltaSyncService,
        EmailExportService emailExportService,
        EmailApprovalService emailApprovalService,
        NotificationService notificationService,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _connectionString = config.ConnectionString;
        _queryExecutor = queryExecutor;
        _destinationService = destinationService;
        _profileService = profileService;
        _connectionService = connectionService;
        _auditService = auditService;
        _templateService = templateService;
        _configuration = configuration;
        _templateEngine = new ScribanTemplateEngine(_configuration);
        _deltaSyncService = deltaSyncService;
        _emailExportService = emailExportService;
        _emailApprovalService = emailApprovalService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Execute a profile with optional parameters
    /// </summary>
    /// <param name="profileId">Profile ID to execute</param>
    /// <param name="parameters">Optional query parameters</param>
    /// <param name="triggeredBy">Who/what triggered the execution</param>
    /// <param name="jobId">Optional job ID that triggered this execution</param>
    /// <param name="destinationOverrideId">Optional destination override ID</param>
    /// <returns>Execution ID, success status, output path, and error message</returns>
    public async Task<(int ExecutionId, bool Success, string? OutputPath, string? ErrorMessage)> 
        ExecuteProfileAsync(
            int profileId, 
            Dictionary<string, string>? parameters = null, 
            string triggeredBy = "Manual",
            int? jobId = null,
            int? destinationOverrideId = null)
    {
        var stopwatch = Stopwatch.StartNew();
        int executionId = 0;

        try
        {
            // Create ProfileExecution record FIRST - ensures all execution attempts are tracked
            executionId = await CreateExecutionRecordAsync(profileId, triggeredBy, jobId);

            // Load profile and connection
            var profile = await _profileService.GetByIdAsync(profileId);
            if (profile == null)
            {
                Log.Warning("Profile {ProfileId} not found", profileId);
                await UpdateExecutionRecordAsync(executionId, "Failed", 0, null, stopwatch.ElapsedMilliseconds, "Profile not found");
                return (executionId, false, null, "Profile not found");
            }

            if (!profile.IsEnabled)
            {
                Log.Warning("Profile {ProfileId} is disabled", profileId);
                await UpdateExecutionRecordAsync(executionId, "Failed", 0, null, stopwatch.ElapsedMilliseconds, "Profile is disabled");
                return (executionId, false, null, "Profile is disabled");
            }

            var connection = await _connectionService.GetByIdAsync(profile.ConnectionId);
            if (connection == null)
            {
                Log.Warning("Connection {ConnectionId} not found for profile {ProfileId}", profile.ConnectionId, profileId);
                await UpdateExecutionRecordAsync(executionId, "Failed", 0, null, stopwatch.ElapsedMilliseconds, "Connection not found");
                return (executionId, false, null, "Connection not found");
            }

            if (!connection.IsActive)
            {
                Log.Warning("Connection {ConnectionId} is inactive", connection.Id);
                await UpdateExecutionRecordAsync(executionId, "Failed", 0, null, stopwatch.ElapsedMilliseconds, "Connection is inactive");
                return (executionId, false, null, "Connection is inactive");
            }

            Log.Debug("Starting execution of profile {ProfileName} (ID: {ProfileId}) triggered by {TriggeredBy}", 
                profile.Name, profileId, triggeredBy);

            // Check dependencies if profile has any
            if (!string.IsNullOrEmpty(profile.DependsOnProfileIds))
            {
                var isDependenciesSatisfied = await ValidateProfileDependenciesAsync(profileId, profile.DependsOnProfileIds);
                if (!isDependenciesSatisfied)
                {
                    Log.Warning("Profile {ProfileId} dependencies not satisfied", profileId);
                    await UpdateExecutionRecordAsync(
                        executionId,
                        "Failed",
                        0,
                        null,
                        stopwatch.ElapsedMilliseconds,
                        "Profile dependencies not satisfied - one or more dependent profiles have not completed successfully");
                    return (executionId, false, null, "Dependencies not satisfied");
                }
                Log.Information("Profile {ProfileId} dependencies validated successfully", profileId);
            }
            
            // ===== PHASE 1: PRE-PROCESSING =====
            // Execute pre-processing if configured
            var preProcessContext = new ProcessingContext
            {
                ExecutionId = executionId,
                ProfileId = profileId,
                RowCount = 0,
                OutputPath = null,
                FileSizeBytes = null,
                ExecutionTimeMs = 0,
                OutputFormat = profile.OutputFormat,
                TriggeredBy = triggeredBy,
                StartedAt = DateTime.UtcNow,
                CompletedAt = null,
                Status = "Running",
                ErrorMessage = null,
                DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn,
                SplitKeyColumn = profile.SplitKeyColumn
            };

            var (preProcessSuccess, preProcessError) = await ExecutePreProcessingAsync(
                executionId, profile, connection, preProcessContext);

            if (!preProcessSuccess)
            {
                Log.Error("Pre-processing failed: {Error}", preProcessError);
                await UpdateExecutionRecordAsync(executionId, "Failed", 0, null, stopwatch.ElapsedMilliseconds, 
                    $"Pre-processing failed: {preProcessError}");
                return (executionId, false, null, $"Pre-processing failed: {preProcessError}");
            }

            // ===== PHASE 2: MAIN QUERY =====
            // Execute query
            Log.Debug("Executing query for profile {ProfileId}", profileId);
            var (querySuccess, rows, queryError, queryTime) = await _queryExecutor.ExecuteQueryAsync(
                connection, profile.Query, parameters);

            if (!querySuccess)
            {
                await UpdateExecutionRecordAsync(executionId, "Failed", 0, null, stopwatch.ElapsedMilliseconds, queryError);
                return (executionId, false, null, queryError);
            }

            // ===== PHASE 2.5: DELTA SYNC (IF ENABLED) =====
            int originalRowCount = rows.Count;
            DeltaSyncResult? deltaSyncResult = null;
            
            if (profile.DeltaSyncEnabled)
            {
                try
                {
                    Log.Debug("Delta sync enabled for profile {ProfileId}, processing deltas...", profileId);
                    
                    // Validate ReefId column exists (only if we have rows to check)
                    if (rows.Any() && !rows[0].ContainsKey(profile.DeltaSyncReefIdColumn!))
                    {
                        var error = $"Delta sync failed: ReefId column '{profile.DeltaSyncReefIdColumn}' not found in query results";
                        Log.Error(error);
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, 
                            stopwatch.ElapsedMilliseconds, error);
                        return (executionId, false, null, error);
                    }
                    
                    // If no rows returned, log and continue with empty delta sync
                    if (!rows.Any())
                    {
                        Log.Debug("Query returned 0 rows. Delta sync will process empty result set.");
                    }
                    
                    // Process deltas
                    deltaSyncResult = await _deltaSyncService.ProcessDeltaAsync(
                        profileId,
                        rows,
                        profile);
                    
                    // Build rows to export (new + changed + optionally deleted)
                    var rowsToExport = new List<Dictionary<string, object>>();
                    rowsToExport.AddRange(deltaSyncResult.NewRows);
                    rowsToExport.AddRange(deltaSyncResult.ChangedRows);
                    
                    // Optionally include deleted rows with marker
                    if (profile.DeltaSyncTrackDeletes && deltaSyncResult.DeletedReefIds.Any())
                    {
                        Log.Debug("Including {Count} deleted rows in export", deltaSyncResult.DeletedReefIds.Count);
                        
                        foreach (var reefId in deltaSyncResult.DeletedReefIds)
                        {
                            var deletedRow = new Dictionary<string, object>
                            {
                                [profile.DeltaSyncReefIdColumn!] = reefId,
                                ["_ReefDeleted"] = true,
                                ["_ReefDeletedAt"] = DateTime.UtcNow
                            };
                            rowsToExport.Add(deletedRow);
                        }
                    }
                    
                    // Update rows to export only deltas
                    rows = rowsToExport;
                    
                    Log.Information(
                        "Delta sync for profile {profileId} completed: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged → Exporting {Export} rows",
                        profileId,
                        deltaSyncResult.NewRows.Count,
                        deltaSyncResult.ChangedRows.Count,
                        deltaSyncResult.DeletedReefIds.Count,
                        deltaSyncResult.UnchangedRows.Count,
                        rows.Count);
                    
                    // Clean up old state if retention policy is set
                    if (profile.DeltaSyncRetentionDays.HasValue)
                    {
                        await _deltaSyncService.CleanupOldStateAsync(profileId, profile.DeltaSyncRetentionDays.Value);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Delta sync processing failed for profile {ProfileId}", profileId);
                    await UpdateExecutionRecordAsync(executionId, "Failed", originalRowCount, null, 
                        stopwatch.ElapsedMilliseconds, $"Delta sync error: {ex.Message}");
                    return (executionId, false, null, $"Delta sync error: {ex.Message}");
                }
            }

            // Check if there are any rows to export (for both delta sync and regular profiles)
            if (rows.Count == 0)
            {
                Log.Information("No rows to export for profile {ProfileId}", profileId);

                var message = profile.DeltaSyncEnabled
                    ? "No changes detected by smart sync"
                    : "Query returned no rows";

                // Check if post-processing should run even with 0 rows
                if (profile.PostProcessOnZeroRows && !string.IsNullOrEmpty(profile.PostProcessType))
                {
                    Log.Information("PostProcessOnZeroRows enabled - executing post-processing despite 0 rows");

                    // Create post-processing context for zero-row scenario
                    var zeroRowPostProcessContext = new ProcessingContext
                    {
                        ExecutionId = executionId,
                        ProfileId = profileId,
                        RowCount = 0,
                        OutputPath = null,
                        FileSizeBytes = null,
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                        OutputFormat = profile.OutputFormat,
                        TriggeredBy = triggeredBy,
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                        Status = "Success",
                        ErrorMessage = null,
                        DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn,
                        SplitKeyColumn = profile.SplitKeyColumn
                    };

                    var zeroRowConnection = await _connectionService.GetByIdAsync(profile.ConnectionId);
                    if (zeroRowConnection != null)
                    {
                        var (zeroRowPostProcessSuccess, zeroRowPostProcessError) = await ExecutePostProcessingAsyncNew(
                            executionId, profile, zeroRowConnection, zeroRowPostProcessContext, mainQueryFailed: false);

                        if (!zeroRowPostProcessSuccess)
                        {
                            Log.Warning("Post-processing failed on zero rows: {Error}", zeroRowPostProcessError);
                            // Don't fail the entire execution, just log it
                        }
                    }
                }

                stopwatch.Stop();
                await UpdateExecutionRecordAsync(executionId, "Success", 0, null, stopwatch.ElapsedMilliseconds, null, message);

                // Update delta sync metrics if applicable
                if (profile.DeltaSyncEnabled && deltaSyncResult != null)
                {
                    await UpdateDeltaSyncMetricsAsync(executionId, deltaSyncResult);
                }

                // Update profile's last executed timestamp
                await UpdateProfileLastExecutedAsync(profileId);

                await _auditService.LogAsync("Profile", profileId, "Executed", triggeredBy,
                    System.Text.Json.JsonSerializer.Serialize(new { RowCount = 0, Message = message, ExecutionTimeMs = stopwatch.ElapsedMilliseconds }));

                // Send success notification (fire and forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var execution = new ProfileExecution { Id = executionId, StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, ExecutionTimeMs = stopwatch.ElapsedMilliseconds };
                        await _notificationService.SendExecutionSuccessAsync(execution, profile);
                    }
                    catch (Exception ex) { Log.Error(ex, "Failed to send execution success notification"); }
                });

                return (executionId, true, null, null);
            }

            // ===== PHASE 2.5.5: EMAIL EXPORT (IF ENABLED) =====
            // Skip email export if there's a destination override (test mode or manual override)
            if (profile.IsEmailExport && rows.Count > 0 && !destinationOverrideId.HasValue)
            {
                Log.Information("Profile {ProfileId} executing as email export (sending {RowCount} rows)", profile.Id, rows.Count);

                try
                {
                    // Load email template
                    if (!profile.EmailTemplateId.HasValue)
                    {
                        var errorMsg = "Email export configured but EmailTemplateId not set";
                        Log.Error(errorMsg);
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds, errorMsg);
                        return (executionId, false, null, errorMsg);
                    }

                    var emailTemplate = await _templateService.GetByIdAsync(profile.EmailTemplateId.Value);
                    if (emailTemplate == null || emailTemplate.Type != QueryTemplateType.ScribanTemplate)
                    {
                        var errorMsg = $"Email template {profile.EmailTemplateId.Value} not found or not a Scriban template";
                        Log.Error(errorMsg);
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds, errorMsg);
                        return (executionId, false, null, errorMsg);
                    }

                    // Load email destination
                    if (!profile.OutputDestinationId.HasValue)
                    {
                        var errorMsg = "Email export configured but OutputDestinationId (SMTP destination) not set";
                        Log.Error(errorMsg);
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds, errorMsg);
                        return (executionId, false, null, errorMsg);
                    }

                    var emailDestination = await _destinationService.GetByIdForExecutionAsync(profile.OutputDestinationId.Value);
                    if (emailDestination == null || emailDestination.Type != DestinationType.Email)
                    {
                        var errorMsg = $"Email destination {profile.OutputDestinationId.Value} not found or not type Email";
                        Log.Error(errorMsg);
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds, errorMsg);
                        return (executionId, false, null, errorMsg);
                    }

                    // Check if email approval is required
                    if (profile.EmailApprovalRequired)
                    {
                        Log.Information("Email approval required for profile {ProfileId}, storing {RowCount} emails for approval", profile.Id, rows.Count);

                        try
                        {
                            // Parse attachment configuration if present
                            AttachmentConfig? attachmentConfig = null;
                            if (!string.IsNullOrEmpty(profile.EmailAttachmentConfig))
                            {
                                try
                                {
                                    attachmentConfig = System.Text.Json.JsonSerializer.Deserialize<AttachmentConfig>(profile.EmailAttachmentConfig);
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning(ex, "Failed to parse attachment configuration for profile {ProfileId}", profile.Id);
                                }
                            }

                            // Render emails without sending
                            var (renderedEmails, renderErrors) = await _emailExportService.RenderEmailsForApprovalAsync(
                                profile,
                                emailTemplate,
                                rows,
                                attachmentConfig);

                            if (renderErrors.Count > 0)
                            {
                                Log.Warning("Errors during email rendering for profile {ProfileId}: {Errors}",
                                    profile.Id, string.Join("; ", renderErrors));
                            }

                            // Store rendered emails as pending approvals
                            int approvalCount = 0;
                            foreach (var (recipients, subject, htmlBody, ccAddresses, attachmentConfigJson) in renderedEmails)
                            {
                                var approvalId = await _emailApprovalService.CreatePendingApprovalAsync(
                                    profileId,
                                    executionId,
                                    recipients,
                                    subject,
                                    htmlBody,
                                    ccAddresses,
                                    attachmentConfigJson);

                                approvalCount++;
                                Log.Debug("Created pending email approval {ApprovalId} for execution {ExecutionId}", approvalId, executionId);
                            }

                            stopwatch.Stop();

                            // Mark execution as successful (query succeeded, awaiting approval)
                            await UpdateExecutionRecordAsync(executionId, "Success", rows.Count, null, stopwatch.ElapsedMilliseconds,
                                $"{approvalCount} emails pending approval");

                            Log.Information("Profile {ProfileId} execution completed: {ApprovalCount} emails stored for approval", profile.Id, approvalCount);

                            return (executionId, true, null, null);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to create pending email approvals for profile {ProfileId}", profile.Id);
                            stopwatch.Stop();
                            await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                                $"Failed to store emails for approval: {ex.Message}");
                            return (executionId, false, null, ex.Message);
                        }
                    }

                    // Send emails directly (approval not required)
                    var (emailSuccess, emailMessage, successCount, failureCount, splits) = await _emailExportService.ExportToEmailAsync(
                        profile,
                        emailDestination,
                        emailTemplate,
                        rows);

                    stopwatch.Stop();

                    // Update execution with split summary (email sends = splits)
                    // Email exports always use HTML format from the email template
                    await UpdateExecutionWithSplitSummaryAsync(
                        executionId,
                        successCount + failureCount,  // splitCount = total attempts
                        successCount,
                        failureCount,
                        rows.Count,
                        stopwatch.ElapsedMilliseconds,
                        "HTML");

                    // Insert split records for email exports
                    Log.Debug("Email export has {SplitCount} splits to record", splits.Count);
                    if (splits.Count > 0)
                    {
                        Log.Debug("Inserting {SplitCount} execution split records for execution {ExecutionId}", splits.Count, executionId);
                        await InsertExecutionSplitsAsync(executionId, splits);
                        Log.Debug("Successfully inserted {SplitCount} execution split records", splits.Count);
                    }

                    // Check if email export meets success threshold
                    int totalEmails = successCount + failureCount;
                    bool meetsThreshold = totalEmails > 0 && (successCount * 100 / totalEmails) >= profile.EmailSuccessThresholdPercent;
                    bool finalSuccess = emailSuccess || meetsThreshold;

                    if (meetsThreshold && !emailSuccess)
                    {
                        Log.Information("Email export meets success threshold: {SuccessPercent}% >= {ThresholdPercent}%",
                            (successCount * 100 / totalEmails), profile.EmailSuccessThresholdPercent);
                    }

                    if (finalSuccess)
                    {
                        Log.Information("Email export completed for profile {ProfileId}: {Message}", profile.Id, emailMessage);

                        // Update profile's last executed timestamp
                        await UpdateProfileLastExecutedAsync(profileId);

                        // Commit delta sync for successfully sent emails ONLY
                        // If some emails failed, only mark the successful ones as synced
                        if (profile.DeltaSyncEnabled && deltaSyncResult != null && splits.Count > 0)
                        {
                            // Determine which rows succeeded based on split results
                            var successfulRowIndices = new HashSet<int>();
                            int rowIndex = 0;

                            // Map splits back to row indices
                            // Splits are created in the same order as rows were processed
                            foreach (var split in splits)
                            {
                                if (split.Status == "Success")
                                {
                                    for (int i = 0; i < split.RowCount; i++)
                                    {
                                        successfulRowIndices.Add(rowIndex + i);
                                    }
                                }
                                rowIndex += split.RowCount;
                            }

                            // Filter delta sync result to only include successful rows
                            var filteredDeltaSyncResult = FilterDeltaSyncByRowIndices(
                                deltaSyncResult,
                                rows,
                                successfulRowIndices,
                                profile.DeltaSyncReefIdColumn);

                            if (filteredDeltaSyncResult != null)
                            {
                                await _deltaSyncService.CommitDeltaSyncAsync(profileId, executionId, filteredDeltaSyncResult);
                                await UpdateDeltaSyncMetricsAsync(executionId, filteredDeltaSyncResult);
                                Log.Information("Delta sync committed for {SuccessCount} successfully sent emails, {FailureCount} failed emails not synced",
                                    successfulRowIndices.Count, splits.Where(s => s.Status == "Failed").Sum(s => s.RowCount));
                            }
                        }

                        await _auditService.LogAsync("Profile", profileId, "Executed", triggeredBy,
                            System.Text.Json.JsonSerializer.Serialize(new { RowCount = rows.Count, EmailsSent = emailMessage, ExecutionTimeMs = stopwatch.ElapsedMilliseconds }));

                        return (executionId, true, null, null);
                    }
                    else
                    {
                        Log.Error("Email export failed for profile {ProfileId}: {Error}", profile.Id, emailMessage);
                        return (executionId, false, null, emailMessage);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Email export failed for profile {ProfileId}", profile.Id);
                    stopwatch.Stop();
                    await UpdateExecutionWithSplitSummaryAsync(
                        executionId,
                        0,
                        0,
                        1,
                        rows.Count,
                        stopwatch.ElapsedMilliseconds,
                        "HTML");
                    return (executionId, false, null, $"Email export error: {ex.Message}");
                }
            }

            // ===== PHASE 2.6: MULTI-OUTPUT SPLITTING (IF ENABLED) =====
            if (profile.SplitEnabled && rows.Count > 0)
            {
                Log.Debug("Profile {ProfileId} executing with multi-output splitting enabled", profile.Id);
                var (splitExecId, splitSuccess, splitOutputPath, splitError) = await ExecuteWithSplittingAsync(
                    profile,
                    rows,
                    executionId,
                    triggeredBy,
                    destinationOverrideId,
                    stopwatch,
                    deltaSyncResult);

                // If PostProcessPerSplit is OFF, run main post-processing after all splits
                if (!profile.PostProcessPerSplit)
                {
                    var splitPostProcessContext = new ProcessingContext
                    {
                        ExecutionId = executionId,
                        ProfileId = profileId,
                        RowCount = rows.Count,
                        OutputPath = splitOutputPath,
                        FileSizeBytes = null, // Not available for multi-output
                        ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                        OutputFormat = profile.OutputFormat,
                        TriggeredBy = triggeredBy,
                        StartedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow,
                        Status = splitSuccess ? "Success" : "Failed",
                        ErrorMessage = splitError,
                        DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn,
                        SplitKeyColumn = profile.SplitKeyColumn
                    };
                    var splitConnection = await _connectionService.GetByIdAsync(profile.ConnectionId);
                    if (splitConnection != null)
                    {
                        var (splitPostProcessSuccess, splitPostProcessError) = await ExecutePostProcessingAsyncNew(
                            executionId, profile, splitConnection, splitPostProcessContext, mainQueryFailed: !splitSuccess);
                        if (!splitPostProcessSuccess && profile.PostProcessRollbackOnFailure)
                        {
                            Log.Error("Post-processing failed with rollback enabled: {Error}", splitPostProcessError);
                            await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, splitOutputPath, 
                                stopwatch.ElapsedMilliseconds, $"Post-processing failed: {splitPostProcessError}");
                            return (executionId, false, splitOutputPath, $"Post-processing failed: {splitPostProcessError}");
                        }
                    }
                }
                return (splitExecId, splitSuccess, splitOutputPath, splitError);
            }

            // ===== PHASE 2.7: FILTER INTERNAL COLUMNS FROM OUTPUT (IF CONFIGURED) =====
            // Remove ReefId and/or SplitKey columns if they should not be included in output
            rows = FilterInternalColumns(profile, rows);

            // Apply template transformation if template is specified
            string? transformedContent = null;
            string actualOutputFormat = profile.OutputFormat;

            // For email profiles in test mode, use the EmailTemplateId and HTML format; otherwise use TemplateId
            int? templateIdToUse = (profile.IsEmailExport && destinationOverrideId.HasValue)
                ? profile.EmailTemplateId
                : profile.TemplateId;

            if (profile.IsEmailExport && destinationOverrideId.HasValue)
            {
                actualOutputFormat = "HTML"; // Email test mode always outputs HTML
                Log.Information("Email profile test mode: Using EmailTemplateId and HTML output format");
            }

            if (templateIdToUse.HasValue)
            {
                try
                {
                    Log.Debug("Loading template ID {TemplateId} for transformation", templateIdToUse.Value);
                    var template = await _templateService.GetByIdAsync(templateIdToUse.Value);

                    if (template == null)
                    {
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                            $"Template {templateIdToUse.Value} not found");
                        return (executionId, false, null, $"Template {templateIdToUse.Value} not found");
                    }

                    if (!template.IsActive)
                    {
                        Log.Warning("Template {TemplateName} is inactive, skipping transformation", template.Name);
                    }
                    else
                    {
                        // Determine if this is a SQL Server native type or custom template
                        bool isNativeType = template.Type >= QueryTemplateType.ForXmlRaw && 
                                          template.Type <= QueryTemplateType.ForJsonPath;
                        
                        if (isNativeType)
                        {
                            // SQL Server Native Type - Use QueryTemplateService
                            Log.Information("Applying SQL Server native template '{TemplateName}' ({TemplateType})", 
                                template.Name, template.Type);
                            
                            // Parse transformation options from profile or template
                            Dictionary<string, object>? transformOptions = null;
                            
                            if (!string.IsNullOrWhiteSpace(profile.TransformationOptionsJson))
                            {
                                try
                                {
                                    transformOptions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                                        profile.TransformationOptionsJson);
                                    Log.Debug("Using transformation options from profile: {Options}", profile.TransformationOptionsJson);
                                }
                                catch (Exception optEx)
                                {
                                    Log.Warning(optEx, "Failed to parse TransformationOptionsJson, will use template string fallback");
                                }
                            }
                            
                            // Execute transformation via QueryTemplateService
                            await using var dbConnection = await _connectionService.CreateDatabaseConnectionAsync(profile.ConnectionId);
                            await dbConnection.OpenAsync();
                            
                            var transformResult = await _templateService.ApplyTransformationAsync(dbConnection, 
                                new TransformationRequest
                                {
                                    Query = profile.Query,
                                    TemplateId = template.Id,
                                    Type = template.Type,
                                    Options = transformOptions
                                });
                            
                            if (!transformResult.Success)
                            {
                                throw new InvalidOperationException(
                                    $"Native template transformation failed: {transformResult.ErrorMessage}");
                            }
                            
                            transformedContent = transformResult.Output;
                            actualOutputFormat = template.OutputFormat;
                            Log.Debug("Native template transformation completed. Output length: {Length} chars, Row count: {RowCount}", 
                                transformedContent?.Length ?? 0, transformResult.RowCount);
                        }
                        else
                        {
                            // Custom Template (Scriban) - Use TemplateEngine
                            Log.Information("Applying custom template '{TemplateName}' ({TemplateType})", 
                                template.Name, template.Type);
                            transformedContent = await _templateEngine.TransformAsync(rows, template.Template);
                            actualOutputFormat = template.OutputFormat;
                            Log.Debug("Custom template transformation completed. Output length: {Length} chars", 
                                transformedContent?.Length ?? 0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Template transformation failed");
                    await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds, 
                        $"Template transformation error: {ex.Message}");
                    return (executionId, false, null, $"Template transformation error: {ex.Message}");
                }
            }

            // Determine file extension based on actual output format
            var fileExtension = GetFileExtension(actualOutputFormat);

            var filename = GenerateFilename(profile, fileExtension, executionId);

            // Normalize path separators for cross-platform compatibility
            filename = filename.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

            var tempFilePath = Path.Combine(Path.GetTempPath(), filename);
            string? finalPath = null;

            try
            {
                // Write output to file
                long fileSize;
                if (!string.IsNullOrEmpty(transformedContent))
                {
                    // Template was applied - check if this is email test mode with multiple documents
                    if (profile.IsEmailExport && destinationOverrideId.HasValue)
                    {
                        // Split HTML documents and save separately
                        var htmlDocs = SplitHtmlDocuments(transformedContent);
                        if (htmlDocs.Count > 1)
                        {
                            Log.Information("Test mode: Splitting {Count} HTML documents into separate files", htmlDocs.Count);

                            // Get destination config to determine where to save files
                            string? destDirectory = null;
                            try
                            {
                                // Get the local destination path from config
                                var destination = await _destinationService.GetByIdForExecutionAsync(destinationOverrideId.Value);
                                if (destination != null && !string.IsNullOrEmpty(destination.ConfigurationJson))
                                {
                                    var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(destination.ConfigurationJson);
                                    if (config != null && config.TryGetValue("path", out var pathValue))
                                    {
                                        var pathStr = pathValue?.ToString() ?? "";
                                        destDirectory = Path.GetDirectoryName(pathStr);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Failed to get destination path, using temp directory");
                            }

                            // Fall back to temp directory if we couldn't determine destination
                            var directory = destDirectory ?? Path.GetDirectoryName(tempFilePath);
                            if (string.IsNullOrEmpty(directory))
                            {
                                directory = Path.GetTempPath();
                            }
                            if (!Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }

                            var baseFileName = Path.GetFileNameWithoutExtension(tempFilePath);
                            fileSize = 0;
                            finalPath = null;

                            // Save each HTML document separately
                            for (int i = 0; i < htmlDocs.Count && i < rows.Count; i++)
                            {
                                var docFileName = $"{baseFileName}_{i + 1:D3}.html";
                                var docFilePath = Path.Combine(directory, docFileName);
                                await File.WriteAllTextAsync(docFilePath, htmlDocs[i]);
                                var docSize = new FileInfo(docFilePath).Length;
                                fileSize += docSize;
                                finalPath = docFilePath; // Last one
                                Log.Information("Saved email document {Number} to {FilePath} ({Size} bytes)",
                                    i + 1, docFilePath, docSize);
                            }
                        }
                        else
                        {
                            // Single document, write normally
                            Log.Debug("Writing template-transformed content to {TempFilePath}", tempFilePath);
                            var directory = Path.GetDirectoryName(tempFilePath);
                            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }
                            await File.WriteAllTextAsync(tempFilePath, transformedContent);
                            fileSize = new FileInfo(tempFilePath).Length;
                            finalPath = tempFilePath;
                            Log.Debug("Wrote {FileSize} bytes of transformed content", fileSize);
                        }
                    }
                    else
                    {
                        // Regular template output - write normally
                        Log.Debug("Writing template-transformed content to {TempFilePath}", tempFilePath);
                        var directory = Path.GetDirectoryName(tempFilePath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        await File.WriteAllTextAsync(tempFilePath, transformedContent);
                        fileSize = new FileInfo(tempFilePath).Length;
                        finalPath = tempFilePath;
                        Log.Debug("Wrote {FileSize} bytes of transformed content", fileSize);
                    }
                }
                else
                {
                    // No template - use standard formatter
                    Log.Debug("Formatting results as {OutputFormat} using standard formatter", actualOutputFormat);
                    var formatter = GetFormatter(actualOutputFormat);
                    var (formatSuccess, formatterFileSize, formatError) = await formatter.FormatAsync(rows, tempFilePath);

                    if (!formatSuccess)
                    {
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds, formatError);
                        return (executionId, false, null, formatError);
                    }

                    fileSize = formatterFileSize;
                    Log.Debug("Formatted {FileSize} bytes", fileSize);
                }

                // Check if this is email test mode with split HTML files
                var emailTestHtmlDocs = (!string.IsNullOrEmpty(transformedContent) && profile.IsEmailExport && destinationOverrideId.HasValue)
                    ? SplitHtmlDocuments(transformedContent)
                    : new List<string>();

                bool isEmailTestWithSplit = (profile.IsEmailExport && destinationOverrideId.HasValue &&
                                           finalPath != null && !string.IsNullOrEmpty(transformedContent) &&
                                           emailTestHtmlDocs.Count > 1);

                // For email test mode with split files, handle destination upload for each file
                if (isEmailTestWithSplit && destinationOverrideId.HasValue)
                {
                    Log.Information("Email test mode: Uploading {Count} split HTML files to destination",
                        emailTestHtmlDocs.Count);

                    // Get destination configuration
                    var destination = await _destinationService.GetByIdForExecutionAsync(destinationOverrideId.Value);
                    if (destination == null || destination.Type != DestinationType.Local)
                    {
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                            "Test mode requires Local destination");
                        return (executionId, false, null, "Test mode requires Local destination");
                    }

                    // Upload each file through the destination service to get proper path handling
                    string? lastFinalPath = null;

                    for (int i = 0; i < emailTestHtmlDocs.Count && i < rows.Count; i++)
                    {
                        var docFileName = $"{Path.GetFileNameWithoutExtension(tempFilePath)}_{i + 1:D3}.html";
                        var docTempPath = Path.Combine(Path.GetTempPath(), docFileName);

                        // Write each HTML doc to temp
                        await File.WriteAllTextAsync(docTempPath, emailTestHtmlDocs[i]);

                        // Upload through destination service for proper path resolution
                        (bool docUploadSuccess, string? docFinalPath, string? docUploadMessage) =
                            await _destinationService.SaveToDestinationAsync(docTempPath, DestinationType.Local,
                                destination.ConfigurationJson, maxRetries: 3);

                        if (!docUploadSuccess)
                        {
                            Log.Error("Failed to upload email document {Number}: {Error}", i + 1, docUploadMessage);
                            await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null,
                                stopwatch.ElapsedMilliseconds, $"Upload failed: {docUploadMessage}");
                            return (executionId, false, null, docUploadMessage);
                        }

                        lastFinalPath = docFinalPath;
                        Log.Information("Uploaded email document {Number} to {FilePath}", i + 1, docFinalPath);

                        // Clean up temp file
                        try { File.Delete(docTempPath); } catch { }
                    }

                    // Create metadata file in the same location
                    if (lastFinalPath != null)
                    {
                        await CreateEmailMetadataFileAsync(profile, rows, lastFinalPath);
                    }

                    // Update execution as success
                    await UpdateExecutionRecordAsync(executionId, "Success", rows.Count, lastFinalPath,
                        stopwatch.ElapsedMilliseconds, null);
                    await UpdateProfileLastExecutedAsync(profileId);

                    // Send success notification (fire and forget)
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var execution = new ProfileExecution { Id = executionId, StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow, RowCount = rows.Count, OutputPath = lastFinalPath, ExecutionTimeMs = stopwatch.ElapsedMilliseconds };
                            await _notificationService.SendExecutionSuccessAsync(execution, profile);
                        }
                        catch (Exception ex) { Log.Error(ex, "Failed to send execution success notification"); }
                    });

                    return (executionId, true, lastFinalPath, null);
                }

                // Get destination configuration (priority: Job override > Profile destination > Profile inline config)
                string? destinationConfig;
                DestinationType destinationType;

                if (destinationOverrideId.HasValue)
                {
                    // Priority 1: Job specified a destination override
                    Log.Debug("Using job destination override ID {DestinationId}", destinationOverrideId.Value);

                    var destination = await _destinationService.GetByIdForExecutionAsync(destinationOverrideId.Value);
                    if (destination == null)
                    {
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                            $"Destination override {destinationOverrideId.Value} not found");
                        return (executionId, false, null, $"Destination override {destinationOverrideId.Value} not found");
                    }

                    if (!destination.IsActive)
                    {
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                            $"Destination {destination.Name} is not active");
                        return (executionId, false, null, $"Destination {destination.Name} is not active");
                    }

                    destinationType = destination.Type;
                    destinationConfig = destination.ConfigurationJson;

                    Log.Debug("Using job destination override: {DestinationName} ({DestinationType})", destination.Name, destinationType);
                }
                else if (profile.OutputDestinationId.HasValue)
                {
                    // Priority 2: Profile has a configured destination (recommended)
                    Log.Debug("Using profile destination ID {DestinationId}", profile.OutputDestinationId.Value);

                    var destination = await _destinationService.GetByIdForExecutionAsync(profile.OutputDestinationId.Value);
                    if (destination == null)
                    {
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                            $"Profile destination {profile.OutputDestinationId.Value} not found");
                        return (executionId, false, null, $"Profile destination {profile.OutputDestinationId.Value} not found");
                    }

                    if (!destination.IsActive)
                    {
                        await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                            $"Destination {destination.Name} is not active");
                        return (executionId, false, null, $"Destination {destination.Name} is not active");
                    }

                    destinationType = destination.Type;
                    destinationConfig = destination.ConfigurationJson;

                    Log.Debug("Using profile destination: {DestinationName} ({DestinationType})", destination.Name, destinationType);
                }
                else
                {
                    // Priority 3: Fall back to profile's inline configuration (legacy/backward compatibility)
                    Log.Debug("Using profile inline destination configuration (legacy)");
                    destinationConfig = profile.OutputDestinationConfig;
                    if (string.IsNullOrEmpty(destinationConfig))
                    {
                        // Default configuration for local destination with correct file extension
                        destinationConfig = $@"{{""path"": ""exports/{DateTime.UtcNow:yyyy-MM-dd}/{DateTime.UtcNow:HHmmss}/{profile.Name.Replace(" ", "_")}.{fileExtension}""}}";
                    }

                    destinationType = Enum.TryParse<DestinationType>(profile.OutputDestinationType, true, out var parsedType)
                        ? parsedType
                        : DestinationType.Local;

                    Log.Debug("Using profile's destination: {DestinationType}", profile.OutputDestinationType);
                }

                // Save to destination with retry logic (handled in DestinationService)
                (bool destSuccess, finalPath, string? destMessage) = await _destinationService.SaveToDestinationAsync(
                    tempFilePath, destinationType, destinationConfig, maxRetries: 3);

                // CRITICAL: Only mark as success if upload succeeded
                if (!destSuccess)
                {
                    Log.Error("Destination upload failed after retries: {Error}", destMessage);
                    await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, stopwatch.ElapsedMilliseconds,
                        $"Destination upload failed: {destMessage}");
                    return (executionId, false, null, destMessage);
                }

                Log.Debug("Output saved to: {OutputPath}", finalPath);

                // Create email metadata file if this is test mode for an email profile
                if (profile.IsEmailExport && destinationOverrideId.HasValue && finalPath != null)
                {
                    await CreateEmailMetadataFileAsync(profile, rows, finalPath);
                }

                // COMMIT DELTA SYNC: Now that transformation and destination write are successful, commit the hash state
                if (profile.DeltaSyncEnabled && deltaSyncResult != null)
                {
                    try
                    {
                        await _deltaSyncService.CommitDeltaSyncAsync(profileId, executionId, deltaSyncResult);
                        Log.Information("Delta sync committed for profile {ProfileId}, execution {ExecutionId}", profileId, executionId);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail the execution - data was successfully exported
                        Log.Error(ex, "Failed to commit delta sync state for profile {ProfileId}, execution {ExecutionId}", profileId, executionId);
                    }
                }

                // Store success message and handle path based on destination type
                string? outputMessage = null;
                string? storedPath = null;

                if (destinationType == DestinationType.Http)
                {
                    // For HTTP destinations, finalPath contains the response message
                    if (!string.IsNullOrEmpty(finalPath))
                    {
                        outputMessage = finalPath;
                        Log.Debug("HTTP response captured: {Response}", outputMessage);
                    }
                    // Do not set OutputPath for HTTP destinations
                    storedPath = null;
                }
                else
                {
                    // For file-based destinations, compute relative path
                    string outputRoot;
                    var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
                    if (environment == "Development")
                    {
                        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
                        outputRoot = Path.Combine(projectDir, "exports");
                    }
                    else
                    {
                        outputRoot = Path.Combine(AppContext.BaseDirectory, "exports");
                    }
                    storedPath = finalPath != null
                        ? Path.GetRelativePath(outputRoot, finalPath).Replace("\\", "/")
                        : null;
                }

                // ===== PHASE 3: POST-PROCESSING =====
                // Build context for post-processing
                var postProcessContext = new ProcessingContext
                {
                    ExecutionId = executionId,
                    ProfileId = profileId,
                    RowCount = rows.Count,
                    OutputPath = finalPath,
                    FileSizeBytes = fileSize,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                    OutputFormat = actualOutputFormat,
                    TriggeredBy = triggeredBy,
                    StartedAt = DateTime.UtcNow,
                    CompletedAt = DateTime.UtcNow,
                    Status = "Success",
                    ErrorMessage = null,
                    DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn,
                    SplitKeyColumn = profile.SplitKeyColumn
                };

                // Execute new post-processing logic (supports both Query and StoredProcedure)
                var (postProcessSuccess, postProcessError) = await ExecutePostProcessingAsyncNew(
                    executionId, profile, connection, postProcessContext, mainQueryFailed: false);

                if (!postProcessSuccess && !profile.PostProcessRollbackOnFailure)
                {
                    // Post-processing failed but we're not rolling back
                    Log.Warning("Post-processing failed but continuing: {Error}", postProcessError);
                }
                else if (!postProcessSuccess && profile.PostProcessRollbackOnFailure)
                {
                    // Post-processing failed and we need to rollback (saga pattern compensation)
                    Log.Error("Post-processing failed with rollback enabled: {Error}", postProcessError);

                    // Attempt compensation: Delete exported file from destination
                    if (finalPath != null)
                    {
                        Log.Information("Attempting export compensation for {FilePath}", finalPath);
                        var (compensationSuccess, compensationError) = await _destinationService.CompensateExportAsync(
                            finalPath, destinationType, destinationConfig);

                        if (compensationSuccess)
                        {
                            Log.Information("Compensation successful: Deleted exported file {FilePath}", finalPath);
                        }
                        else
                        {
                            Log.Warning("Compensation failed (non-critical): {Error}. Exported file may still exist at {FilePath}",
                                compensationError, finalPath);
                        }
                    }

                    await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, storedPath,
                        stopwatch.ElapsedMilliseconds, $"Post-processing failed (compensation attempted): {postProcessError}");
                    return (executionId, false, finalPath, $"Post-processing failed: {postProcessError}");
                }

                // Update execution record with success
                stopwatch.Stop();
                await UpdateExecutionRecordAsync(executionId, "Success", rows.Count, storedPath, stopwatch.ElapsedMilliseconds, null, outputMessage);

                // Update delta sync metrics if enabled
                if (profile.DeltaSyncEnabled && deltaSyncResult != null)
                {
                    await UpdateDeltaSyncMetricsAsync(executionId, deltaSyncResult);
                }

                // Update profile's last executed timestamp
                await UpdateProfileLastExecutedAsync(profileId);

                // Audit log
                await _auditService.LogAsync("Profile", profileId, "Executed", triggeredBy,

                    System.Text.Json.JsonSerializer.Serialize(new { rows.Count, finalPath, ExecutionTimeMs = stopwatch.ElapsedMilliseconds }));

                Log.Information("Profile execution completed successfully. ExecutionId: {ExecutionId}, RowCount: {RowCount}, OutputPath: {OutputPath}",
                    executionId, rows.Count, storedPath);

                return (executionId, true, finalPath, null);
            }
            finally
            {
                // Cleanup temp file (best effort)
                if (tempFilePath != null && File.Exists(tempFilePath))
                {
                    try
                    {
                        File.Delete(tempFilePath);
                        Log.Debug("Cleaned up temporary file: {TempFilePath}", tempFilePath);
                    }
                    catch (Exception cleanupEx)
                    {
                        Log.Warning(cleanupEx, "Failed to delete temporary file: {TempFilePath}", tempFilePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error(ex, "Unexpected error executing profile {ProfileId}", profileId);

            if (executionId > 0)
            {
                await UpdateExecutionRecordAsync(executionId, "Failed", 0, null, stopwatch.ElapsedMilliseconds, ex.Message);

                // Send failure notification (fire and forget)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var profile = await _profileService.GetByIdAsync(profileId);
                        if (profile != null)
                        {
                            var execution = new ProfileExecution { Id = executionId, ErrorMessage = ex.Message, ExecutionTimeMs = stopwatch.ElapsedMilliseconds };
                            await _notificationService.SendExecutionFailureAsync(execution, profile);
                        }
                    }
                    catch (Exception notifEx) { Log.Error(notifEx, "Failed to send execution failure notification"); }
                });
            }

            return (executionId, false, null, ex.Message);
        }
    }

    /// <summary>
    /// Execute profile with multi-output splitting enabled
    /// Generates multiple output files based on SplitKeyColumn
    /// </summary>
    private async Task<(int ExecutionId, bool Success, string? OutputPath, string? ErrorMessage)>
        ExecuteWithSplittingAsync(
            Profile profile,
            List<Dictionary<string, object>> rows,
            int executionId,
            string triggeredBy,
            int? destinationOverrideId,
            Stopwatch stopwatch,
            DeltaSyncResult? deltaSyncResult)
    {
        try
        {
            Log.Information("Profile {ProfileId} executing with splitting enabled, split key: {SplitKey}", 
                profile.Id, profile.SplitKeyColumn);
            
            // Validate split key column exists in results
            if (!rows[0].ContainsKey(profile.SplitKeyColumn!))
            {
                var error = $"Split key column '{profile.SplitKeyColumn}' not found in query results. " +
                           $"Available columns: {string.Join(", ", rows[0].Keys)}";
                Log.Error(error);
                await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null, 
                    stopwatch.ElapsedMilliseconds, error);
                return (executionId, false, null, error);
            }
            
            // Group rows by split key value
            var splitGroups = rows
                .GroupBy(row => NormalizeSplitKey(row[profile.SplitKeyColumn!]))
                .ToDictionary(g => g.Key, g => g.ToList());
            
            Log.Information("Split {TotalRows} rows into {SplitCount} groups by '{SplitKey}'",
                rows.Count, splitGroups.Count, profile.SplitKeyColumn);

            // Get destination configuration (use ForExecution to get decrypted config)
            var destination = await _destinationService.GetByIdForExecutionAsync(
                destinationOverrideId ?? profile.OutputDestinationId ?? 0);
            
            if (destination == null)
            {
                var error = "Destination not found";
                await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null,
                    stopwatch.ElapsedMilliseconds, error);
                return (executionId, false, null, error);
            }
            
            // Process each split group
            int successCount = 0;
            int failureCount = 0;
            var errors = new List<string>();
            
            foreach (var splitGroup in splitGroups)
            {
                var splitKey = splitGroup.Key;
                var splitRows = splitGroup.Value;
                
                Log.Debug("Processing split '{SplitKey}' with {RowCount} rows", splitKey, splitRows.Count);
                
                var (success, error) = await ProcessSingleSplitAsync(
                    profile,
                    executionId,
                    splitKey,
                    splitRows,
                    destination.Type,
                    destination.ConfigurationJson);
                
                if (success)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                    errors.Add($"{splitKey}: {error}");
                }
            }
            
            // Update execution record with split summary
            stopwatch.Stop();
            bool overallSuccess = failureCount == 0;
            string status = failureCount == 0 ? "Success" : 
                           successCount == 0 ? "Failed" : "Partial";
            
            string? errorMessage = failureCount > 0 
                ? $"{failureCount} of {splitGroups.Count} splits failed: {string.Join("; ", errors.Take(3))}"
                : null;
            
            await UpdateExecutionWithSplitSummaryAsync(
                executionId,
                splitGroups.Count,
                successCount,
                failureCount,
                rows.Count,
                stopwatch.ElapsedMilliseconds);
            
            Log.Information(
                "Split execution for profile {ProfileId} completed: {Success}/{Total} splits succeeded",
                profile.Id, successCount, splitGroups.Count);
            
            // COMMIT DELTA SYNC: Now that all splits have been processed successfully, commit the hash state
            if (profile.DeltaSyncEnabled && deltaSyncResult != null && overallSuccess)
            {
                try
                {
                    await _deltaSyncService.CommitDeltaSyncAsync(profile.Id, executionId, deltaSyncResult);
                    Log.Information("Delta sync committed for profile {ProfileId}, execution {ExecutionId} after split processing", 
                        profile.Id, executionId);
                }
                catch (Exception ex)
                {
                    // Log but don't fail the execution - data was successfully exported
                    Log.Error(ex, "Failed to commit delta sync state for profile {ProfileId}, execution {ExecutionId}", 
                        profile.Id, executionId);
                }
            }
            
            // Update profile's last executed timestamp
            await UpdateProfileLastExecutedAsync(profile.Id);
            
            // Audit log
            await _auditService.LogAsync("Profile", profile.Id, "ExecutedWithSplitting", triggeredBy,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    SplitCount = splitGroups.Count,
                    SuccessCount = successCount,
                    FailureCount = failureCount,
                    TotalRows = rows.Count,
                    ExecutionTimeMs = stopwatch.ElapsedMilliseconds
                }));
            
            return (executionId, overallSuccess, null, errorMessage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Split execution failed for profile {ProfileId}", profile.Id);
            stopwatch.Stop();
            await UpdateExecutionRecordAsync(executionId, "Failed", rows.Count, null,
                stopwatch.ElapsedMilliseconds, ex.Message);
            return (executionId, false, null, ex.Message);
        }
    }

    /// <summary>
    /// Get list of recent executions
    /// </summary>
    public async Task<List<ProfileExecution>> GetRecentExecutionsAsync(int limit = 100)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                pe.*,
                p.Name as ProfileName
            FROM ProfileExecutions pe
            LEFT JOIN Profiles p ON pe.ProfileId = p.Id
            ORDER BY pe.StartedAt DESC
            LIMIT @Limit";

        var executions = await connection.QueryAsync<ProfileExecution>(sql, new { Limit = limit });
        return executions.ToList();
    }

    /// <summary>
    /// Get executions with pagination and filtering
    /// </summary>
    public async Task<ExecutionPagedResult> GetExecutionsPagedAsync(
        int? profileId = null,
        int? jobId = null,
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? search = null,
        int page = 1,
        int pageSize = 50)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var whereConditions = new List<string>();
        var parameters = new DynamicParameters();

        if (profileId.HasValue)
        {
            whereConditions.Add("ProfileId = @ProfileId");
            parameters.Add("ProfileId", profileId.Value);
        }

        if (jobId.HasValue)
        {
            whereConditions.Add("JobId = @JobId");
            parameters.Add("JobId", jobId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            whereConditions.Add("Status = @Status");
            parameters.Add("Status", status);
        }

        if (fromDate.HasValue)
        {
            whereConditions.Add("StartedAt >= @FromDate");
            parameters.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            whereConditions.Add("StartedAt <= @ToDate");
            parameters.Add("ToDate", toDate.Value.AddDays(1).AddSeconds(-1));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            whereConditions.Add("(p.Name LIKE @Search OR pe.Status LIKE @Search)");
            parameters.Add("Search", $"%{search}%");
        }

        var whereClause = whereConditions.Count > 0
            ? "WHERE " + string.Join(" AND ", whereConditions)
            : "";

        // Get total count
        var countSql = $"SELECT COUNT(*) FROM ProfileExecutions pe LEFT JOIN Profiles p ON pe.ProfileId = p.Id {whereClause}";
        var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

        // Get paged data
        var offset = (page - 1) * pageSize;
        parameters.Add("Limit", pageSize);
        parameters.Add("Offset", offset);

        var dataSql = $@"
            SELECT
                pe.*,
                p.Name as ProfileName
            FROM ProfileExecutions pe
            LEFT JOIN Profiles p ON pe.ProfileId = p.Id
            {whereClause}
            ORDER BY pe.StartedAt DESC
            LIMIT @Limit OFFSET @Offset";

        var executions = await connection.QueryAsync<ProfileExecution>(dataSql, parameters);

        return new ExecutionPagedResult
        {
            Data = executions.ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    /// <summary>
    /// Get executions for a specific profile
    /// </summary>
    public async Task<List<ProfileExecution>> GetExecutionsByProfileIdAsync(int profileId, int limit = 50)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                pe.*,
                p.Name as ProfileName
            FROM ProfileExecutions pe
            LEFT JOIN Profiles p ON pe.ProfileId = p.Id
            WHERE pe.ProfileId = @ProfileId
            ORDER BY pe.StartedAt DESC
            LIMIT @Limit";

        var executions = await connection.QueryAsync<ProfileExecution>(sql, new { ProfileId = profileId, Limit = limit });
        return executions.ToList();
    }

    /// <summary>
    /// Get execution by ID
    /// </summary>
    public async Task<ProfileExecution?> GetExecutionByIdAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                pe.*,
                p.Name as ProfileName
            FROM ProfileExecutions pe
            LEFT JOIN Profiles p ON pe.ProfileId = p.Id
            WHERE pe.Id = @Id";
        return await connection.QueryFirstOrDefaultAsync<ProfileExecution>(sql, new { Id = id });
    }

    /// <summary>
    /// Get the last execution for a specific profile
    /// </summary>
    public async Task<ProfileExecution?> GetLastExecutionForProfileAsync(int profileId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                pe.*,
                p.Name as ProfileName
            FROM ProfileExecutions pe
            LEFT JOIN Profiles p ON pe.ProfileId = p.Id
            WHERE pe.ProfileId = @ProfileId
            ORDER BY pe.StartedAt DESC
            LIMIT 1";
        return await connection.QueryFirstOrDefaultAsync<ProfileExecution>(sql, new { ProfileId = profileId });
    }

    /// <summary>
    /// Create initial execution record
    /// </summary>
    private async Task<int> CreateExecutionRecordAsync(int profileId, string triggeredBy, int? jobId = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO ProfileExecutions (ProfileId, JobId, StartedAt, Status, TriggeredBy)
            VALUES (@ProfileId, @JobId, @StartedAt, 'Running', @TriggeredBy);
            SELECT last_insert_rowid();";

        var id = await connection.ExecuteScalarAsync<int>(sql, new 
        { 
            ProfileId = profileId,
            JobId = jobId,
            StartedAt = DateTime.UtcNow,
            TriggeredBy = triggeredBy
        });

        return id;
    }

    /// <summary>
    /// Update execution record with final results
    /// </summary>
    private async Task UpdateExecutionRecordAsync(int executionId, string status, int rowCount, 
        string? outputPath, long executionTimeMs, string? errorMessage, string? outputMessage = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE ProfileExecutions 
            SET Status = @Status,
                CompletedAt = @CompletedAt,
                RowCount = @RowCount,
                OutputPath = @OutputPath,
                ExecutionTimeMs = @ExecutionTimeMs,
                ErrorMessage = @ErrorMessage,
                OutputMessage = @OutputMessage
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new 
        { 
            Id = executionId,
            Status = status,
            CompletedAt = DateTime.UtcNow,
            RowCount = rowCount,
            OutputPath = outputPath,
            ExecutionTimeMs = executionTimeMs,
            ErrorMessage = errorMessage,
            OutputMessage = outputMessage
        });
    }

    /// <summary>
    /// Update delta sync metrics in the execution record
    /// </summary>
    private async Task UpdateDeltaSyncMetricsAsync(int executionId, DeltaSyncResult deltaSyncResult)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE ProfileExecutions 
            SET DeltaSyncNewRows = @NewRows,
                DeltaSyncChangedRows = @ChangedRows,
                DeltaSyncDeletedRows = @DeletedRows,
                DeltaSyncUnchangedRows = @UnchangedRows,
                DeltaSyncTotalHashedRows = @TotalHashedRows
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new 
        { 
            Id = executionId,
            NewRows = deltaSyncResult.NewRows.Count,
            ChangedRows = deltaSyncResult.ChangedRows.Count,
            DeletedRows = deltaSyncResult.DeletedReefIds.Count,
            UnchangedRows = deltaSyncResult.UnchangedRows.Count,
            TotalHashedRows = deltaSyncResult.TotalRowsProcessed
        });

        Log.Debug("Updated delta sync metrics for execution {ExecutionId}", executionId);
    }

    /// <summary>
    /// Execute pre-processing query or stored procedure before main query execution
    /// </summary>
    /// <param name="executionId">Current execution ID for tracking</param>
    /// <param name="profile">Profile containing pre-processing configuration</param>
    /// <param name="connection">Database connection to use</param>
    /// <param name="context">Context data for variable substitution</param>
    /// <returns>Success status and optional error message</returns>
    private async Task<(bool Success, string? ErrorMessage)> ExecutePreProcessingAsync(
        int executionId, 
        Profile profile,
        Connection connection,
        ProcessingContext context)
    {
        if (string.IsNullOrEmpty(profile.PreProcessType) || string.IsNullOrEmpty(profile.PreProcessConfig))
        {
            Log.Debug("No pre-processing configured for profile {ProfileId}", profile.Id);
            return (true, null);
        }

        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTime.UtcNow;

        try
        {
            Log.Debug("Starting pre-processing for execution {ExecutionId}", executionId);
            await UpdatePreProcessingStatusAsync(executionId, startedAt, null, "Running", null, null);

            // Parse configuration
            ProcessingConfig? config;
            try
            {
                config = System.Text.Json.JsonSerializer.Deserialize<ProcessingConfig>(profile.PreProcessConfig);
                if (config == null)
                {
                    throw new Exception("Failed to deserialize pre-processing configuration");
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Invalid pre-processing configuration: {ex.Message}";
                Log.Error(ex, errorMsg);
                await UpdatePreProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Failed", errorMsg, stopwatch.ElapsedMilliseconds);
                return (false, errorMsg);
            }

            // Build and execute command
            var command = BuildDatabaseCommand(connection.Type, config, context);
            Log.Debug("Executing pre-processing command: {Command}", command);

            var parameters = BuildParametersDictionary(config.Parameters, context);
            var (success, rowsAffected, error, _) = await _queryExecutor.ExecuteCommandAsync(connection, command, parameters, config.Timeout);

            stopwatch.Stop();

            if (success)
            {
                Log.Debug("Pre-processing completed successfully in {ElapsedMs}ms. Rows affected: {RowsAffected}", 
                    stopwatch.ElapsedMilliseconds, rowsAffected);
                await UpdatePreProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Success", null, stopwatch.ElapsedMilliseconds);
                return (true, null);
            }
            else
            {
                Log.Warning("Pre-processing failed: {Error}", error);
                await UpdatePreProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Failed", error, stopwatch.ElapsedMilliseconds);
                
                if (!config.ContinueOnError)
                {
                    return (false, error);
                }
                
                Log.Information("Continuing execution despite pre-processing error (ContinueOnError=true)");
                return (true, null); // Continue execution
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = $"Pre-processing exception: {ex.Message}";
            Log.Error(ex, error);
            await UpdatePreProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Failed", error, stopwatch.ElapsedMilliseconds);
            return (false, error);
        }
    }

    /// <summary>
    /// Execute post-processing query or stored procedure after main query execution
    /// Enhanced version supporting both Query and StoredProcedure types with new configuration format
    /// </summary>
    /// <param name="executionId">Current execution ID for tracking</param>
    /// <param name="profile">Profile containing post-processing configuration</param>
    /// <param name="connection">Database connection to use</param>
    /// <param name="context">Context data for variable substitution</param>
    /// <param name="mainQueryFailed">Whether the main query failed</param>
    /// <returns>Success status and optional error message</returns>
    private async Task<(bool Success, string? ErrorMessage)> ExecutePostProcessingAsyncNew(
        int executionId,
        Profile profile,
        Connection connection,
        ProcessingContext context,
        bool mainQueryFailed)
    {
        if (string.IsNullOrEmpty(profile.PostProcessType) || string.IsNullOrEmpty(profile.PostProcessConfig))
        {
            Log.Debug("No post-processing configured for profile {ProfileId}", profile.Id);
            return (true, null);
        }

        // Check if we should skip post-processing due to main query failure
        if (mainQueryFailed && profile.PostProcessSkipOnFailure)
        {
            Log.Information("Skipping post-processing because main query failed and PostProcessSkipOnFailure=true");
            await UpdatePostProcessingStatusAsync(executionId, DateTime.UtcNow, DateTime.UtcNow, "Skipped", 
                "Skipped due to main query failure", 0);
            return (true, null);
        }

        var stopwatch = Stopwatch.StartNew();
        var startedAt = DateTime.UtcNow;

        try
        {
            Log.Debug("Starting post-processing for execution {ExecutionId}", executionId);
            await UpdatePostProcessingStatusAsync(executionId, startedAt, null, "Running", null, null);

            // Parse configuration
            ProcessingConfig? config;
            try
            {
                config = System.Text.Json.JsonSerializer.Deserialize<ProcessingConfig>(profile.PostProcessConfig);
                if (config == null)
                {
                    throw new Exception("Failed to deserialize post-processing configuration");
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Invalid post-processing configuration: {ex.Message}";
                Log.Error(ex, errorMsg);
                await UpdatePostProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Failed", errorMsg, stopwatch.ElapsedMilliseconds);
                return (false, errorMsg);
            }

            // Build and execute command
            var command = BuildDatabaseCommand(connection.Type, config, context);
            Log.Debug("Executing post-processing command: {Command}", command);

            var parameters = BuildParametersDictionary(config.Parameters, context);
            var (success, rowsAffected, error, _) = await _queryExecutor.ExecuteCommandAsync(connection, command, parameters, config.Timeout);

            stopwatch.Stop();

            if (success)
            {
                Log.Debug("Post-processing completed successfully in {ElapsedMs}ms. Rows affected: {RowsAffected}", 
                    stopwatch.ElapsedMilliseconds, rowsAffected);
                await UpdatePostProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Success", null, stopwatch.ElapsedMilliseconds);
                return (true, null);
            }
            else
            {
                Log.Warning("Post-processing failed: {Error}", error);
                await UpdatePostProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Failed", error, stopwatch.ElapsedMilliseconds);
                
                if (!config.ContinueOnError)
                {
                    return (false, error);
                }
                
                Log.Information("Ignoring post-processing error (ContinueOnError=true)");
                return (true, null); // Don't fail overall execution
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = $"Post-processing exception: {ex.Message}";
            Log.Error(ex, error);
            await UpdatePostProcessingStatusAsync(executionId, startedAt, DateTime.UtcNow, "Failed", error, stopwatch.ElapsedMilliseconds);
            return (false, error);
        }
    }

    /// <summary>
    /// Build database-specific command for query or stored procedure execution
    /// Handles SQL Server (EXEC), MySQL (CALL), and PostgreSQL (CALL) syntax
    /// </summary>
    /// <param name="connectionType">Database type (SqlServer, MySQL, PostgreSQL)</param>
    /// <param name="config">Processing configuration</param>
    /// <param name="context">Context for variable substitution</param>
    /// <returns>SQL command string</returns>
    private string BuildDatabaseCommand(string connectionType, ProcessingConfig config, ProcessingContext context)
    {
        // Substitute context variables in the command
        var command = SubstituteContextVariables(config.Command, context);

        // For Query type, return the command as-is (already SQL)
        if (config.Type.Equals("Query", StringComparison.OrdinalIgnoreCase))
        {
            return command;
        }

        // For StoredProcedure type, build database-specific syntax
        if (config.Type.Equals("StoredProcedure", StringComparison.OrdinalIgnoreCase))
        {
            var procedureName = command.Trim();
            
            // Build parameter list if provided
            string parameterList = "";
            if (config.Parameters != null && config.Parameters.Count > 0)
            {
                var paramNames = config.Parameters.Select(p => 
                {
                    var paramName = p.Name.StartsWith("@") || p.Name.StartsWith("p_") ? p.Name : $"@{p.Name}";
                    return paramName;
                });
                parameterList = " " + string.Join(", ", paramNames);
            }

            // Generate database-specific syntax
            return connectionType.ToLowerInvariant() switch
            {
                "sqlserver" => $"EXEC {procedureName}{parameterList}",
                "mysql" => $"CALL {procedureName}({parameterList.Trim()})",
                "postgresql" => $"CALL {procedureName}({parameterList.Trim()})",
                _ => $"EXEC {procedureName}{parameterList}" // Default to SQL Server syntax
            };
        }

        throw new ArgumentException($"Unknown processing type: {config.Type}");
    }

    /// <summary>
    /// Substitute context variables in a string with actual values
    /// Supports: {executionid}, {profileid}, {rowcount}, {outputpath}, {filesizebytes}, 
    ///           {executiontimems}, {outputformat}, {triggeredby}, {startedat}, {completedat}, 
    ///           {status}, {errormessage}
    /// </summary>
    /// <param name="input">Input string with {placeholder} variables</param>
    /// <param name="context">Context containing actual values</param>
    /// <returns>String with variables replaced</returns>
    private string SubstituteContextVariables(string input, ProcessingContext context)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var result = input;
        
        // Replace all supported context variables
        result = result.Replace("{executionid}", context.ExecutionId.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{profileid}", context.ProfileId.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{rowcount}", context.RowCount.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{outputpath}", context.OutputPath ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{filesizebytes}", (context.FileSizeBytes ?? 0).ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{executiontimems}", context.ExecutionTimeMs.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{outputformat}", context.OutputFormat, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{triggeredby}", context.TriggeredBy ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{startedat}", context.StartedAt.ToString("O"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{completedat}", (context.CompletedAt?.ToString("O") ?? ""), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{status}", context.Status, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{errormessage}", context.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{deltasyncreefidcolumn}", context.DeltaSyncReefIdColumn ?? "", StringComparison.OrdinalIgnoreCase);
        result = result.Replace("{splitkeycolumn}", context.SplitKeyColumn ?? "", StringComparison.OrdinalIgnoreCase);
    result = result.Replace("{splitkey}", context.SplitKey ?? "", StringComparison.OrdinalIgnoreCase);

        return result;
    }

    /// <summary>
    /// Build parameters dictionary with context variable substitution
    /// </summary>
    /// <param name="configParameters">Parameters from ProcessingConfig</param>
    /// <param name="context">Context for variable substitution</param>
    /// <returns>Dictionary of parameter name/value pairs</returns>
    private Dictionary<string, string> BuildParametersDictionary(
        List<ProcessingParameter>? configParameters, 
        ProcessingContext context)
    {
        var parameters = new Dictionary<string, string>();

        if (configParameters == null || configParameters.Count == 0)
        {
            return parameters;
        }

        foreach (var param in configParameters)
        {
            // Preserve @ prefix for parameter names to match SQL/stored procedure expectations
            var paramName = param.Name.StartsWith("@") ? param.Name : "@" + param.Name;
            var paramValue = SubstituteContextVariables(param.Value, context);
            parameters[paramName] = paramValue;
        }

        return parameters;
    }

    /// <summary>
    /// Update pre-processing status in ProfileExecutions table
    /// </summary>
    private async Task UpdatePreProcessingStatusAsync(
        int executionId,
        DateTime startedAt,
        DateTime? completedAt,
        string status,
        string? error,
        long? timeMs)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE ProfileExecutions 
            SET PreProcessStartedAt = @StartedAt,
                PreProcessCompletedAt = @CompletedAt,
                PreProcessStatus = @Status,
                PreProcessError = @Error,
                PreProcessTimeMs = @TimeMs
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            Id = executionId,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Status = status,
            Error = error,
            TimeMs = timeMs
        });
    }

    /// <summary>
    /// Update post-processing status in ProfileExecutions table
    /// </summary>
    private async Task UpdatePostProcessingStatusAsync(
        int executionId,
        DateTime startedAt,
        DateTime? completedAt,
        string status,
        string? error,
        long? timeMs)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE ProfileExecutions 
            SET PostProcessStartedAt = @StartedAt,
                PostProcessCompletedAt = @CompletedAt,
                PostProcessStatus = @Status,
                PostProcessError = @Error,
                PostProcessTimeMs = @TimeMs
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            Id = executionId,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Status = status,
            Error = error,
            TimeMs = timeMs
        });
    }

    /// <summary>
    /// Update profile's last executed timestamp
    /// </summary>
    private async Task UpdateProfileLastExecutedAsync(int profileId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE Profiles 
            SET LastExecutedAt = @LastExecutedAt
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new 
        { 
            Id = profileId,
            LastExecutedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Execute post-processing stored procedure
    /// </summary>
    private async Task ExecutePostProcessingAsync(Connection connection, string postProcessConfig, 
        int rowCount, string? outputPath)
    {
        try
        {
            var config = System.Text.Json.JsonDocument.Parse(postProcessConfig);
            var root = config.RootElement;

            if (!root.TryGetProperty("name", out var nameElement))
            {
                Log.Warning("Post-process configuration missing 'name' property");
                return;
            }

            var procedureName = nameElement.GetString();
            if (string.IsNullOrEmpty(procedureName))
            {
                Log.Warning("Post-process procedure name is empty");
                return;
            }

            // Build parameter list
            var parameters = new Dictionary<string, string>();
            
            if (root.TryGetProperty("parameters", out var paramsElement) && paramsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var param in paramsElement.EnumerateArray())
                {
                    if (param.TryGetProperty("name", out var paramName) && param.TryGetProperty("value", out var paramValue))
                    {
                        var value = paramValue.GetString() ?? "";
                        
                        // Substitute placeholders
                        value = value.Replace("{rowcount}", rowCount.ToString());
                        value = value.Replace("{outputpath}", outputPath ?? "");
                        
                        parameters[paramName.GetString() ?? ""] = value;
                    }
                }
            }

            // Execute the stored procedure
            var query = $"EXEC {procedureName}";
            if (parameters.Count > 0)
            {
                var paramList = string.Join(", ", parameters.Select(p => $"{p.Key}=@{p.Key.TrimStart('@')}"));
                query += $" {paramList}";
            }

            Log.Debug("Executing post-processing: {Query}", query);
            await _queryExecutor.ExecuteQueryAsync(connection, query, parameters);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing post-processing");
            throw;
        }
    }

    /// <summary>
    /// Process a single split (group of rows with same split key value)
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage)> ProcessSingleSplitAsync(
        Profile profile,
        int executionId,
        string splitKey,
        List<Dictionary<string, object>> splitRows,
        DestinationType destinationType,
        string destinationConfig)
    {
        int splitRecordId = 0;
        string? tempFilePath = null;
        
        try
        {
            // Record split start
            splitRecordId = await RecordSplitStartAsync(executionId, splitKey, splitRows.Count);
            
            // Apply FilterInternalColumns to remove ReefId/SplitKey if configured
            var filteredRows = FilterInternalColumns(profile, splitRows);
            
            // Apply template transformation if specified
            string? transformedContent = null;
            string actualOutputFormat = profile.OutputFormat;
            
            if (profile.TemplateId.HasValue)
            {
                transformedContent = await ApplyTemplateToSplitAsync(profile, splitKey, filteredRows);
                
                var template = await _templateService.GetByIdAsync(profile.TemplateId.Value);
                if (template != null)
                {
                    actualOutputFormat = template.OutputFormat;
                }
            }
            
            // Generate filename for this split
            var extension = GetFileExtension(actualOutputFormat);
            var filename = GenerateSplitFilename(
                profile.SplitFilenameTemplate!,
                profile.Name,
                splitKey,
                extension);

            // Normalize path separators for cross-platform compatibility
            filename = filename.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

            // Create temporary file
            tempFilePath = Path.Combine(Path.GetTempPath(), "reef_splits", filename);
            var directory = Path.GetDirectoryName(tempFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Format and write output
            long fileSizeBytes;
            
            if (!string.IsNullOrEmpty(transformedContent))
            {
                // Template was applied, write directly
                await File.WriteAllTextAsync(tempFilePath, transformedContent);
                fileSizeBytes = new FileInfo(tempFilePath).Length;
            }
            else
            {
                // Use standard formatter
                var formatter = GetFormatter(actualOutputFormat);
                var (formatSuccess, fileSize, formatError) = await formatter.FormatAsync(filteredRows, tempFilePath);
                
                if (!formatSuccess)
                {
                    throw new Exception($"Formatting failed: {formatError}");
                }
                
                fileSizeBytes = fileSize;
            }
            
            Log.Debug("Split '{SplitKey}' formatted to {FilePath} ({FileSize} bytes)",
                splitKey, tempFilePath, fileSizeBytes);
            
            // Upload to destination
            var (uploadSuccess, finalPath, uploadError) = await _destinationService.SaveToDestinationAsync(
                tempFilePath,
                destinationType,
                destinationConfig);
            
            if (!uploadSuccess)
            {
                throw new Exception($"Upload failed: {uploadError}");
            }
            
            Log.Information("Split '{SplitKey}' uploaded successfully to {FinalPath}", splitKey, finalPath);
            
            // Record success
            await RecordSplitSuccessAsync(splitRecordId, finalPath, fileSizeBytes);

            // If PostProcessPerSplit is enabled, run post-processing for this split
            if (profile.PostProcessPerSplit)
            {
                try
                {
                    var connection = await _connectionService.GetByIdAsync(profile.ConnectionId);
                    if (connection == null)
                    {
                        Log.Warning("Connection {ConnectionId} not found for post-processing split '{SplitKey}'", profile.ConnectionId, splitKey);
                    }
                    else
                    {
                        var postProcessContext = new ProcessingContext
                        {
                            ExecutionId = executionId,
                            ProfileId = profile.Id,
                            RowCount = splitRows.Count,
                            OutputPath = finalPath,
                            FileSizeBytes = fileSizeBytes,
                            OutputFormat = actualOutputFormat,
                            TriggeredBy = "System:Split",
                            StartedAt = DateTime.UtcNow, // This might need adjustment
                            Status = "Success",
                            DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn,
                            SplitKeyColumn = profile.SplitKeyColumn,
                            SplitKey = splitKey
                        };

                        var (postProcessSuccess, postProcessError) = await ExecutePostProcessingAsyncNew(
                            executionId, profile, connection, postProcessContext, mainQueryFailed: false);

                        if (!postProcessSuccess)
                        {
                            Log.Warning("Post-processing failed for split '{SplitKey}': {Error}", splitKey, postProcessError);
                            // Continue processing other splits
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during post-processing for split '{SplitKey}'", splitKey);
                    // Continue processing other splits
                }
            }
            
            return (true, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing split '{SplitKey}'", splitKey);
            
            if (splitRecordId > 0)
            {
                await RecordSplitFailureAsync(splitRecordId, ex.Message);
            }
            
            return (false, ex.Message);
        }
        finally
        {
            // Clean up temp file
            if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete temporary file: {FilePath}", tempFilePath);
                }
            }
        }
    }

    /// <summary>
    /// Apply template transformation to a single split's rows
    /// </summary>
    private async Task<string> ApplyTemplateToSplitAsync(
        Profile profile,
        string splitKey,
        List<Dictionary<string, object>> splitRows)
    {
        var template = await _templateService.GetByIdAsync(profile.TemplateId!.Value);
        
        if (template == null)
        {
            throw new InvalidOperationException($"Template {profile.TemplateId} not found");
        }
        
        // Use the template engine's TransformAsync method
        return await _templateEngine.TransformAsync(splitRows, template.Template);
    }

    /// <summary>
    /// Generate filename for a standard execution using template placeholders
    /// </summary>
    private string GenerateFilename(
        Profile profile,
        string extension,
        int executionId)
    {
        if (string.IsNullOrWhiteSpace(profile.FilenameTemplate))
        {
            return $"reef_export_{executionId}_{Guid.NewGuid()}.{extension.TrimStart('.')}";
        }

        var now = DateTime.UtcNow;
        
        return profile.FilenameTemplate
            .Replace("{profile}", SanitizeFilename(profile.Name), StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", now.ToString("yyyyMMdd_HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{guid}", Guid.NewGuid().ToString("N"), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Generate filename for a split using template placeholders
    /// </summary>
    private string GenerateSplitFilename(
        string template,
        string profileName,
        string splitKey,
        string extension)
    {
        var now = DateTime.UtcNow;
        
        return template
            .Replace("{profile}", SanitizeFilename(profileName), StringComparison.OrdinalIgnoreCase)
            .Replace("{splitkey}", SanitizeFilename(splitKey), StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", now.ToString("yyyyMMdd_HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{guid}", Guid.NewGuid().ToString("N"), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", extension.TrimStart('.'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalize split key for consistency (trim, handle nulls, sanitize)
    /// </summary>
    private string NormalizeSplitKey(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "_NULL_";
        }
        
        var stringValue = value.ToString()?.Trim();
        
        if (string.IsNullOrEmpty(stringValue))
        {
            return "_EMPTY_";
        }
        
        return stringValue;
    }

    /// <summary>
    /// Sanitize filename to remove invalid characters
    /// </summary>
    private string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", filename.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Record the start of a split execution
    /// </summary>
    private async Task<int> RecordSplitStartAsync(int executionId, string splitKey, int rowCount)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            INSERT INTO ProfileExecutionSplits 
                (ExecutionId, SplitKey, RowCount, Status, StartedAt)
            VALUES 
                (@ExecutionId, @SplitKey, @RowCount, 'Running', @StartedAt);
            SELECT last_insert_rowid();";
        
        return await connection.ExecuteScalarAsync<int>(sql, new
        {
            ExecutionId = executionId,
            SplitKey = splitKey,
            RowCount = rowCount,
            StartedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Record split success
    /// </summary>
    private async Task RecordSplitSuccessAsync(int splitRecordId, string? outputPath, long fileSizeBytes)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE ProfileExecutionSplits
            SET Status = 'Success',
                OutputPath = @OutputPath,
                FileSizeBytes = @FileSizeBytes,
                CompletedAt = @CompletedAt
            WHERE Id = @Id";
        
        await connection.ExecuteAsync(sql, new
        {
            Id = splitRecordId,
            OutputPath = outputPath,
            FileSizeBytes = fileSizeBytes,
            CompletedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Record split failure
    /// </summary>
    private async Task RecordSplitFailureAsync(int splitRecordId, string? errorMessage)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            UPDATE ProfileExecutionSplits
            SET Status = 'Failed',
                ErrorMessage = @ErrorMessage,
                CompletedAt = @CompletedAt
            WHERE Id = @Id";
        
        await connection.ExecuteAsync(sql, new
        {
            Id = splitRecordId,
            ErrorMessage = errorMessage,
            CompletedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Insert multiple execution split records (used for email exports)
    /// </summary>
    private async Task InsertExecutionSplitsAsync(int executionId, List<ProfileExecutionSplit> splits)
    {
        if (splits == null || splits.Count == 0)
        {
            Log.Debug("No splits to insert for execution {ExecutionId}", executionId);
            return;
        }

        Log.Debug("InsertExecutionSplitsAsync called with {SplitCount} splits for execution {ExecutionId}", splits.Count, executionId);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO ProfileExecutionSplits
                (ExecutionId, SplitKey, RowCount, Status, ErrorMessage, StartedAt, CompletedAt)
            VALUES
                (@ExecutionId, @SplitKey, @RowCount, @Status, @ErrorMessage, @StartedAt, @CompletedAt)";

        int insertedCount = 0;
        foreach (var split in splits)
        {
            try
            {
                await connection.ExecuteAsync(sql, new
                {
                    ExecutionId = executionId,
                    SplitKey = split.SplitKey,
                    RowCount = split.RowCount,
                    Status = split.Status,
                    ErrorMessage = split.ErrorMessage,
                    StartedAt = split.StartedAt,
                    CompletedAt = split.CompletedAt ?? DateTime.UtcNow
                });
                insertedCount++;
                Log.Debug("Inserted split record: {SplitKey} - {Status}", split.SplitKey, split.Status);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to insert split record for execution {ExecutionId}: {SplitKey}", executionId, split.SplitKey);
                throw;
            }
        }

        Log.Debug("InsertExecutionSplitsAsync completed: inserted {InsertedCount}/{TotalCount} split records", insertedCount, splits.Count);
    }

    /// <summary>
    /// Update execution record with split summary
    /// </summary>
    private async Task UpdateExecutionWithSplitSummaryAsync(
        int executionId,
        int splitCount,
        int successCount,
        int failureCount,
        int totalRowCount,
        long elapsedMs,
        string? outputFormat = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        string status = failureCount == 0 ? "Success" :
                       successCount == 0 ? "Failed" : "Partial";

        string? errorMessage = failureCount > 0
            ? $"{failureCount} of {splitCount} splits failed"
            : null;

        const string sql = @"
            UPDATE ProfileExecutions
            SET Status = @Status,
                RowCount = @RowCount,
                WasSplit = 1,
                SplitCount = @SplitCount,
                SplitSuccessCount = @SplitSuccessCount,
                SplitFailureCount = @SplitFailureCount,
                ErrorMessage = @ErrorMessage,
                ExecutionTimeMs = @ExecutionTimeMs,
                OutputFormat = COALESCE(@OutputFormat, OutputFormat),
                CompletedAt = @CompletedAt
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            Id = executionId,
            Status = status,
            RowCount = totalRowCount,
            SplitCount = splitCount,
            SplitSuccessCount = successCount,
            SplitFailureCount = failureCount,
            ErrorMessage = errorMessage,
            ExecutionTimeMs = elapsedMs,
            OutputFormat = outputFormat,
            CompletedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get splits by execution ID for API/UI
    /// </summary>
    public async Task<List<ProfileExecutionSplit>> GetSplitsByExecutionIdAsync(int executionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        const string sql = @"
            SELECT * FROM ProfileExecutionSplits
            WHERE ExecutionId = @ExecutionId
            ORDER BY StartedAt";
        
        var splits = await connection.QueryAsync<ProfileExecutionSplit>(sql, new { ExecutionId = executionId });
        return splits.ToList();
    }

    /// <summary>
    /// Get formatter instance based on output format
    /// </summary>
    private IFormatter GetFormatter(string outputFormat)
    {
        return outputFormat.ToUpperInvariant() switch
        {
            "JSON" => new JsonFormatter(),
            "XML" => new XmlFormatter(),
            "CSV" => new CsvFormatter(),
            "YAML" => new YamlFormatter(),
            _ => new JsonFormatter() // Default to JSON
        };
    }

    /// <summary>
    /// Get file extension based on output format
    /// </summary>
    private string GetFileExtension(string outputFormat)
    {
        return outputFormat.ToLowerInvariant() switch
        {
            "json" => "json",
            "xml" => "xml",
            "csv" => "csv",
            "yaml" => "yaml",
            _ => "txt"
        };
    }

    /// <summary>
    /// Validate that all profile dependencies have been satisfied
    /// </summary>
    /// <param name="profileId">The profile ID being executed</param>
    /// <param name="dependsOnProfileIds">Comma-separated list of profile IDs that must be satisfied</param>
    /// <returns>True if all dependencies are satisfied, false otherwise</returns>
    private async Task<bool> ValidateProfileDependenciesAsync(int profileId, string dependsOnProfileIds)
    {
        if (string.IsNullOrEmpty(dependsOnProfileIds))
            return true;

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        
        var profileIds = dependsOnProfileIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id.Trim())
            .Where(id => int.TryParse(id, out _))
            .Select(id => int.Parse(id))
            .ToList();

        if (profileIds.Count == 0)
        {
            Log.Warning("Profile {ProfileId} has invalid DependsOnProfileIds: {DependsOnProfileIds}", 
                profileId, dependsOnProfileIds);
            return true; // Don't block execution for invalid configuration
        }

        foreach (var dependencyProfileId in profileIds)
        {
            // Get the most recent execution for the dependency profile
            var sql = @"
                SELECT Status, CompletedAt
                FROM ProfileExecution
                WHERE ProfileId = @ProfileId
                ORDER BY CompletedAt DESC
                LIMIT 1";
            
            var lastExecution = await conn.QueryFirstOrDefaultAsync<dynamic>(
                sql, new { ProfileId = dependencyProfileId });

            if (lastExecution == null)
            {
                Log.Warning("Dependency profile {DependencyProfileId} has never been executed", 
                    dependencyProfileId);
                return false;
            }

            if (lastExecution.Status != "Success")
            {
                Log.Warning("Dependency profile {DependencyProfileId} last execution status: {Status}", 
                    dependencyProfileId, lastExecution.Status);
                return false;
            }

            // Log success for each validated dependency
            Log.Debug("Dependency profile {DependencyProfileId} validated successfully (last success: {CompletedAt})", 
                dependencyProfileId, lastExecution.CompletedAt);
        }

        return true;
    }

    /// <summary>
    /// Filter out internal columns (ReefId, SplitKey) from output if configured
    /// </summary>
    private List<Dictionary<string, object>> FilterInternalColumns(
        Profile profile, 
        List<Dictionary<string, object>> rows)
    {
        if (rows.Count == 0)
        {
            return rows;
        }

        var columnsToRemove = new List<string>();

        // Check if ReefId should be excluded
        if (profile.ExcludeReefIdFromOutput && 
            profile.DeltaSyncEnabled && 
            !string.IsNullOrWhiteSpace(profile.DeltaSyncReefIdColumn))
        {
            columnsToRemove.Add(profile.DeltaSyncReefIdColumn);
            Log.Debug("Excluding ReefId column '{Column}' from output", profile.DeltaSyncReefIdColumn);
        }

        // Check if SplitKey should be excluded
        if (profile.ExcludeSplitKeyFromOutput && 
            profile.SplitEnabled && 
            !string.IsNullOrWhiteSpace(profile.SplitKeyColumn))
        {
            columnsToRemove.Add(profile.SplitKeyColumn);
            Log.Debug("Excluding SplitKey column '{Column}' from output", profile.SplitKeyColumn);
        }

        // If no columns to remove, return original rows
        if (columnsToRemove.Count == 0)
        {
            return rows;
        }

        // Create new list with filtered rows
        var filteredRows = new List<Dictionary<string, object>>(rows.Count);
        
        foreach (var row in rows)
        {
            var filteredRow = new Dictionary<string, object>(row);
            
            foreach (var columnToRemove in columnsToRemove)
            {
                // Remove case-insensitively
                var keyToRemove = filteredRow.Keys
                    .FirstOrDefault(k => string.Equals(k, columnToRemove, StringComparison.OrdinalIgnoreCase));
                
                if (keyToRemove != null)
                {
                    filteredRow.Remove(keyToRemove);
                }
            }
            
            filteredRows.Add(filteredRow);
        }

        Log.Information("Filtered {Count} internal columns from output: {Columns}",
            columnsToRemove.Count, string.Join(", ", columnsToRemove));

        return filteredRows;
    }

    /// <summary>
    /// Split rendered HTML output into individual HTML documents based on <!doctype html> delimiters
    /// </summary>
    private static List<string> SplitHtmlDocuments(string htmlOutput)
    {
        if (string.IsNullOrWhiteSpace(htmlOutput))
            return new List<string> { htmlOutput };

        var documents = htmlOutput
            .Split(new[] { "<!doctype html>" }, StringSplitOptions.None)
            .Where(doc => !string.IsNullOrWhiteSpace(doc))
            .Select(doc => "<!doctype html>" + doc)
            .ToList();

        return documents.Count > 0 ? documents : new List<string> { htmlOutput };
    }

    /// <summary>
    /// Create TOML metadata file for email test mode
    /// Creates a .toml file with email recipient and configuration data for all split HTML files
    /// </summary>
    private async Task CreateEmailMetadataFileAsync(
        Profile profile,
        List<Dictionary<string, object>> rows,
        string? lastOutputPath)
    {
        try
        {
            if (!profile.IsEmailExport || string.IsNullOrEmpty(lastOutputPath))
                return;

            var dir = Path.GetDirectoryName(lastOutputPath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(lastOutputPath);

            // Remove the _001, _002, etc suffix if present to create the metadata file name
            var baseFileName = System.Text.RegularExpressions.Regex.Replace(fileNameWithoutExt, @"_\d{3}$", "");
            var metadataPath = Path.Combine(dir ?? "", $"{baseFileName}_metadata.toml");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Email Export Metadata - Test Mode");
            sb.AppendLine($"# Generated: {DateTime.UtcNow:O}");
            sb.AppendLine($"# Profile: {profile.Name}");
            sb.AppendLine($"# Total Recipients: {rows.Count}");
            sb.AppendLine();

            foreach (var (i, row) in rows.Select((r, idx) => (idx, r)))
            {
                var email = row.TryGetValue(profile.EmailRecipientsColumn ?? "email", out var emailVal)
                    ? emailVal?.ToString()
                    : "unknown";
                var cc = string.Empty;
                if (!string.IsNullOrEmpty(profile.EmailCcColumn) &&
                    row.TryGetValue(profile.EmailCcColumn, out var ccVal))
                {
                    cc = ccVal?.ToString() ?? "";
                }
                var subject = string.Empty;
                if (!string.IsNullOrEmpty(profile.EmailSubjectColumn) &&
                    row.TryGetValue(profile.EmailSubjectColumn, out var subjectVal))
                {
                    subject = subjectVal?.ToString() ?? "";
                }

                if (i > 0) sb.AppendLine();
                sb.AppendLine($"[[recipients]]");
                sb.AppendLine($"file = \"{baseFileName}_{i + 1:D3}.html\"");
                sb.AppendLine($"email = \"{EscapeTomlString(email ?? "unknown")}\"");
                if (!string.IsNullOrEmpty(cc))
                    sb.AppendLine($"cc = \"{EscapeTomlString(cc)}\"");
                if (!string.IsNullOrEmpty(subject))
                    sb.AppendLine($"subject = \"{EscapeTomlString(subject)}\"");
            }

            await File.WriteAllTextAsync(metadataPath, sb.ToString());
            Log.Information("Created email metadata file: {MetadataPath}", metadataPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create email metadata file");
            // Don't fail the execution if metadata file creation fails
        }
    }

    /// <summary>
    /// Escape special characters in TOML strings
    /// </summary>
    private static string EscapeTomlString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";

        return input
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    /// <summary>
    /// Filters a DeltaSyncResult to only include rows at successful indices.
    /// This allows partial delta sync commits when some rows fail (e.g., failed email sends).
    /// </summary>
    /// <param name="deltaSyncResult">The original delta sync result</param>
    /// <param name="rows">The original rows list</param>
    /// <param name="successfulRowIndices">Set of row indices that succeeded</param>
    /// <param name="reefIdColumn">The column name used as ReefId for delta sync</param>
    /// <returns>A new DeltaSyncResult with only successful rows, or null if no successful rows</returns>
    private DeltaSyncResult? FilterDeltaSyncByRowIndices(
        DeltaSyncResult deltaSyncResult,
        List<Dictionary<string, object>> rows,
        HashSet<int> successfulRowIndices,
        string? reefIdColumn)
    {
        if (string.IsNullOrEmpty(reefIdColumn))
            return null;

        // Filter new rows to only include successful indices
        var filteredNewRows = deltaSyncResult.NewRows
            .Where((row, index) => successfulRowIndices.Contains(index))
            .ToList();

        // For changed rows, we need to match them back to original rows by ReefId
        var successfulReefIds = new HashSet<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (successfulRowIndices.Contains(i) && rows[i].TryGetValue(reefIdColumn, out var reefIdVal))
            {
                var reefId = reefIdVal?.ToString();
                if (!string.IsNullOrEmpty(reefId))
                {
                    successfulReefIds.Add(reefId);
                }
            }
        }

        var filteredChangedRows = deltaSyncResult.ChangedRows
            .Where(row => row.TryGetValue(reefIdColumn, out var reefIdVal) &&
                   successfulReefIds.Contains(reefIdVal?.ToString() ?? ""))
            .ToList();

        // Filter deleted ReefIds to only include deletions for successful rows
        var filteredDeletedReefIds = deltaSyncResult.DeletedReefIds
            .Where(reefId => successfulReefIds.Contains(reefId))
            .ToList();

        // Filter NewHashState to only include successful rows
        // This is CRITICAL - we must not commit hashes for failed rows to delta sync state
        var filteredNewHashState = new Dictionary<string, string>();
        foreach (var (reefId, hash) in deltaSyncResult.NewHashState)
        {
            if (successfulReefIds.Contains(reefId))
            {
                filteredNewHashState[reefId] = hash;
            }
        }

        // Check if we have any successful rows to commit
        if (filteredNewRows.Count == 0 && filteredChangedRows.Count == 0 && filteredDeletedReefIds.Count == 0)
        {
            return null;
        }

        // Create a new DeltaSyncResult with only successful rows
        return new DeltaSyncResult
        {
            NewRows = filteredNewRows,
            ChangedRows = filteredChangedRows,
            UnchangedRows = deltaSyncResult.UnchangedRows, // Keep unchanged rows as-is
            DeletedReefIds = filteredDeletedReefIds,
            TotalRowsProcessed = deltaSyncResult.TotalRowsProcessed,
            NewHashState = filteredNewHashState
        };
    }
}