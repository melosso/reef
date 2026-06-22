namespace Reef.Core.Recipes;

// Second recipe proving the engine is generic: no staging table, no HTTP import, no
// webhook - just Connection/Destination/QueryTemplate/Profile steps. Required zero
// engine changes to add.
public static class ErrorDigestRecipe
{
    public const string Key = "system-error-daily-digest";

    // No wizard-created staging table here - this assumes the user already has an
    // error/log table, so the query is a starting point to edit, not a fixed schema.
    public const string DefaultExportQuery = """
        SELECT
            'ops-team@yourcompany.com' AS email,
            'Daily System Report' AS subject,
            'RPT-' || strftime('%Y%m%d', 'now') AS report_id,
            'Your Company' AS company,
            strftime('%Y-%m-%d %H:%M', 'now') AS generated_at,
            0 AS total_count,
            0 AS critical_count,
            0 AS warn_count,
            'https://dashboard.example.com/daily' AS dashboard_url,
            'https://example.com/unsubscribe' AS unsubscribe_url,
            strftime('%Y', 'now') AS year,
            '[]' AS error_list_json
        """;

    public static Dictionary<string, object> MockDigestRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["email"] = "ops-team@yourcompany.com",
        ["subject"] = "Daily System Report: 2 Critical Events",
        ["report_id"] = "RPT-20260101",
        ["company"] = "Reef Demo Co",
        ["generated_at"] = DateTime.UtcNow.ToString("MMM dd, yyyy HH:mm"),
        ["total_count"] = "12",
        ["critical_count"] = "2",
        ["warn_count"] = "4",
        ["dashboard_url"] = "https://dashboard.example.com/daily",
        ["unsubscribe_url"] = "https://example.com/unsubscribe",
        ["year"] = DateTime.UtcNow.Year.ToString(),
        ["error_list_json"] = "[{\"time\":\"03:14:07\",\"level\":\"CRITICAL\",\"source\":\"PaymentGateway\",\"message\":\"Connection timed out waiting for response\",\"id\":\"ERR-4821\"}]"
    };

    public static RecipeDefinition Definition { get; } = new()
    {
        Key = Key,
        Name = "System Error Daily Digest",
        Description = "Email your team a daily summary of system errors/warnings pulled from your own database - no import required.",
        Category = "Database Sync",
        Icon = "alert-triangle",
        Steps = new List<RecipeStepDefinition>
        {
            new()
            {
                StepKey = "connection",
                Title = "Database Connection",
                EntityType = RecipeEntityType.Connection,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.Connection
            },
            new()
            {
                StepKey = "group",
                Title = "Organize",
                EntityType = RecipeEntityType.Group,
                IsShared = true,
                VerifierKind = null
            },
            new()
            {
                StepKey = "destination",
                Title = "Email Destination",
                EntityType = RecipeEntityType.Destination,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.EmailDestination
            },
            new()
            {
                StepKey = "query-template",
                Title = "Digest Email Template",
                EntityType = RecipeEntityType.QueryTemplate,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "export-profile",
                Title = "Send Daily Digest",
                EntityType = RecipeEntityType.Profile,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "jobs",
                Title = "Scheduling (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                VerifierKind = null
            }
        }
    };
}
