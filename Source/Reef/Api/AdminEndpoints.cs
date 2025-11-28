// Source/Reef/Api/AdminEndpoints.cs
// REST API endpoints for administration (Admin role only)

using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.Destinations;
using Serilog;
using System.Security.Claims;

namespace Reef.Api;

/// <summary>
/// API endpoints for system administration
/// All endpoints require Admin role authorization
/// </summary>
public static class AdminEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization();

        group.MapGet("/metrics", GetMetrics);
        group.MapGet("/audit-logs", GetAuditLogs);
        group.MapGet("/users", GetUsers);
        group.MapPost("/users", CreateUser);
        group.MapPut("/users/{id:int}", UpdateUser);
        group.MapDelete("/users/{id:int}", DeleteUser);
        group.MapGet("/api-keys", GetApiKeys);
        group.MapPost("/api-keys", CreateApiKey);
        group.MapDelete("/api-keys/{id:int}", RevokeApiKey);
        group.MapGet("/notifications", GetNotificationSettings);
        group.MapPost("/notifications", UpdateNotificationSettings);
        group.MapPost("/notifications/test", TestNotificationSettings);
        group.MapGet("/notification-templates", GetAllNotificationTemplates);
        group.MapGet("/notification-templates/{templateType}", GetNotificationTemplate);
        group.MapPost("/notification-templates", CreateNotificationTemplate);
        group.MapPut("/notification-templates/{templateType}", UpdateNotificationTemplate);
        group.MapDelete("/notification-templates/{templateType}", DeleteNotificationTemplate);
        group.MapPost("/notification-templates/{templateType}/reset", ResetNotificationTemplate);
        group.MapGet("/logs", (Delegate)GetLogs);
        group.MapGet("/version", GetVersion);
        group.MapGet("/danger/confirmation-string", GetConfirmationString);
        group.MapPost("/danger/reset-database", ResetDatabase);
        group.MapPost("/danger/clear-old-audits", ClearOldAudits);
        group.MapPost("/danger/clear-old-executions", ClearOldExecutions);
        group.MapPost("/danger/clear-all-executions", ClearAllExecutions);
    }

    /// <summary>
    /// GET /api/admin/logs - Get application logs with pagination
    /// Retrieves logs from the latest Serilog output file with pagination support.
    /// Query parameters: page, pageSize, minimumLevel
    /// </summary>
    private static async Task<IResult> GetLogs(
        HttpContext context, 
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? minimumLevel = null)
    {
        if (!IsAdmin(context))
        {
            Log.Warning("Non-admin user {Username} attempted to access application logs",
                context.User.Identity?.Name ?? "Unknown");
            return Results.Forbid();
        }

        try
        {
            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 500) pageSize = 100;

            // Get the path to the log directory configured in Program.cs
            string logDirectory;
            
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") 
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") 
                ?? "Production";
            
            if (environment == "Development")
            {
                var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
                logDirectory = Path.Combine(projectDir, "log");

                Log.Debug("GetLogs: Development environment detected, using log directory: {LogDirectory}", logDirectory);
            }
            else
            {
                logDirectory = Path.Combine(AppContext.BaseDirectory, "log");
            }

            Log.Debug("GetLogs: Checking log directory: {LogDirectory}", logDirectory);

            if (!Directory.Exists(logDirectory))
            {
                Log.Warning("GetLogs: Log directory does not exist: {LogDirectory}", logDirectory);
                return Results.Ok(new { logs = Array.Empty<object>() });
            }

            // Find the most recently modified file matching the Serilog rolling log pattern
            var logFiles = Directory.GetFiles(logDirectory, "reef-*.log");
            Log.Debug("GetLogs: Found {Count} log files", logFiles.Length);
            
            var latestLogFile = logFiles
                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                .FirstOrDefault();

            if (latestLogFile == null)
            {
                Log.Warning("GetLogs: No log files found matching pattern 'reef-*.log'");
                return Results.Ok(new { logs = Array.Empty<object>() });
            }

            Log.Debug("GetLogs: Reading log file: {LogFile}", latestLogFile);

            // Read the entire file content. Use FileShare.ReadWrite to allow reading 
            // a file that Serilog might be actively writing to.
            string content;
            using (var stream = new FileStream(latestLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                content = await reader.ReadToEndAsync();
            }

            // Split content into lines and parse
            var allLines = content
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            // Parse each log line into an object with Timestamp, Level, and Message
            var parsedLogs = allLines.Select(line =>
            {
                // Example: 2025-10-31 20:27:02 [INF] Message
                var firstBracket = line.IndexOf('[');
                var lastBracket = line.IndexOf(']', firstBracket + 1);
                string? timestamp = null;
                string? level = null;
                string? message = null;
                if (firstBracket > 0 && lastBracket > firstBracket)
                {
                    timestamp = line.Substring(0, firstBracket).Trim();
                    level = line.Substring(firstBracket + 1, lastBracket - firstBracket - 1).Trim();
                    message = line.Substring(lastBracket + 1).Trim();
                }
                else
                {
                    // Fallback: treat whole line as message
                    message = line;
                }
                return new {
                    Timestamp = timestamp,
                    Level = level,
                    Message = message
                };
            }).Reverse().ToList(); // Reverse to show newest first

            // Filter by minimum level if specified
            if (!string.IsNullOrWhiteSpace(minimumLevel))
            {
                parsedLogs = parsedLogs.Where(log => 
                    log.Level != null && log.Level.Equals(minimumLevel, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Calculate pagination
            var totalCount = parsedLogs.Count;
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var skip = (page - 1) * pageSize;
            
            var pagedLogs = parsedLogs
                .Skip(skip)
                .Take(pageSize)
                .ToArray();

            Log.Debug("GetLogs: Found {Count} log lines, returning page {Page} with {PageSize} items", 
                totalCount, page, pagedLogs.Length);

            return Results.Ok(new { 
                data = pagedLogs,
                page,
                pageSize,
                totalCount,
                totalPages
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching log data");
            return Results.Problem("Error fetching log data");
        }
    }

    /// <summary>
    /// GET /api/admin/notifications - Get system notification settings
    /// </summary>
    private static async Task<IResult> GetNotificationSettings(
        HttpContext context,
        [FromServices] AdminService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access notification settings",
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var settings = await service.GetNotificationSettingsAsync();
            return Results.Ok(settings);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving notification settings");
            return Results.Problem("Error retrieving notification settings");
        }
    }

    /// <summary>
    /// POST /api/admin/notifications - Update system notification settings
    /// Body: { "isEnabled": true, "destinationId": 1, "recipientEmails": "admin@example.com", ... }
    /// </summary>
    private static async Task<IResult> UpdateNotificationSettings(
        HttpContext context,
        [FromBody] NotificationSettings request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to update notification settings",
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var success = await service.UpdateNotificationSettingsAsync(request);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                var changes = System.Text.Json.JsonSerializer.Serialize(request);
                await auditService.LogAsync("NotificationSettings", request.Id, "Updated", username, changes, context);

                return Results.Ok(new { message = "Notification settings updated successfully" });
            }

            return Results.Problem("Failed to update notification settings");
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid notification settings update request");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating notification settings");
            return Results.Problem("Error updating notification settings");
        }
    }

    /// <summary>
    /// POST /api/admin/notifications/test - Send a test notification
    /// Body: { "destinationId": 1, "recipientEmails": "test@example.com" }
    /// </summary>
    private static async Task<IResult> TestNotificationSettings(
        HttpContext context,
        [FromBody] TestNotificationRequest request,
        [FromServices] NotificationService service,
        [FromServices] DestinationService destinationService,
        [FromServices] EncryptionService encryptionService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to send test notification",
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            if (request.DestinationId <= 0)
            {
                return Results.BadRequest(new { error = "Invalid destination ID" });
            }

            // Get the destination to verify it exists
            var destination = await destinationService.GetByIdAsync(request.DestinationId);
            if (destination == null)
            {
                return Results.BadRequest(new { error = "Destination not found" });
            }

            // Create a test notification settings object
            var testSettings = new NotificationSettings
            {
                Id = 0,
                IsEnabled = true,
                DestinationId = request.DestinationId,
                RecipientEmails = request.RecipientEmails,
                NotifyOnProfileFailure = false,
                NotifyOnProfileSuccess = false,
                NotifyOnJobFailure = false,
                NotifyOnJobSuccess = false,
                NotifyOnDatabaseSizeThreshold = false,
                NotifyOnNewApiKey = false,
                NotifyOnNewUser = false,
                NotifyOnNewWebhook = false,
                DatabaseSizeThresholdBytes = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Hash = ""
            };

            // Send a test notification (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    var subject = "[Reef] Test Notification";
                    var body = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #06b6d4; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #06b6d4; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
        .success-box {{ background-color: #d1fae5; border-left: 5px solid #10b981; padding: 15px; margin: 20px 0; border-radius: 4px; }}
        .success-box p {{ color: #065f46; margin: 0; }}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>✓ Test Notification Successful</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>System:</span>
                        <span class='value'>Reef Notification System</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Sent At:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Test Type:</span>
                        <span class='value'>Email Destination Verification</span>
                    </div>
                </div>
                <div class='success-box'>
                    <p>✓ Email configuration is working correctly! Your notification system is ready to receive alerts.</p>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";

                    // Decrypt destination config and send
                    var decryptedConfig = destination.ConfigurationJson;

                    // Check if configuration is encrypted before attempting decryption
                    if (encryptionService.IsEncrypted(decryptedConfig))
                    {
                        decryptedConfig = encryptionService.Decrypt(decryptedConfig);
                    }

                    var emailConfig = System.Text.Json.JsonSerializer.Deserialize<EmailDestinationConfiguration>(decryptedConfig);
                    if (emailConfig == null) throw new Exception("Failed to deserialize email configuration");

                    // Override recipients if specified
                    if (!string.IsNullOrWhiteSpace(request.RecipientEmails))
                    {
                        emailConfig.ToAddresses = request.RecipientEmails
                            .Split(',')
                            .Select(e => e.Trim())
                            .Where(e => !string.IsNullOrWhiteSpace(e))
                            .ToList();
                    }

                    // Construct message
                    var message = new MimeKit.MimeMessage();
                    message.From.Add(new MimeKit.MailboxAddress(emailConfig.FromName ?? "Reef", emailConfig.FromAddress));
                    foreach (var recipient in emailConfig.ToAddresses)
                    {
                        if (!string.IsNullOrWhiteSpace(recipient))
                        {
                            try { message.To.Add(MimeKit.MailboxAddress.Parse(recipient.Trim())); }
                            catch { }
                        }
                    }
                    message.Subject = subject;
                    var bodyBuilder = new MimeKit.BodyBuilder { HtmlBody = body };
                    message.Body = bodyBuilder.ToMessageBody();

                    // Send via appropriate provider
                    IEmailProvider emailProvider = (emailConfig.EmailProvider?.ToLower()) switch
                    {
                        "resend" => new ResendEmailProvider(),
                        "sendgrid" => new SendGridEmailProvider(),
                        _ => new SmtpEmailProvider()
                    };

                    var result = await emailProvider.SendEmailAsync(message, emailConfig);
                    if (result.Success)
                    {
                        Log.Information("Test notification sent successfully to {RecipientCount} recipients",
                            emailConfig.ToAddresses.Count);
                    }
                    else
                    {
                        Log.Warning("Test notification failed: {Error}", result.ErrorMessage);
                    }
                }
                catch (Exception testEx)
                {
                    Log.Error(testEx, "Error sending test notification");
                }
            });

            return Results.Ok(new { message = "Test notification queued for sending. Check your email." });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending test notification");
            return Results.Problem("Error sending test notification: " + ex.Message);
        }
    }

    /// <summary>
    /// GET /api/admin/notification-templates - Get all email templates
    /// </summary>
    private static async Task<IResult> GetAllNotificationTemplates(
        HttpContext context,
        [FromServices] NotificationTemplateService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access notification templates",
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var templates = await service.GetAllAsync();
            return Results.Ok(new { data = templates });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving notification templates");
            return Results.Problem("Error retrieving notification templates");
        }
    }

    /// <summary>
    /// GET /api/admin/notification-templates/{templateType} - Get template by type
    /// </summary>
    private static async Task<IResult> GetNotificationTemplate(
        HttpContext context,
        string templateType,
        [FromServices] NotificationTemplateService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access notification template {Type}",
                    context.User.Identity?.Name ?? "Unknown", templateType);
                return Results.Forbid();
            }

            var template = await service.GetByTypeAsync(templateType);
            if (template == null)
            {
                return Results.NotFound(new { error = $"Template '{templateType}' not found" });
            }

            return Results.Ok(template);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving notification template {Type}", templateType);
            return Results.Problem("Error retrieving notification template");
        }
    }

    /// <summary>
    /// POST /api/admin/notification-templates - Create a new email template
    /// </summary>
    private static async Task<IResult> CreateNotificationTemplate(
        HttpContext context,
        [FromBody] NotificationEmailTemplate template,
        [FromServices] NotificationTemplateService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to create notification template",
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(template.TemplateType))
            {
                return Results.BadRequest(new { error = "TemplateType is required" });
            }

            if (string.IsNullOrWhiteSpace(template.Subject))
            {
                return Results.BadRequest(new { error = "Subject is required" });
            }

            if (string.IsNullOrWhiteSpace(template.HtmlBody))
            {
                return Results.BadRequest(new { error = "HtmlBody is required" });
            }

            template.CreatedAt = DateTime.UtcNow;
            template.UpdatedAt = DateTime.UtcNow;

            var id = await service.CreateAsync(template);
            Log.Information("Created notification template {Type} (ID: {Id})", template.TemplateType, id);

            return Results.Created($"/api/admin/notification-templates/{template.TemplateType}",
                new { id, templateType = template.TemplateType });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating notification template");
            return Results.Problem("Error creating notification template: " + ex.Message);
        }
    }

    /// <summary>
    /// PUT /api/admin/notification-templates/{templateType} - Update an email template
    /// </summary>
    private static async Task<IResult> UpdateNotificationTemplate(
        HttpContext context,
        string templateType,
        [FromBody] NotificationEmailTemplate template,
        [FromServices] NotificationTemplateService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to update notification template {Type}",
                    context.User.Identity?.Name ?? "Unknown", templateType);
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(template.Subject))
            {
                return Results.BadRequest(new { error = "Subject is required" });
            }

            if (string.IsNullOrWhiteSpace(template.HtmlBody))
            {
                return Results.BadRequest(new { error = "HtmlBody is required" });
            }

            template.TemplateType = templateType;
            template.UpdatedAt = DateTime.UtcNow;

            var success = await service.UpdateAsync(template);
            if (!success)
            {
                return Results.NotFound(new { error = $"Template '{templateType}' not found" });
            }

            Log.Information("Updated notification template {Type}", templateType);
            return Results.Ok(new { message = "Template updated successfully" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating notification template {Type}", templateType);
            return Results.Problem("Error updating notification template: " + ex.Message);
        }
    }

    /// <summary>
    /// DELETE /api/admin/notification-templates/{templateType} - Delete an email template
    /// </summary>
    private static async Task<IResult> DeleteNotificationTemplate(
        HttpContext context,
        string templateType,
        [FromServices] NotificationTemplateService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to delete notification template {Type}",
                    context.User.Identity?.Name ?? "Unknown", templateType);
                return Results.Forbid();
            }

            var success = await service.DeleteAsync(templateType);
            if (!success)
            {
                return Results.NotFound(new { error = $"Template '{templateType}' not found" });
            }

            Log.Information("Deleted notification template {Type}", templateType);
            return Results.Ok(new { message = "Template deleted successfully" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting notification template {Type}", templateType);
            return Results.Problem("Error deleting notification template: " + ex.Message);
        }
    }

    /// <summary>
    /// POST /api/admin/notification-templates/{templateType}/reset - Reset template to default
    /// </summary>
    private static async Task<IResult> ResetNotificationTemplate(
        HttpContext context,
        string templateType,
        [FromServices] NotificationTemplateService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to reset notification template {Type}",
                    context.User.Identity?.Name ?? "Unknown", templateType);
                return Results.Forbid();
            }

            var success = await service.ResetToDefaultAsync(templateType);
            if (!success)
            {
                return Results.NotFound(new { error = $"No default template found for type '{templateType}'" });
            }

            Log.Information("Reset notification template {Type} to default", templateType);
            return Results.Ok(new { message = "Template reset to default successfully" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting notification template {Type}", templateType);
            return Results.Problem("Error resetting notification template: " + ex.Message);
        }
    }

    /// <summary>
    /// Check if user has Admin role
    /// </summary>
    private static bool IsAdmin(HttpContext context)
    {
        var role = context.User.Claims
            .FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type == "role")?.Value;
        return role == "Admin";
    }

    /// <summary>
    /// GET /api/admin/metrics - Get system metrics
    /// </summary>
    private static async Task<IResult> GetMetrics(
        HttpContext context,
        [FromServices] AdminService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access metrics", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var metrics = await service.GetSystemMetricsAsync();
            return Results.Ok(metrics);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving system metrics");
            return Results.Problem("Error retrieving system metrics");
        }
    }

    /// <summary>
    /// GET /api/admin/audit-logs - Get audit logs with pagination
    /// Query parameters: page, pageSize, entityType, action, username, startDate, endDate
    /// </summary>
    private static async Task<IResult> GetAuditLogs(
        HttpContext context,
        [FromServices] AdminService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null,
        [FromQuery] string? username = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access audit logs", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 200)
            {
                return Results.BadRequest(new { error = "Page size must be between 1 and 200" });
            }

            var (logs, totalCount) = await service.GetAuditLogsAsync(
                page, pageSize, entityType, action, username, startDate, endDate);
            
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            
            return Results.Ok(new
            {
                data = logs,
                page,
                pageSize,
                totalCount,
                totalPages
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving audit logs");
            return Results.Problem("Error retrieving audit logs");
        }
    }

    /// <summary>
    /// GET /api/admin/users - Get all users
    /// </summary>
    private static async Task<IResult> GetUsers(
        HttpContext context,
        [FromServices] AdminService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access users", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var users = await service.GetUsersAsync();
            
            // Don't return password hashes
            var sanitizedUsers = users.Select(u => new
            {
                u.Id,
                u.Username,
                u.Role,
                u.IsActive,
                u.CreatedAt,
                u.LastLoginAt
            });

            return Results.Ok(sanitizedUsers);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving users");
            return Results.Problem("Error retrieving users");
        }
    }

    /// <summary>
    /// POST /api/admin/users - Create new user
    /// Body: { "username": "newuser", "password": "password123", "role": "User" }
    /// </summary>
    private static async Task<IResult> CreateUser(
        HttpContext context,
        [FromBody] CreateUserRequest request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to create user", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(request.Username))
            {
                return Results.BadRequest(new { error = "Username is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Password is required" });
            }

            if (request.Role != "Admin" && request.Role != "User")
            {
                return Results.BadRequest(new { error = "Role must be 'Admin' or 'User'" });
            }

            var createdBy = context.User.Identity?.Name ?? "Unknown";
            var userId = await service.CreateUserAsync(
                request.Username,
                request.Password,
                request.Role);

            // Audit log
            var changes = System.Text.Json.JsonSerializer.Serialize(new { request.Username, request.Role });
            await auditService.LogAsync("User", userId, "Created", createdBy, changes, context);

            return Results.Ok(new { id = userId, message = "User created successfully" });
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid user creation request");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating user");
            return Results.Problem("Error creating user");
        }
    }

    /// <summary>
    /// PUT /api/admin/users/{id} - Update user
    /// Body: { "role": "Admin", "isActive": true }
    /// </summary>
    private static async Task<IResult> UpdateUser(
        int id,
        HttpContext context,
        [FromBody] UpdateUserRequest request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to update user", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            // Validate role
            if (request.Role != "Admin" && request.Role != "User")
            {
                return Results.BadRequest(new { error = "Role must be 'Admin' or 'User'" });
            }

            // Get existing user
            var users = await service.GetUsersAsync();
            var existingUser = users.FirstOrDefault(u => u.Id == id);
            if (existingUser == null)
            {
                return Results.NotFound();
            }

            // Get current admin's username
            var currentUsername = context.User.Identity?.Name;
            var currentUser = users.FirstOrDefault(u => u.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase));

            // Admins cannot modify other admin accounts
            if (existingUser.Role == "Admin" && currentUser?.Id != existingUser.Id)
            {
                return Results.BadRequest(new { error = "Admins cannot modify other admin accounts" });
            }

            // Prevent changing password for other admin accounts (but allow admins to change their own)
            if (existingUser.Role == "Admin" && !string.IsNullOrWhiteSpace(request.Password) && currentUser?.Id != existingUser.Id)
            {
                return Results.BadRequest(new { error = "Cannot change password for other admin accounts" });
            }

            // Update user
            existingUser.Role = request.Role;
            existingUser.IsActive = request.IsActive;

            var success = await service.UpdateUserAsync(existingUser, request.Password);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                var changes = System.Text.Json.JsonSerializer.Serialize(new { 
                    request.Role, 
                    request.IsActive, 
                    PasswordChanged = !string.IsNullOrWhiteSpace(request.Password) 
                });
                await auditService.LogAsync("User", id, "Updated", username, changes, context);
                
                return Results.Ok(new { message = "User updated successfully" });
            }

            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid user update request");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating user {Id}", id);
            return Results.Problem("Error updating user");
        }
    }

    /// <summary>
    /// DELETE /api/admin/users/{id} - Delete user
    /// </summary>
    private static async Task<IResult> DeleteUser(
        int id,
        HttpContext context,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to delete user", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            // Prevent self-deletion and deletion of other admins
            var currentUsername = context.User.Identity?.Name;
            var users = await service.GetUsersAsync();
            var userToDelete = users.FirstOrDefault(u => u.Id == id);

            if (userToDelete?.Username == currentUsername)
            {
                return Results.BadRequest(new { error = "Cannot delete your own user account" });
            }

            // Admins cannot delete other admin accounts
            if (userToDelete?.Role == "Admin")
            {
                return Results.BadRequest(new { error = "Admins cannot delete other admin accounts" });
            }

            var success = await service.DeleteUserAsync(id);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("User", id, "Deleted", username, null, context);
                
                return Results.Ok(new { message = "User deleted successfully" });
            }

            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid user deletion attempt");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting user {Id}", id);
            return Results.Problem("Error deleting user");
        }
    }

    /// <summary>
    /// GET /api/admin/api-keys - Get all API keys
    /// </summary>
    private static async Task<IResult> GetApiKeys(
        HttpContext context,
        [FromServices] AdminService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access API keys", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var keys = await service.GetApiKeysAsync();
            
            // Don't return key hashes
            var sanitizedKeys = keys.Select(k => new
            {
                k.Id,
                k.Name,
                k.Permissions,
                k.ExpiresAt,
                k.IsActive,
                k.CreatedAt,
                k.LastUsedAt
            });

            return Results.Ok(sanitizedKeys);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving API keys");
            return Results.Problem("Error retrieving API keys");
        }
    }

    /// <summary>
    /// POST /api/admin/api-keys - Create new API key
    /// Body: { "name": "Integration Key", "permissions": "{...}", "expiresAt": "2025-12-31" }
    /// </summary>
    private static async Task<IResult> CreateApiKey(
        HttpContext context,
        [FromBody] CreateApiKeyRequest request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to create API key", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            // Validate input
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest(new { error = "API key name is required" });
            }

            var createdBy = context.User.Identity?.Name ?? "Unknown";
            var (keyId, apiKeyValue) = await service.CreateApiKeyAsync(
                request.Name,
                request.Permissions ?? "{}",
                request.ExpiresAt);

            // Audit log
            var changes = System.Text.Json.JsonSerializer.Serialize(request);
            await auditService.LogAsync("ApiKey", keyId, "Created", createdBy, changes, context);

            return Results.Ok(new
            {
                id = keyId,
                apiKey = apiKeyValue,
                message = "API key created successfully. Save the key - it won't be shown again!"
            });
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Invalid API key creation request");
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating API key");
            return Results.Problem("Error creating API key");
        }
    }

    /// <summary>
    /// DELETE /api/admin/api-keys/{id} - Revoke API key
    /// </summary>
    private static async Task<IResult> RevokeApiKey(
        int id,
        HttpContext context,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to revoke API key", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var success = await service.RevokeApiKeyAsync(id);

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("ApiKey", id, "Revoked", username, null, context);
                
                return Results.Ok(new { message = "API key revoked successfully" });
            }

            return Results.NotFound();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error revoking API key {Id}", id);
            return Results.Problem("Error revoking API key");
        }
    }

    /// <summary>
    /// GET /api/admin/version - Get current application version
    /// </summary>
    private static IResult GetVersion(HttpContext context)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to access version info", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "Unknown";
            
            // Extract the informational version if available (this includes the semantic version from .csproj)
            var informationalVersion = assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;

            var semanticVersion = informationalVersion?.InformationalVersion ?? version;

            return Results.Ok(new
            {
                version = semanticVersion,
                timestamp = DateTime.UtcNow,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") 
                    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") 
                    ?? "Production"
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving version information");
            return Results.Problem("Error retrieving version information");
        }
    }

    /// <summary>
    /// GET /api/admin/danger/confirmation-string - Generate random confirmation string
    /// </summary>
    private static IResult GetConfirmationString(
        HttpContext context,
        [FromServices] AdminService service)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to get confirmation string", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            var confirmationString = service.GenerateConfirmationString();
            return Results.Ok(new { confirmationString });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generating confirmation string");
            return Results.Problem("Error generating confirmation string");
        }
    }

    /// <summary>
    /// POST /api/admin/danger/reset-database - Reset database completely
    /// Body: { "confirmationString": "word_word_word" }
    /// </summary>
    private static async Task<IResult> ResetDatabase(
        HttpContext context,
        [FromBody] DangerConfirmationRequest request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to reset database", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.ConfirmationString))
            {
                return Results.BadRequest(new { error = "Confirmation string is required" });
            }

            // Note: We can't verify the confirmation string here since it's generated client-side
            // The client must verify it before sending the request

            var success = await service.ResetDatabaseAsync();

            if (success)
            {
                var username = context.User.Identity?.Name ?? "Unknown";
                await auditService.LogAsync("Database", 0, "Reset", username, 
                    "Complete database reset performed", context);
                
                Log.Warning("DANGER: Database reset performed by {Username}", username);
                return Results.Ok(new { message = "Database reset successfully" });
            }

            return Results.Problem("Failed to reset database");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting database");
            return Results.Problem("Error resetting database: " + ex.Message);
        }
    }

    /// <summary>
    /// POST /api/admin/danger/clear-old-audits - Clear audit logs older than N days
    /// Body: { "retentionDays": 90 }
    /// </summary>
    private static async Task<IResult> ClearOldAudits(
        HttpContext context,
        [FromBody] RetentionRequest request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to clear audit logs", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            if (request.RetentionDays < 90)
            {
                return Results.BadRequest(new { error = "Minimum retention period is 90 days" });
            }

            var deletedCount = await service.ClearOldAuditLogsAsync(request.RetentionDays);

            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync("AuditLog", 0, "Cleared", username, 
                $"Deleted {deletedCount} audit logs older than {request.RetentionDays} days", context);

            return Results.Ok(new { 
                message = $"Deleted {deletedCount} audit log entries", 
                deletedCount 
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing old audit logs");
            return Results.Problem("Error clearing old audit logs");
        }
    }

    /// <summary>
    /// POST /api/admin/danger/clear-old-executions - Clear execution logs older than N days
    /// Body: { "retentionDays": 30 }
    /// </summary>
    private static async Task<IResult> ClearOldExecutions(
        HttpContext context,
        [FromBody] RetentionRequest request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to clear execution logs", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            if (request.RetentionDays < 1)
            {
                return Results.BadRequest(new { error = "Retention days must be at least 1" });
            }

            var deletedCount = await service.ClearOldExecutionLogsAsync(request.RetentionDays);

            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync("ProfileExecutions", 0, "Cleared", username, 
                $"Deleted {deletedCount} execution logs older than {request.RetentionDays} days", context);

            return Results.Ok(new { 
                message = $"Deleted {deletedCount} execution log entries", 
                deletedCount 
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing old execution logs");
            return Results.Problem("Error clearing old execution logs");
        }
    }

    /// <summary>
    /// POST /api/admin/danger/clear-all-executions - Clear ALL execution logs
    /// Body: { "confirmationString": "word_word_word" }
    /// </summary>
    private static async Task<IResult> ClearAllExecutions(
        HttpContext context,
        [FromBody] DangerConfirmationRequest request,
        [FromServices] AdminService service,
        [FromServices] AuditService auditService)
    {
        try
        {
            if (!IsAdmin(context))
            {
                Log.Warning("Non-admin user {User} attempted to clear all execution logs", 
                    context.User.Identity?.Name ?? "Unknown");
                return Results.Forbid();
            }

            if (string.IsNullOrWhiteSpace(request.ConfirmationString))
            {
                return Results.BadRequest(new { error = "Confirmation string is required" });
            }

            var deletedCount = await service.ClearAllExecutionLogsAsync();

            var username = context.User.Identity?.Name ?? "Unknown";
            await auditService.LogAsync("ProfileExecutions", 0, "ClearedAll", username, 
                $"Deleted ALL execution logs ({deletedCount} entries)", context);

            Log.Warning("DANGER: All execution logs cleared by {Username}", username);
            return Results.Ok(new { 
                message = $"Deleted all execution log entries", 
                deletedCount 
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing all execution logs");
            return Results.Problem("Error clearing all execution logs");
        }
    }
}

/// <summary>
/// Request model for creating a user
/// </summary>
public record CreateUserRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
    public string Role { get; init; } = "User";
}

/// <summary>
/// Request model for updating a user
/// </summary>
public record UpdateUserRequest
{
    public required string Role { get; init; }
    public bool IsActive { get; init; }
    public string? Password { get; init; }
}

/// <summary>
/// Request model for creating an API key
/// </summary>
public record CreateApiKeyRequest
{
    public required string Name { get; init; }
    public string? Permissions { get; init; }
    public DateTime? ExpiresAt { get; init; }
}

/// <summary>
/// Request model for dangerous operations requiring confirmation
/// </summary>
public record DangerConfirmationRequest
{
    public required string ConfirmationString { get; init; }
}

/// <summary>
/// Request model for retention-based cleanup operations
/// </summary>
public record RetentionRequest
{
    public required int RetentionDays { get; init; }
}