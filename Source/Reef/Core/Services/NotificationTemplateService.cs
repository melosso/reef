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
                SELECT Id, TemplateType, Subject, HtmlBody, IsDefault, CTAButtonText, CTAUrlOverride, CreatedAt, UpdatedAt
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
                SELECT Id, TemplateType, Subject, HtmlBody, IsDefault, CTAButtonText, CTAUrlOverride, CreatedAt, UpdatedAt
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
                SET Subject = @Subject, HtmlBody = @HtmlBody, IsDefault = @IsDefault,
                    CTAButtonText = @CTAButtonText, CTAUrlOverride = @CTAUrlOverride, UpdatedAt = @UpdatedAt
                WHERE TemplateType = @TemplateType";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                template.TemplateType,
                template.Subject,
                template.HtmlBody,
                template.IsDefault,
                template.CTAButtonText,
                template.CTAUrlOverride,
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
    /// NOTE: Template seeding is now handled by DatabaseInitializer.SeedNotificationEmailTemplatesAsync()
    /// This method is kept for backward compatibility but delegates to DatabaseInitializer
    /// </summary>
    [Obsolete("Template seeding is now handled by DatabaseInitializer. This method is kept for backward compatibility.")]
    public async Task SeedDefaultTemplatesAsync()
    {
        Log.Debug("NotificationTemplateService.SeedDefaultTemplatesAsync() is deprecated. Templates are now seeded by DatabaseInitializer.");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Get the default template for a given type
    /// Used for "Reset to Default" feature - must match DatabaseInitializer templates
    /// </summary>
    private NotificationEmailTemplate? GetDefaultTemplate(string templateType)
    {
        return templateType switch
        {
            "ProfileSuccess" => new NotificationEmailTemplate
            {
                TemplateType = "ProfileSuccess",
                Subject = "[Reef] Profile '{{ ProfileName }}' executed successfully",
                HtmlBody = BuildDefaultSuccessEmailBody(),
                IsDefault = true,
                CTAButtonText = "View Execution",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "ProfileFailure" => new NotificationEmailTemplate
            {
                TemplateType = "ProfileFailure",
                Subject = "[Reef] Profile '{{ ProfileName }}' execution failed",
                HtmlBody = BuildDefaultFailureEmailBody(),
                IsDefault = true,
                CTAButtonText = "View Execution",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "JobSuccess" => new NotificationEmailTemplate
            {
                TemplateType = "JobSuccess",
                Subject = "[Reef] Job '{{ JobName }}' completed successfully",
                HtmlBody = BuildDefaultJobSuccessEmailBody(),
                IsDefault = true,
                CTAButtonText = "View Job",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "JobFailure" => new NotificationEmailTemplate
            {
                TemplateType = "JobFailure",
                Subject = "[Reef] Job '{{ JobName }}' failed",
                HtmlBody = BuildDefaultJobFailureEmailBody(),
                IsDefault = true,
                CTAButtonText = "View Job",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewUser" => new NotificationEmailTemplate
            {
                TemplateType = "NewUser",
                Subject = "[Reef] New user created: {{ Username }}",
                HtmlBody = BuildDefaultNewUserEmailBody(),
                IsDefault = true,
                CTAButtonText = "View Users",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewApiKey" => new NotificationEmailTemplate
            {
                TemplateType = "NewApiKey",
                Subject = "[Reef] New API key created: {{ KeyName }}",
                HtmlBody = BuildDefaultNewApiKeyEmailBody(),
                IsDefault = true,
                CTAButtonText = "Manage API Keys",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewWebhook" => new NotificationEmailTemplate
            {
                TemplateType = "NewWebhook",
                Subject = "[Reef] New webhook created: {{ WebhookName }}",
                HtmlBody = BuildDefaultNewWebhookEmailBody(),
                IsDefault = true,
                CTAButtonText = "Manage Webhooks",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "NewEmailApproval" => new NotificationEmailTemplate
            {
                TemplateType = "NewEmailApproval",
                Subject = "[Reef] {{ PendingCount }} email{{ Plural }} pending approval",
                HtmlBody = BuildDefaultNewEmailApprovalEmailBody(),
                IsDefault = true,
                CTAButtonText = "Review Approvals",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            "DatabaseSizeThreshold" => new NotificationEmailTemplate
            {
                TemplateType = "DatabaseSizeThreshold",
                Subject = "[Reef] Database size critical",
                HtmlBody = BuildDefaultDatabaseSizeEmailBody(),
                IsDefault = true,
                CTAButtonText = "View System Status",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            _ => null
        };
    }

    // ===== Default Template HTML Bodies =====
    // These templates MUST match DatabaseInitializer.cs templates for consistency
    // Used for "Reset to Default" feature in the template management UI

    private static string BuildDefaultSuccessEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Profile Execution Success</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Profile {{ ProfileName }} executed successfully.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  Profile executed successfully
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Profile <strong style=""color:#111827;"">{{ ProfileName }}</strong> has completed successfully.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Execution ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ExecutionId }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Execution Time
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ExecutionTime }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Row Count
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ RowCount }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    File Size
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ FileSize }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultFailureEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Profile Execution Failed</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Profile {{ ProfileName }} execution failed.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px; color:#dc2626;"">
                  Profile execution failed
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Profile <strong style=""color:#111827;""{{ ProfileName }}</strong> encountered an error during execution.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fef2f2; border:1px solid #fecaca; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7f1d1d;"">
                    <strong>Error:</strong> {{ ErrorMessage }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Execution ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ExecutionId }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultJobSuccessEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Job Completed Successfully</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Job {{ JobName }} completed successfully.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  Job completed successfully
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Job <strong style=""color:#111827;"">{{ JobName }}</strong> has completed successfully.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Job ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ JobId }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultJobFailureEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Job Failed</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Job {{ JobName }} failed.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px; color:#dc2626;"">
                  Job failed
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Job <strong style=""color:#111827;"">{{ JobName }}</strong> encountered an error.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fef2f2; border:1px solid #fecaca; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7f1d1d;"">
                    <strong>Error:</strong> {{ ErrorMessage }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Job ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ JobId }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultNewUserEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>New User Created</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    New user {{ Username }} created.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  New user created
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                A new user account has been created in the system.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Username
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ Username }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Email
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ Email }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultNewApiKeyEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>New API Key Created</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    New API key {{ KeyName }} created.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  New API key created
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                A new API key has been created in the system.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Key Name
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ KeyName }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#eff6ff; border:1px solid #dbeafe; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#1e3a8a;"">
                    Please ensure this API key is stored securely and rotated regularly according to your security policy.
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultNewWebhookEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>New Webhook Created</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    New webhook {{ WebhookName }} created.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  New webhook created
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                A new webhook has been created in the system.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Webhook Name
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ WebhookName }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultNewEmailApprovalEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Pending Approval</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    {{ PendingCount }} pending approval in Reef.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  Email{{ Plural }} pending approval
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                There {{ PluralVerb }} <strong style=""color:#111827;"">{{ PendingCount }}</strong> email{{ Plural }} waiting for approval.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Pending items
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ PendingCount }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Notification time
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ NotificationTime | date.to_string '%Y-%m-%d %H:%M:%S' }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fff7ed; border:1px solid #ffedd5; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7c2d12;"">
                    Please review and approve or reject {{ PluralThem }} in the application dashboard.
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    private static string BuildDefaultDatabaseSizeEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Database Size Alert</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Database size threshold exceeded.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px; color:#dc2626;"">
                  Database size critical
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                The database has exceeded the configured size threshold and requires attention.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Current Size
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ CurrentMB }} MB
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Threshold
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ThresholdMB }} MB
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Excess
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#dc2626; font-weight:600;"">
                    +{{ ExcessMB }} MB
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fef2f2; border:1px solid #fecaca; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7f1d1d;"">
                    <strong>Action Required:</strong> Consider archiving old data, increasing storage capacity, or adjusting the threshold limit.
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }
}
