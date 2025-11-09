using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using Reef.Core.Security;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import.DataSourceExecutors;

/// <summary>
/// Executor for querying data from relational databases
/// Supports SQL Server, SQLite, PostgreSQL, MySQL via connection strings
/// </summary>
public class DatabaseDataSourceExecutor : IDataSourceExecutor
{
    public DataSourceType SourceType => DataSourceType.Database;

    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(DatabaseDataSourceExecutor));
    private readonly EncryptionService? _encryptionService;

    public DatabaseDataSourceExecutor(EncryptionService? encryptionService = null)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Executes a SQL query against a database and returns all rows
    /// SourceUri contains the connection string, SourceConfiguration contains the SQL query
    /// </summary>
    public async Task<List<Dictionary<string, object>>> ExecuteAsync(
        string sourceUri,
        string? sourceConfig,
        CancellationToken cancellationToken = default)
    {
        var config = ParseConfiguration(sourceConfig);
        var allRows = new List<Dictionary<string, object>>();

        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Log.Information("Starting database import");

            if (string.IsNullOrWhiteSpace(config.Query))
                throw new InvalidOperationException("No SQL query specified in configuration");

            var connectionString = sourceUri;

            // Decrypt if necessary
            if (config.EncryptionEnabled == true && _encryptionService != null)
            {
                connectionString = _encryptionService.Decrypt(sourceUri);
            }

            var rows = await ExecuteQueryAsync(connectionString, config.Query, config, cancellationToken);
            allRows.AddRange(rows);

            watch.Stop();
            Log.Information("Database import completed: {Count} rows in {ElapsedMs}ms", allRows.Count, watch.ElapsedMilliseconds);

            return allRows;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Database import was cancelled after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "Database import failed after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Validates that the database is accessible
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        string sourceUri,
        string? sourceConfig,
        CancellationToken cancellationToken = default)
    {
        var config = ParseConfiguration(sourceConfig);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Log.Information("Validating database connection");

            if (string.IsNullOrWhiteSpace(config.Query))
                throw new InvalidOperationException("No SQL query specified in configuration");

            var connectionString = sourceUri;

            // Decrypt if necessary
            if (config.EncryptionEnabled == true && _encryptionService != null)
            {
                connectionString = _encryptionService.Decrypt(sourceUri);
            }

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            watch.Stop();
            Log.Information("Database validation successful");

            return new ValidationResult
            {
                IsValid = true,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "Database validation error");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
    }

    // ===== Helper Methods =====

    private async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(
        string connectionString,
        string query,
        DatabaseSourceConfig config,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object>>();

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            Log.Debug("Executing query: {Query}", SanitizeQueryForLogging(query));

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = config.CommandTimeout ?? 300;

            // Add parameters if provided
            if (config.Parameters != null && config.Parameters.Count > 0)
            {
                foreach (var param in config.Parameters)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = param.Key;
                    parameter.Value = param.Value ?? DBNull.Value;
                    command.Parameters.Add(parameter);
                }
            }

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object>();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var columnName = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[columnName] = value ?? DBNull.Value;
                }

                rows.Add(row);

                // Limit rows if specified
                if (config.MaxRows.HasValue && rows.Count >= config.MaxRows.Value)
                {
                    break;
                }
            }

            Log.Debug("Query returned {Count} rows", rows.Count);
            return rows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to execute database query");
            throw;
        }
    }

    private string SanitizeQueryForLogging(string query)
    {
        // Remove or mask sensitive parts of query for logging
        var sanitized = query
            .Replace("\n", " ")
            .Replace("\r", " ");

        if (sanitized.Length > 200)
            sanitized = sanitized.Substring(0, 200) + "...";

        return sanitized;
    }

    private DatabaseSourceConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return new DatabaseSourceConfig();

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
            return JsonSerializer.Deserialize<DatabaseSourceConfig>(configJson, options) ?? new DatabaseSourceConfig();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse database source configuration, using defaults");
            return new DatabaseSourceConfig();
        }
    }
}

/// <summary>
/// Configuration for database data source
/// Stored as JSON in ImportProfile.SourceConfiguration
/// </summary>
public class DatabaseSourceConfig
{
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("commandTimeout")]
    public int? CommandTimeout { get; set; } = 300; // 5 minutes

    [JsonPropertyName("maxRows")]
    public int? MaxRows { get; set; }

    [JsonPropertyName("encryptionEnabled")]
    public bool? EncryptionEnabled { get; set; } = false;

    [JsonPropertyName("parameters")]
    public Dictionary<string, object?>? Parameters { get; set; }
}
