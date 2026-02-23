using Microsoft.AspNetCore.Mvc;
using Reef.Core.Services;
using Serilog;
using System.Text.Json;

namespace Reef.Api;

/// <summary>
/// API endpoints for webhook triggers
/// Includes both authenticated management and public trigger endpoints
/// </summary>
public static class WebhooksEndpoints
{
    public static void Map(WebApplication app)
    {
        // Authenticated management endpoints
        var managementGroup = app.MapGroup("/api/webhooks").RequireAuthorization();
        managementGroup.MapGet("/", GetAll);
        managementGroup.MapGet("/{id:int}", GetById);
        managementGroup.MapPost("/", Create);
        managementGroup.MapDelete("/{id:int}", Delete);
        managementGroup.MapPost("/{id:int}/regenerate", RegenerateToken);
        managementGroup.MapPost("/{id:int}/enable", Enable);
        managementGroup.MapPost("/{id:int}/disable", Disable);

        // Public webhook trigger (NO AUTHENTICATION REQUIRED)
        var publicGroup = app.MapGroup("/webhooks");
        publicGroup.MapPost("/{token}", TriggerWebhook);
    }

    /// <summary>
    /// GET /api/webhooks - Get all webhook triggers
    /// </summary>
    private static async Task<IResult> GetAll(
        [FromServices] WebhookService service,
        [FromQuery] int? profileId = null,
        [FromQuery] int? jobId = null,
        [FromQuery] int? importProfileId = null)
    {
        try
        {
            List<Reef.Core.Models.WebhookTrigger> webhooks;

            if (profileId.HasValue)
            {
                webhooks = await service.GetByProfileIdAsync(profileId.Value);
            }
            else if (jobId.HasValue)
            {
                webhooks = await service.GetByJobIdAsync(jobId.Value);
            }
            else if (importProfileId.HasValue)
            {
                webhooks = await service.GetByImportProfileIdAsync(importProfileId.Value);
            }
            else
            {
                return Results.BadRequest(new { error = "profileId, jobId, or importProfileId query parameter is required" });
            }

            // Don't return actual tokens, only masked versions
            var result = webhooks.Select(w => new
            {
                w.Id,
                w.ProfileId,
                w.JobId,
                w.ImportProfileId,
                Token = $"reef_wh_...{w.Token.Substring(Math.Max(0, w.Token.Length - 8))}",
                w.IsActive,
                w.CreatedAt,
                w.LastTriggeredAt,
                w.TriggerCount
            });

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving webhooks");
            return Results.Problem("Error retrieving webhooks");
        }
    }

    /// <summary>
    /// GET /api/webhooks/{id} - Get specific webhook trigger
    /// </summary>
    private static async Task<IResult> GetById(
        int id,
        [FromServices] WebhookService service,
        [FromServices] ProfileService profileService,
        [FromServices] JobService jobService,
        [FromServices] ImportProfileService importProfileService)
    {
        try
        {
            var webhook = await service.GetByIdAsync(id);

            if (webhook == null)
            {
                return Results.NotFound();
            }

            object result;

            if (webhook.ProfileId.HasValue)
            {
                var profile = await profileService.GetByIdAsync(webhook.ProfileId.Value);
                result = new
                {
                    webhook.Id,
                    webhook.ProfileId,
                    webhook.JobId,
                    webhook.ImportProfileId,
                    ProfileName = profile?.Name,
                    Token = $"reef_wh_...{webhook.Token.Substring(Math.Max(0, webhook.Token.Length - 8))}",
                    webhook.IsActive,
                    webhook.CreatedAt,
                    webhook.LastTriggeredAt,
                    webhook.TriggerCount
                };
            }
            else if (webhook.JobId.HasValue)
            {
                var job = await jobService.GetByIdAsync(webhook.JobId.Value);
                result = new
                {
                    webhook.Id,
                    webhook.ProfileId,
                    webhook.JobId,
                    webhook.ImportProfileId,
                    JobName = job?.Name,
                    Token = $"reef_wh_...{webhook.Token.Substring(Math.Max(0, webhook.Token.Length - 8))}",
                    webhook.IsActive,
                    webhook.CreatedAt,
                    webhook.LastTriggeredAt,
                    webhook.TriggerCount
                };
            }
            else if (webhook.ImportProfileId.HasValue)
            {
                var importProfile = await importProfileService.GetByIdAsync(webhook.ImportProfileId.Value);
                result = new
                {
                    webhook.Id,
                    webhook.ProfileId,
                    webhook.JobId,
                    webhook.ImportProfileId,
                    ImportProfileName = importProfile?.Name,
                    Token = $"reef_wh_...{webhook.Token.Substring(Math.Max(0, webhook.Token.Length - 8))}",
                    webhook.IsActive,
                    webhook.CreatedAt,
                    webhook.LastTriggeredAt,
                    webhook.TriggerCount
                };
            }
            else
            {
                return Results.Problem("Invalid webhook: no ProfileId, JobId, or ImportProfileId");
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving webhook {Id}", id);
            return Results.Problem("Error retrieving webhook");
        }
    }

    /// <summary>
    /// POST /api/webhooks - Create new webhook trigger
    /// Body: { "profileId": 123 } OR { "jobId": 456 } OR { "importProfileId": 789 }
    /// </summary>
    private static async Task<IResult> Create(
        HttpContext context,
        [FromBody] CreateWebhookRequest request,
        [FromServices] WebhookService service,
        [FromServices] ProfileService profileService,
        [FromServices] JobService jobService,
        [FromServices] ImportProfileService importProfileService,
        [FromServices] AuditService auditService)
    {
        try
        {
            // Normalize: treat 0 as null
            var profileId = request.ProfileId.HasValue && request.ProfileId.Value > 0 ? request.ProfileId : null;
            var jobId = request.JobId.HasValue && request.JobId.Value > 0 ? request.JobId : null;
            var importProfileId = request.ImportProfileId.HasValue && request.ImportProfileId.Value > 0 ? request.ImportProfileId : null;

            // Validate that exactly one is provided
            var providedCount = (profileId.HasValue ? 1 : 0) + (jobId.HasValue ? 1 : 0) + (importProfileId.HasValue ? 1 : 0);
            if (providedCount != 1)
            {
                return Results.BadRequest(new { error = "Exactly one of profileId, jobId, or importProfileId must be provided" });
            }

            int webhookId;
            string token;

            if (profileId.HasValue)
            {
                var profile = await profileService.GetByIdAsync(profileId.Value);
                if (profile == null)
                    return Results.BadRequest(new { error = $"Profile {profileId.Value} not found" });

                (webhookId, token) = await service.CreateWebhookAsync(profileId.Value);
            }
            else if (importProfileId.HasValue)
            {
                var importProfile = await importProfileService.GetByIdAsync(importProfileId.Value);
                if (importProfile == null)
                    return Results.BadRequest(new { error = $"Import Profile {importProfileId.Value} not found" });

                (webhookId, token) = await service.CreateWebhookForImportProfileAsync(importProfileId.Value);
            }
            else // jobId must have value
            {
                var job = await jobService.GetByIdAsync(jobId!.Value);
                if (job == null)
                    return Results.BadRequest(new { error = $"Job {jobId.Value} not found" });

                (webhookId, token) = await service.CreateWebhookForJobAsync(jobId.Value);
            }

            // Audit log
            var username = context.User.Identity?.Name ?? "Unknown";
            var changes = JsonSerializer.Serialize(request);
            await auditService.LogAsync("WebhookTrigger", webhookId, "Created", username, changes, context);

            return Results.Ok(new
            {
                id = webhookId,
                token,
                url = $"{context.Request.Scheme}://{context.Request.Host}/webhooks/{token}",
                message = "Webhook created successfully. Save the token - it won't be shown again!"
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating webhook");
            return Results.Problem("Error creating webhook");
        }
    }

    /// <summary>
    /// DELETE /api/webhooks/{id} - Delete webhook trigger
    /// </summary>
    private static async Task<IResult> Delete(
        int id,
        HttpContext context,
        [FromServices] WebhookService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            var success = await service.DeleteAsync(id);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("WebhookTrigger", id, "Deleted", username, null, context);
                
                return Results.Ok(new { message = "Webhook deleted successfully" });
            }

            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting webhook {Id}", id);
            return Results.Problem("Error deleting webhook");
        }
    }

    /// <summary>
    /// POST /api/webhooks/{id}/regenerate - Regenerate webhook token
    /// </summary>
    private static async Task<IResult> RegenerateToken(
        int id,
        HttpContext context,
        [FromServices] WebhookService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            var token = await service.RegenerateTokenAsync(id);

            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync("WebhookTrigger", id, "TokenRegenerated", username, null, context);

            return Results.Ok(new
            {
                token,
                url = $"{context.Request.Scheme}://{context.Request.Host}/webhooks/{token}",
                message = "Token regenerated successfully. Save the token - it won't be shown again!"
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error regenerating token for webhook {Id}", id);
            return Results.Problem("Error regenerating token");
        }
    }

    /// <summary>
    /// POST /api/webhooks/{id}/enable - Enable webhook trigger
    /// </summary>
    private static async Task<IResult> Enable(
        int id,
        HttpContext context,
        [FromServices] WebhookService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            var success = await service.EnableAsync(id);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("WebhookTrigger", id, "Enabled", username, null, context);
                
                return Results.Ok(new { message = "Webhook enabled successfully" });
            }

            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enabling webhook {Id}", id);
            return Results.Problem("Error enabling webhook");
        }
    }

    /// <summary>
    /// POST /api/webhooks/{id}/disable - Disable webhook trigger
    /// </summary>
    private static async Task<IResult> Disable(
        int id,
        HttpContext context,
        [FromServices] WebhookService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            var success = await service.DisableAsync(id);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("WebhookTrigger", id, "Disabled", username, null, context);
                
                return Results.Ok(new { message = "Webhook disabled successfully" });
            }

            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disabling webhook {Id}", id);
            return Results.Problem("Error disabling webhook");
        }
    }

    /// <summary>
    /// POST /webhooks/{token} - PUBLIC endpoint to trigger profile or job execution via webhook
    /// NO AUTHENTICATION REQUIRED - token-based access
    /// Body (optional): { "parameters": { "key": "value" } }
    /// </summary>
    private static async Task<IResult> TriggerWebhook(
        string token,
        HttpContext context,
        CancellationToken ct,
        [FromBody] WebhookTriggerRequest? request,
        [FromServices] WebhookService service,
        [FromServices] ExecutionService executionService,
        [FromServices] JobExecutorService jobExecutorService,
        [FromServices] AuditService auditService)
    {
        try
        {
            // Validate token format
            if (!WebhookService.IsValidTokenFormat(token))
            {
                Log.Warning("Invalid webhook token format received for token {Token}", token);
                return Results.Json(
                    new
                    {
                        error = "invalid_token",
                        message = "Invalid webhook token format"
                    },
                    statusCode: 401 // Unauthorized
                );
            }

            // Get webhook by token
            var webhook = await service.GetByTokenAsync(token);
            if (webhook == null)
            {
                Log.Warning("Webhook token not found for token {Token}", token);
                return Results.Json(
                    new
                    {
                        error = "webhook_not_found",
                        message = "Webhook token is invalid or does not exist"
                    },
                    statusCode: 404 // Not Found
                );
            }

            // Check if webhook is active
            if (!webhook.IsActive)
            {
                Log.Warning("Inactive webhook {WebhookId} triggered for token {Token}", webhook.Id, token);
                return Results.Json(
                    new
                    {
                        error = "webhook_disabled",
                        message = "This webhook is disabled",
                        webhookId = webhook.Id
                    },
                    statusCode: 403 // Forbidden
                );
            }

            // Check rate limit (100 requests per hour by default)
            // To enable "only once per period" mode, call: CheckRateLimit(webhook.Id, 1, 24) for once per 24 hours
            if (!service.CheckRateLimit(webhook.Id))
            {
                var (count, oldest) = service.GetRateLimitInfo(webhook.Id);
                Log.Warning("Rate limit exceeded for webhook {WebhookId} and token {Token}", webhook.Id, token);

                var resetTime = oldest.HasValue ? oldest.Value.AddHours(1) : DateTime.UtcNow.AddHours(1);
                var retryAfterSeconds = (int)Math.Max(1, (resetTime - DateTime.UtcNow).TotalSeconds);

                return Results.Json(
                    new
                    {
                        error = "rate_limit_exceeded",
                        message = "Webhook rate limit exceeded",
                        details = new
                        {
                            webhookId = webhook.Id,
                            requestsInWindow = count,
                            maxRequestsPerHour = 100,
                            windowHours = 1,
                            oldestRequestAt = oldest?.ToString("o"),
                            retryAfterSeconds = retryAfterSeconds,
                            resetAt = resetTime.ToString("o")
                        }
                    },
                    statusCode: 429 // Too Many Requests
                );
            }

            // Parse parameters from request body
            var parameters = request?.Parameters ?? new Dictionary<string, string>();

            object result;
            
            if (webhook.ProfileId.HasValue)
            {
                // Execute profile
                Log.Information("Triggering profile {ProfileId} via webhook {WebhookId} and token {Token}", 
                    webhook.ProfileId.Value, webhook.Id, token);

                var (executionId, success, outputPath, errorMessage) = await executionService.ExecuteProfileAsync(
                    webhook.ProfileId.Value,
                    parameters,
                    $"Webhook/{webhook.Id}"
                );

                // Update webhook last triggered
                await service.UpdateLastTriggeredAsync(webhook.Id);

                // Audit log
                var profileChanges = JsonSerializer.Serialize(new { parameters, executionId });
                await auditService.LogAsync("WebhookTrigger", webhook.Id, "Triggered", "Webhook", profileChanges, context);

                // Return execution status immediately (don't wait for completion)
                result = new
                {
                    executionId,
                    status = success ? "Started" : "Failed",
                    message = success 
                        ? $"Profile execution started with ID {executionId}" 
                        : $"Profile execution failed: {errorMessage}",
                    errorMessage = success ? null : errorMessage
                };
            }
            else if (webhook.JobId.HasValue)
            {
                // Trigger job
                Log.Information("Triggering job {JobId} via webhook {WebhookId} and token {Token}",
                    webhook.JobId.Value, webhook.Id, token);

                var jobService = context.RequestServices.GetRequiredService<JobService>();
                var job = await jobService.GetByIdAsync(webhook.JobId.Value);

                if (job == null)
                {
                    Log.Error("Job {JobId} not found for webhook {WebhookId}", webhook.JobId.Value, webhook.Id);
                    return Results.Json(
                        new
                        {
                            error = "job_not_found",
                            message = "Job not found or deleted",
                            webhookId = webhook.Id,
                            jobId = webhook.JobId.Value
                        },
                        statusCode: 404 // Not Found
                    );
                }

                // Convert string parameters to object parameters for job executor
                var jobParameters = parameters?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
                var executionId = await jobExecutorService.TriggerJobAsync(job, false, jobParameters);

                // Update webhook last triggered
                await service.UpdateLastTriggeredAsync(webhook.Id);

                // Audit log
                var jobChanges = JsonSerializer.Serialize(new { parameters, executionId });
                await auditService.LogAsync("WebhookTrigger", webhook.Id, "Triggered", "Webhook", jobChanges, context);

                // Return execution status
                result = new
                {
                    executionId,
                    status = "Started",
                    message = $"Job execution started with ID {executionId}"
                };
            }
            else if (webhook.ImportProfileId.HasValue)
            {
                // Execute import profile
                Log.Information("Triggering import profile {ImportProfileId} via webhook {WebhookId} and token {Token}",
                    webhook.ImportProfileId.Value, webhook.Id, token);

                var importExecService = context.RequestServices.GetRequiredService<ImportExecutionService>();
                var exec = await importExecService.ExecuteAsync(webhook.ImportProfileId.Value, $"Webhook/{webhook.Id}", ct);

                // Update webhook last triggered
                await service.UpdateLastTriggeredAsync(webhook.Id);

                // Audit log
                var importChanges = JsonSerializer.Serialize(new { executionId = exec.Id });
                await auditService.LogAsync("WebhookTrigger", webhook.Id, "Triggered", "Webhook", importChanges, context);

                result = new
                {
                    executionId = exec.Id,
                    status = exec.Status,
                    message = $"Import profile execution started with ID {exec.Id}"
                };
            }
            else
            {
                Log.Error("Webhook {WebhookId} has neither ProfileId, JobId, nor ImportProfileId", webhook.Id);
                return Results.Json(
                    new
                    {
                        error = "invalid_webhook_configuration",
                        message = "Webhook is misconfigured",
                        webhookId = webhook.Id
                    },
                    statusCode: 500 // Internal Server Error
                );
            }

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing webhook trigger for token {Token}", token);
            return Results.Json(
                new
                {
                    error = "webhook_trigger_error",
                    message = "An error occurred while processing the webhook trigger"
                },
                statusCode: 500 // Internal Server Error
            );
        }
    }
}

/// <summary>
/// Request model for creating a webhook
/// </summary>
public record CreateWebhookRequest
{
    public int? ProfileId { get; init; }
    public int? JobId { get; init; }
    public int? ImportProfileId { get; init; }
}

/// <summary>
/// Request model for webhook trigger with optional parameters
/// </summary>
public record WebhookTriggerRequest
{
    public Dictionary<string, string>? Parameters { get; init; }
}