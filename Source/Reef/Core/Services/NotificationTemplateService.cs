using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing email templates stored in database
/// Supports CRUD operations, seeding defaults, and template retrieval
/// </summary>
public class NotificationTemplateService
{
    private readonly string _connectionString;

    public NotificationTemplateService(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Get all email templates
    /// </summary>
    public async Task<List<NotificationEmailTemplate>> GetAllAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT Id, TemplateType, Subject, HtmlBody, IsDefault, CreatedAt, UpdatedAt
                FROM NotificationEmailTemplate
                ORDER BY TemplateType";

            var templates = (await connection.QueryAsync<NotificationEmailTemplate>(sql)).ToList();
            return templates;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving all email templates");
            return new List<NotificationEmailTemplate>();
        }
    }

    /// <summary>
    /// Get template by type
    /// </summary>
    public async Task<NotificationEmailTemplate?> GetByTypeAsync(string templateType)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                SELECT Id, TemplateType, Subject, HtmlBody, IsDefault, CreatedAt, UpdatedAt
                FROM NotificationEmailTemplate
                WHERE TemplateType = @TemplateType";

            var template = await connection.QueryFirstOrDefaultAsync<NotificationEmailTemplate>(
                sql,
                new { TemplateType = templateType });

            return template;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving email template for type {TemplateType}", templateType);
            return null;
        }
    }

    /// <summary>
    /// Create a new email template
    /// </summary>
    public async Task<int> CreateAsync(NotificationEmailTemplate template)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO NotificationEmailTemplate (TemplateType, Subject, HtmlBody, IsDefault, CreatedAt, UpdatedAt)
                VALUES (@TemplateType, @Subject, @HtmlBody, @IsDefault, @CreatedAt, @UpdatedAt);
                SELECT last_insert_rowid();";

            var id = await connection.ExecuteScalarAsync<int>(sql, new
            {
                template.TemplateType,
                template.Subject,
                template.HtmlBody,
                template.IsDefault,
                template.CreatedAt,
                template.UpdatedAt
            });

            Log.Debug("Created email template: {TemplateType} (ID: {Id})", template.TemplateType, id);
            return id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating email template for type {TemplateType}", template.TemplateType);
            throw;
        }
    }

    /// <summary>
    /// Update an existing email template
    /// </summary>
    public async Task<bool> UpdateAsync(NotificationEmailTemplate template)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE NotificationEmailTemplate
                SET Subject = @Subject, HtmlBody = @HtmlBody, IsDefault = @IsDefault, UpdatedAt = @UpdatedAt
                WHERE TemplateType = @TemplateType";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                template.TemplateType,
                template.Subject,
                template.HtmlBody,
                template.IsDefault,
                template.UpdatedAt
            });

            if (rowsAffected > 0)
            {
                Log.Information("Updated email template: {TemplateType}", template.TemplateType);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating email template for type {TemplateType}", template.TemplateType);
            throw;
        }
    }

    /// <summary>
    /// Delete an email template
    /// </summary>
    public async Task<bool> DeleteAsync(string templateType)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = "DELETE FROM NotificationEmailTemplate WHERE TemplateType = @TemplateType";

            var rowsAffected = await connection.ExecuteAsync(sql, new { TemplateType = templateType });

            if (rowsAffected > 0)
            {
                Log.Information("Deleted email template: {TemplateType}", templateType);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting email template for type {TemplateType}", templateType);
            throw;
        }
    }

    /// <summary>
    /// Reset a template to its default version
    /// </summary>
    public async Task<bool> ResetToDefaultAsync(string templateType)
    {
        try
        {
            var defaultTemplate = GetDefaultTemplate(templateType);
            if (defaultTemplate == null)
            {
                Log.Warning("No default template found for type {TemplateType}", templateType);
                return false;
            }

            defaultTemplate.UpdatedAt = DateTime.UtcNow;
            return await UpdateAsync(defaultTemplate);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting email template to default for type {TemplateType}", templateType);
            return false;
        }
    }

    /// <summary>
    /// Seed default templates if the table is empty
    /// </summary>
    public async Task SeedDefaultTemplatesAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Check if templates already exist
            const string countSql = "SELECT COUNT(*) FROM NotificationEmailTemplate";
            var count = await connection.ExecuteScalarAsync<int>(countSql);

            if (count > 0)
            {
                Log.Debug("Email templates already exist in database, skipping seed");
                return;
            }

            // Insert all default templates
            var templateTypes = new[]
            {
                "ProfileSuccess",
                "ProfileFailure",
                "JobSuccess",
                "JobFailure",
                "NewUser",
                "NewApiKey",
                "NewWebhook",
                "NewEmailApproval",
                "DatabaseSizeThreshold"
            };

            foreach (var templateType in templateTypes)
            {
                var defaultTemplate = GetDefaultTemplate(templateType);
                if (defaultTemplate != null)
                {
                    await CreateAsync(defaultTemplate);
                }
            }

            Log.Debug("Seeded {TemplateCount} default email templates", templateTypes.Length);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error seeding default email templates");
        }
    }

    /// <summary>
    /// Get the default template for a given type
    /// </summary>
    private NotificationEmailTemplate? GetDefaultTemplate(string templateType)
    {
        return templateType switch
        {
            "ProfileSuccess" => new NotificationEmailTemplate
            {
                TemplateType = "ProfileSuccess",
                Subject = "[Reef] Profile '{ProfileName}' executed successfully",
                HtmlBody = BuildDefaultSuccessEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "ProfileFailure" => new NotificationEmailTemplate
            {
                TemplateType = "ProfileFailure",
                Subject = "[Reef] Profile '{ProfileName}' execution failed",
                HtmlBody = BuildDefaultFailureEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "JobSuccess" => new NotificationEmailTemplate
            {
                TemplateType = "JobSuccess",
                Subject = "[Reef] Job '{JobName}' completed successfully",
                HtmlBody = BuildDefaultJobSuccessEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "JobFailure" => new NotificationEmailTemplate
            {
                TemplateType = "JobFailure",
                Subject = "[Reef] Job '{JobName}' failed",
                HtmlBody = BuildDefaultJobFailureEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewUser" => new NotificationEmailTemplate
            {
                TemplateType = "NewUser",
                Subject = "[Reef] New user created: {Username}",
                HtmlBody = BuildDefaultNewUserEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewApiKey" => new NotificationEmailTemplate
            {
                TemplateType = "NewApiKey",
                Subject = "[Reef] New API key created: {KeyName}",
                HtmlBody = BuildDefaultNewApiKeyEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewWebhook" => new NotificationEmailTemplate
            {
                TemplateType = "NewWebhook",
                Subject = "[Reef] New webhook created: {WebhookName}",
                HtmlBody = BuildDefaultNewWebhookEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewEmailApproval" => new NotificationEmailTemplate
            {
                TemplateType = "NewEmailApproval",
                Subject = "[Reef] {PendingCount} email{Plural} pending approval",
                HtmlBody = BuildDefaultNewEmailApprovalEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "DatabaseSizeThreshold" => new NotificationEmailTemplate
            {
                TemplateType = "DatabaseSizeThreshold",
                Subject = "[Reef] Database size critical: {CurrentMb}MB (threshold: {ThresholdMb}MB)",
                HtmlBody = BuildDefaultDatabaseSizeEmailBody(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            _ => null
        };
    }

    // ===== Default Template HTML Bodies =====

    private string BuildDefaultSuccessEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #10b981; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #10b981; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>✓ Profile Executed Successfully</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Profile:</span>
                        <span class='value'>{ProfileName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Execution ID:</span>
                        <span class='value'>{ExecutionId}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Started At:</span>
                        <span class='value'>{StartedAt.GMT+1}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Completed At:</span>
                        <span class='value'>{CompletedAt.GMT+1}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Execution Time:</span>
                        <span class='value'>{ExecutionTime}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Rows Exported:</span>
                        <span class='value'>{RowCount}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>File Size:</span>
                        <span class='value'>{FileSize}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Output Path:</span>
                        <span class='value'>{OutputPath}</span>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultFailureEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #ef4444; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #ef4444; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        .error-box { background-color: #fee2e2; border-left: 5px solid #ef4444; padding: 15px; margin: 20px 0; border-radius: 4px; }
        .error-box strong { color: #991b1b; display: block; margin-bottom: 8px; }
        .error-message { color: #7f1d1d; word-break: break-word; font-family: 'Courier New', monospace; font-size: 13px; line-height: 1.5; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
            .error-box { padding: 12px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>Profile Execution Failed</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Profile:</span>
                        <span class='value'>{ProfileName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Execution ID:</span>
                        <span class='value'>{ExecutionId}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Started At:</span>
                        <span class='value'>{StartedAt.GMT+1}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Failed At:</span>
                        <span class='value'>{CompletedAt.GMT+1}</span>
                    </div>
                </div>
                <div class='error-box'>
                    <strong>Error Details:</strong>
                    <div class='error-message'>{ErrorMessage}</div>
                </div>
                <p style='color: #666; font-size: 13px;'>Please check the Reef dashboard execution logs for additional diagnostic information.</p>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultJobSuccessEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #10b981; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #10b981; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>✓ Job Completed Successfully</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Job:</span>
                        <span class='value'>{JobName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Completed At:</span>
                        <span class='value'>{CompletedAt}</span>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultJobFailureEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #ef4444; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #ef4444; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        .error-box { background-color: #fee2e2; border-left: 5px solid #ef4444; padding: 15px; margin: 20px 0; border-radius: 4px; }
        .error-box strong { color: #991b1b; display: block; margin-bottom: 8px; }
        .error-message { color: #7f1d1d; word-break: break-word; font-family: 'Courier New', monospace; font-size: 13px; line-height: 1.5; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
            .error-box { padding: 12px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>Job Failed</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Job:</span>
                        <span class='value'>{JobName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Failed At:</span>
                        <span class='value'>{CompletedAt}</span>
                    </div>
                </div>
                <div class='error-box'>
                    <strong>Error Details:</strong>
                    <div class='error-message'>{ErrorMessage}</div>
                </div>
                <p style='color: #666; font-size: 13px;'>Please check the Reef dashboard job logs for additional diagnostic information.</p>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultNewUserEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #3b82f6; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #3b82f6; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>👤 New User Created</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Username:</span>
                        <span class='value'>{Username}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Email:</span>
                        <span class='value'>{Email}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Created At:</span>
                        <span class='value'>{CreatedAt.GMT+1}</span>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultNewApiKeyEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #8b5cf6; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #8b5cf6; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>New API Key Created</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Key Name:</span>
                        <span class='value'>{KeyName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Created At:</span>
                        <span class='value'>{CreatedAt.GMT+1}</span>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultNewWebhookEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #06b6d4; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #06b6d4; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>🪝 New Webhook Created</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Webhook:</span>
                        <span class='value'>{WebhookName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Created At:</span>
                        <span class='value'>{CreatedAt.GMT+1}</span>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultNewEmailApprovalEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #f59e0b; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #f59e0b; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        .info-box { background-color: #fef3c7; border-left: 5px solid #f59e0b; padding: 15px; margin: 20px 0; border-radius: 4px; }
        .info-box p { color: #92400e; margin: 0; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
            .info-box { padding: 12px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>✉️ Email{Plural} Pending Approval</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Pending Items:</span>
                        <span class='value'>{PendingCount}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Notification Time:</span>
                        <span class='value'>{NotificationTime.GMT+1}</span>
                    </div>
                </div>
                <div class='info-box'>
                    <p>There {PluralVerb} {PendingCount} email{Plural} waiting for approval in the workflow. Please review and approve or reject {PluralThem} in the Reef dashboard.</p>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDefaultDatabaseSizeEmailBody()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #f59e0b; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #f59e0b; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        .warning { background-color: #fef3c7; border-left: 5px solid #f59e0b; padding: 15px; margin: 20px 0; border-radius: 4px; }
        .warning p { color: #92400e; margin: 0; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
            .warning { padding: 12px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>⚠️ Database Size Alert</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Threshold:</span>
                        <span class='value'>{ThresholdMb} MB</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Current Size:</span>
                        <span class='value'>{CurrentMb} MB</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Over Threshold:</span>
                        <span class='value'>{ExcessMb} MB</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Checked At:</span>
                        <span class='value'>{CheckedAt.GMT+1}</span>
                    </div>
                </div>
                <div class='warning'>
                    <p>Please review your retention policies and consider archiving or cleaning old execution records to reclaim database storage space.</p>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }
}
