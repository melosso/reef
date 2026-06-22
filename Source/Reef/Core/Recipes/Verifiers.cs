using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.Sources;
using Reef.Core.Targets;
using Reef.Core.TemplateEngines;

namespace Reef.Core.Recipes;

// Wraps ConnectionService.TestConnectionAsync (same mechanism as ConnectionsEndpoints' /test).
public class ConnectionVerifier : IRecipeVerifier
{
    private readonly ConnectionService _connectionService;
    private readonly EncryptionService _encryptionService;

    public ConnectionVerifier(ConnectionService connectionService, EncryptionService encryptionService)
    {
        _connectionService = connectionService;
        _encryptionService = encryptionService;
    }

    public async Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default)
    {
        if (context.EntityId is not { } connectionId)
            return new RecipeVerifyResult { Success = false, Message = "No Connection has been saved for this step yet." };

        var connection = await _connectionService.GetByIdAsync(connectionId, ct);
        if (connection is null)
            return new RecipeVerifyResult { Success = false, Message = $"Connection {connectionId} not found." };

        // GetByIdAsync returns the connection string encrypted; decrypt before testing.
        var connectionString = _encryptionService.IsEncrypted(connection.ConnectionString)
            ? _encryptionService.Decrypt(connection.ConnectionString)
            : connection.ConnectionString;

        var (success, message, responseTimeMs) = await _connectionService.TestConnectionAsync(connection.Type, connectionString, ct);

        return new RecipeVerifyResult
        {
            Success = success,
            Message = message ?? (success ? "Connection successful." : "Connection test failed."),
            Detail = $"{responseTimeMs}ms"
        };
    }
}

// Wraps ImportSourceFactory + IImportSource.TestAsync (same path as ImportProfilesEndpoints.TestSource).
public class HttpSourceVerifier : IRecipeVerifier
{
    private readonly ImportProfileService _importProfileService;

    public HttpSourceVerifier(ImportProfileService importProfileService)
    {
        _importProfileService = importProfileService;
    }

    public async Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default)
    {
        if (context.EntityId is not { } importProfileId)
            return new RecipeVerifyResult { Success = false, Message = "No ImportProfile has been saved for this step yet." };

        var profile = await _importProfileService.GetByIdAsync(importProfileId);
        if (profile is null)
            return new RecipeVerifyResult { Success = false, Message = $"ImportProfile {importProfileId} not found." };

        try
        {
            var source = ImportSourceFactory.Create(profile.SourceType);
            var (success, message) = await source.TestAsync(profile, ct);
            return new RecipeVerifyResult { Success = success, Message = message ?? (success ? "Source reachable." : "Source test failed.") };
        }
        catch (Exception ex)
        {
            return new RecipeVerifyResult { Success = false, Message = $"Source test failed: {ex.Message}" };
        }
    }
}

// Wraps DatabaseImportTarget.TestAsync's table-exists check.
public class StagingTableVerifier : IRecipeVerifier
{
    private readonly ConnectionService _connectionService;
    private readonly DatabaseImportTarget _databaseImportTarget;

    public StagingTableVerifier(ConnectionService connectionService, DatabaseImportTarget databaseImportTarget)
    {
        _connectionService = connectionService;
        _databaseImportTarget = databaseImportTarget;
    }

    public async Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default)
    {
        if (context.ConnectionId is not { } connectionId)
            return new RecipeVerifyResult { Success = false, Message = "No Connection selected for the staging table." };

        if (string.IsNullOrWhiteSpace(context.TableName))
            return new RecipeVerifyResult { Success = false, Message = "No staging table name configured." };

        var connection = await _connectionService.GetByIdAsync(connectionId, ct);
        if (connection is null)
            return new RecipeVerifyResult { Success = false, Message = $"Connection {connectionId} not found." };

        var (success, message) = await _databaseImportTarget.TestAsync(connection, context.TableName, ct);
        return new RecipeVerifyResult { Success = success, Message = message ?? (success ? "Table exists." : "Table check failed.") };
    }
}

// Wraps DestinationService.TestDestinationConfigurationAsync - a real send, not just a probe.
public class EmailDestinationVerifier : IRecipeVerifier
{
    private readonly DestinationService _destinationService;

    public EmailDestinationVerifier(DestinationService destinationService)
    {
        _destinationService = destinationService;
    }

    public async Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default)
    {
        if (context.EntityId is not { } destinationId)
            return new RecipeVerifyResult { Success = false, Message = "No Email destination has been saved for this step yet." };

        // Needs the fully-decrypted (never masked) config to actually send the test email.
        var destination = await _destinationService.GetByIdForExecutionAsync(destinationId, ct);
        if (destination is null)
            return new RecipeVerifyResult { Success = false, Message = $"Destination {destinationId} not found." };

        var result = await _destinationService.TestDestinationConfigurationAsync(
            destination.Type,
            destination.ConfigurationJson);

        return new RecipeVerifyResult
        {
            Success = result.Success,
            Message = result.Message ?? (result.Success ? "Test email sent." : "Test email failed."),
            Detail = result.ResponseTimeMs is { } ms ? $"{ms}ms" : null
        };
    }
}

// Renders against one real staging row, or mock data if the table is empty - same
// Scriban preview path QueryTemplateEndpoints' /preview handler uses.
public class ScribanTemplateVerifier : IRecipeVerifier
{
    private readonly QueryTemplateService _queryTemplateService;
    private readonly ConnectionService _connectionService;
    private readonly QueryExecutor _queryExecutor;
    private readonly ScribanTemplateEngine _templateEngine;

    public ScribanTemplateVerifier(
        QueryTemplateService queryTemplateService,
        ConnectionService connectionService,
        QueryExecutor queryExecutor,
        ScribanTemplateEngine templateEngine)
    {
        _queryTemplateService = queryTemplateService;
        _connectionService = connectionService;
        _queryExecutor = queryExecutor;
        _templateEngine = templateEngine;
    }

    public async Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default)
    {
        if (context.EntityId is not { } templateId)
            return new RecipeVerifyResult { Success = false, Message = "No QueryTemplate has been saved for this step yet." };

        var template = await _queryTemplateService.GetByIdAsync(templateId);
        if (template is null)
            return new RecipeVerifyResult { Success = false, Message = $"QueryTemplate {templateId} not found." };

        var rows = await TryFetchOneStagingRowAsync(context, ct) ?? new List<Dictionary<string, object>> { context.MockTemplateRow ?? MockOrderRow() };

        try
        {
            var preview = await _templateEngine.TransformAsync(rows, template.Template);
            var truncated = preview.Length > 500 ? preview[..500] + "…" : preview;
            return new RecipeVerifyResult { Success = true, Message = "Template rendered successfully.", Detail = truncated };
        }
        catch (Exception ex)
        {
            return new RecipeVerifyResult { Success = false, Message = $"Template render failed: {ex.Message}" };
        }
    }

    private async Task<List<Dictionary<string, object>>?> TryFetchOneStagingRowAsync(RecipeVerifyContext context, CancellationToken ct)
    {
        if (context.ConnectionId is not { } connectionId || string.IsNullOrWhiteSpace(context.TableName))
            return null;

        var connection = await _connectionService.GetByIdAsync(connectionId, ct);
        if (connection is null) return null;

        var sql = connection.Type == "SqlServer"
            ? $"SELECT TOP 1 * FROM {context.TableName}"
            : $"SELECT * FROM {context.TableName} LIMIT 1";

        var (success, rows, _, _) = await _queryExecutor.ExecuteQueryAsync(connection, sql, null, commandTimeout: 15);
        return success && rows.Count > 0 ? rows : null;
    }

    private static Dictionary<string, object> MockOrderRow() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["order_number"] = "1001",
        ["order_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["order_status"] = "PROCESSING",
        ["customer_name"] = "Jane Doe",
        ["company"] = "Reef Demo Store",
        ["currency_symbol"] = "$",
        ["total"] = "129.99",
        ["subtotal"] = "119.99",
        ["shipping"] = "10.00",
        ["items_json"] = "[{\"product_name\":\"Sample Product\",\"sku\":\"SKU-1\",\"quantity\":1,\"unit_price\":\"119.99\",\"line_total\":\"119.99\"}]"
    };
}

// Wraps the same QueryExecutor path ProfilesEndpoints' TestQuery/TestQueryPreview use.
public class ExportQueryVerifier : IRecipeVerifier
{
    private readonly ConnectionService _connectionService;
    private readonly QueryExecutor _queryExecutor;

    public ExportQueryVerifier(ConnectionService connectionService, QueryExecutor queryExecutor)
    {
        _connectionService = connectionService;
        _queryExecutor = queryExecutor;
    }

    public async Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default)
    {
        if (context.ConnectionId is not { } connectionId)
            return new RecipeVerifyResult { Success = false, Message = "No Connection selected for the export query." };

        if (context.Params.GetValueOrDefault("query") is not string query || string.IsNullOrWhiteSpace(query))
            return new RecipeVerifyResult { Success = false, Message = "No export query configured for this step." };

        var connection = await _connectionService.GetByIdAsync(connectionId, ct);
        if (connection is null)
            return new RecipeVerifyResult { Success = false, Message = $"Connection {connectionId} not found." };

        var testQuery = query.Trim();
        var hasLimit = testQuery.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
            || testQuery.Contains("TOP", StringComparison.OrdinalIgnoreCase)
            || testQuery.Contains("FETCH", StringComparison.OrdinalIgnoreCase);

        if (!hasLimit)
        {
            if (connection.Type == "SqlServer")
            {
                var selectIndex = testQuery.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
                if (selectIndex >= 0) testQuery = testQuery.Insert(selectIndex + "SELECT".Length, " TOP 25");
            }
            else
            {
                testQuery += " LIMIT 25";
            }
        }

        var (success, rows, error, executionTime) = await _queryExecutor.ExecuteQueryAsync(connection, testQuery, null, commandTimeout: 30);

        if (!success)
            return new RecipeVerifyResult { Success = false, Message = error ?? "Query failed." };

        return new RecipeVerifyResult
        {
            Success = true,
            Message = $"Query returned {rows.Count} row(s) in {executionTime}ms.",
            Detail = rows.Count == 0 ? "No rows matched yet - this is expected if no orders have synced." : null
        };
    }
}
