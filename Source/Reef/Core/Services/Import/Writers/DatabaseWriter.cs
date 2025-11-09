using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using Reef.Core.Security;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import.Writers;

/// <summary>
/// Writes imported data to a SQL database (SQLite, SQL Server, PostgreSQL, MySQL, etc.)
/// Supports INSERT (append) and UPSERT (insert or update) modes
/// </summary>
public class DatabaseWriter : IDataWriter
{
    public ImportDestinationType DestinationType => ImportDestinationType.Database;

    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(DatabaseWriter));
    private readonly EncryptionService _encryptionService;
    private readonly JsonSerializerOptions _jsonOptions;

    public DatabaseWriter(EncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <summary>
    /// Writes data rows to the database
    /// </summary>
    public async Task<WriteResult> WriteAsync(
        string destinationUri,
        string? destinationConfig,
        List<Dictionary<string, object>> rows,
        List<FieldMapping> fieldMappings,
        WriteMode mode = WriteMode.Insert,
        CancellationToken cancellationToken = default)
    {
        var config = ParseConfiguration(destinationConfig);
        var tableName = destinationUri;  // URI is the table name
        var result = new WriteResult();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        if (rows.Count == 0)
        {
            Log.Information("No rows to write to {Table}", tableName);
            result.ExecutionTimeMs = watch.ElapsedMilliseconds;
            return result;
        }

        try
        {
            Log.Information("Writing {Count} rows to {Table} using {Mode} mode", rows.Count, tableName, mode);

            // Get connection from the encrypted connection string
            var connectionString = _encryptionService.Decrypt(config.ConnectionString);

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Validate table exists and get schema
            var schema = await GetTableSchemaAsync(connection, tableName);
            if (schema.Count == 0)
            {
                throw new InvalidOperationException($"Table '{tableName}' does not exist or is not accessible");
            }

            // Map fields and build INSERT/UPSERT statements
            var mappedRows = MapRows(rows, fieldMappings, schema);

            if (mode == WriteMode.Insert)
            {
                result = await InsertRowsAsync(connection, tableName, mappedRows, schema, cancellationToken);
            }
            else if (mode == WriteMode.Upsert)
            {
                result = await UpsertRowsAsync(connection, tableName, mappedRows, schema, config, cancellationToken);
            }

            watch.Stop();
            result.ExecutionTimeMs = watch.ElapsedMilliseconds;

            Log.Information("Write completed: {Written} rows written, {Failed} failed, {Skipped} skipped in {ElapsedMs}ms",
                result.RowsWritten, result.RowsFailed, result.RowsSkipped, watch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "Database write failed after {ElapsedMs}ms", watch.ElapsedMilliseconds);
            result.ExecutionTimeMs = watch.ElapsedMilliseconds;
            result.ErrorMessages = new List<string> { ex.Message };
            return result;
        }
    }

    /// <summary>
    /// Validates that the database connection and table are accessible
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        string destinationUri,
        string? destinationConfig,
        CancellationToken cancellationToken = default)
    {
        var config = ParseConfiguration(destinationConfig);
        var tableName = destinationUri;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Log.Information("Validating database connection and table {Table}", tableName);

            var connectionString = _encryptionService.Decrypt(config.ConnectionString);

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Try to query the table
            var schema = await GetTableSchemaAsync(connection, tableName);
            watch.Stop();

            if (schema.Count == 0)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Table '{tableName}' not found or not accessible",
                    ResponseTimeMs = watch.ElapsedMilliseconds
                };
            }

            Log.Information("Database validation successful: table {Table} found with {Columns} columns",
                tableName, schema.Count);

            return new ValidationResult
            {
                IsValid = true,
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            watch.Stop();
            Log.Error(ex, "Database validation failed");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Connection error: {ex.Message}",
                ResponseTimeMs = watch.ElapsedMilliseconds
            };
        }
    }

    // ===== Helper Methods =====

    private async Task<WriteResult> InsertRowsAsync(
        SqliteConnection connection,
        string tableName,
        List<Dictionary<string, object>> rows,
        Dictionary<string, (string Type, bool Nullable)> schema,
        CancellationToken cancellationToken)
    {
        var result = new WriteResult();
        var errorMessages = new List<string>();

        try
        {
            using var transaction = connection.BeginTransaction();

            // Build INSERT statement
            var columns = rows[0].Keys.ToList();
            var columnList = string.Join(", ", columns.Select(c => $"[{c}]"));
            var paramList = string.Join(", ", columns.Select(c => $"@{c}"));
            var insertSql = $"INSERT INTO [{tableName}] ({columnList}) VALUES ({paramList})";

            Log.Debug("INSERT SQL: {Sql}", insertSql);

            foreach (var row in rows)
            {
                try
                {
                    await connection.ExecuteAsync(insertSql, row);
                    result.RowsWritten++;
                }
                catch (Exception ex)
                {
                    result.RowsFailed++;
                    var errorMsg = $"Row insert failed: {ex.Message}";
                    errorMessages.Add(errorMsg);
                    Log.Warning(ex, "Failed to insert row: {Message}", errorMsg);
                }
            }

            transaction.Commit();
            result.ErrorMessages = errorMessages;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "INSERT batch failed");
            result.RowsFailed += rows.Count - result.RowsWritten;
            result.ErrorMessages = new List<string> { ex.Message };
        }

        return result;
    }

    private async Task<WriteResult> UpsertRowsAsync(
        SqliteConnection connection,
        string tableName,
        List<Dictionary<string, object>> rows,
        Dictionary<string, (string Type, bool Nullable)> schema,
        DatabaseWriterConfig config,
        CancellationToken cancellationToken)
    {
        var result = new WriteResult();
        var errorMessages = new List<string>();

        if (string.IsNullOrEmpty(config.UpsertKeyColumns) || rows.Count == 0)
        {
            Log.Warning("UPSERT requires key columns to be specified");
            return result;
        }

        try
        {
            using var transaction = connection.BeginTransaction();

            var keyColumns = config.UpsertKeyColumns.Split(',').Select(c => c.Trim()).ToList();
            var allColumns = rows[0].Keys.ToList();
            var updateColumns = allColumns.Except(keyColumns).ToList();

            // SQLite UPSERT syntax
            var columns = string.Join(", ", allColumns.Select(c => $"[{c}]"));
            var values = string.Join(", ", allColumns.Select(c => $"@{c}"));
            var updateClause = string.Join(", ", updateColumns.Select(c => $"[{c}] = @{c}"));
            var whereClause = string.Join(" AND ", keyColumns.Select(c => $"[{c}] = @{c}"));

            var upsertSql = $@"
                INSERT INTO [{tableName}] ({columns})
                VALUES ({values})
                ON CONFLICT({string.Join(", ", keyColumns.Select(c => $"[{c}]"))})
                DO UPDATE SET {updateClause}";

            Log.Debug("UPSERT SQL: {Sql}", upsertSql);

            foreach (var row in rows)
            {
                try
                {
                    await connection.ExecuteAsync(upsertSql, row);
                    result.RowsWritten++;
                }
                catch (Exception ex)
                {
                    result.RowsFailed++;
                    var errorMsg = $"Row upsert failed: {ex.Message}";
                    errorMessages.Add(errorMsg);
                    Log.Warning(ex, "Failed to upsert row: {Message}", errorMsg);
                }
            }

            transaction.Commit();
            result.ErrorMessages = errorMessages;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UPSERT batch failed");
            result.RowsFailed += rows.Count - result.RowsWritten;
            result.ErrorMessages = new List<string> { ex.Message };
        }

        return result;
    }

    private async Task<Dictionary<string, (string Type, bool Nullable)>> GetTableSchemaAsync(
        SqliteConnection connection,
        string tableName)
    {
        var schema = new Dictionary<string, (string Type, bool Nullable)>();

        try
        {
            var columns = await connection.QueryAsync<dynamic>($"PRAGMA table_info({tableName})");

            foreach (var col in columns)
            {
                var name = (string)col.name;
                var type = (string)col.type;
                var notnull = (int)col.notnull == 1;
                schema[name] = (type, !notnull);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get table schema for {Table}", tableName);
        }

        return schema;
    }

    private List<Dictionary<string, object>> MapRows(
        List<Dictionary<string, object>> rows,
        List<FieldMapping> fieldMappings,
        Dictionary<string, (string Type, bool Nullable)> schema)
    {
        var mappedRows = new List<Dictionary<string, object>>();

        foreach (var row in rows)
        {
            var mappedRow = new Dictionary<string, object>();

            if (fieldMappings.Count == 0)
            {
                // No explicit mappings, use source columns as-is
                mappedRow = new Dictionary<string, object>(row);
            }
            else
            {
                // Apply field mappings
                foreach (var mapping in fieldMappings)
                {
                    if (row.TryGetValue(mapping.SourceColumn, out var value))
                    {
                        var convertedValue = ConvertValue(value, mapping.DataType);
                        mappedRow[mapping.DestinationColumn] = convertedValue ?? (mapping.DefaultValue != null ? mapping.DefaultValue : DBNull.Value);
                    }
                    else if (mapping.Required)
                    {
                        mappedRow[mapping.DestinationColumn] = mapping.DefaultValue ?? (object)DBNull.Value;
                    }
                }
            }

            mappedRows.Add(mappedRow);
        }

        return mappedRows;
    }

    private object? ConvertValue(object? value, FieldDataType dataType)
    {
        if (value == null)
            return null;

        return dataType switch
        {
            FieldDataType.String => value.ToString(),
            FieldDataType.Integer => long.TryParse(value.ToString(), out var l) ? l : value,
            FieldDataType.Decimal => decimal.TryParse(value.ToString(), out var d) ? d : value,
            FieldDataType.DateTime => DateTime.TryParse(value.ToString(), out var dt) ? dt : value,
            FieldDataType.Boolean => bool.TryParse(value.ToString(), out var b) ? b : value,
            FieldDataType.Json => value.ToString(),
            FieldDataType.Binary => value,
            _ => value
        };
    }

    private DatabaseWriterConfig ParseConfiguration(string? configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            return new DatabaseWriterConfig();

        try
        {
            return JsonSerializer.Deserialize<DatabaseWriterConfig>(configJson, _jsonOptions) ?? new DatabaseWriterConfig();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse database writer configuration, using defaults");
            return new DatabaseWriterConfig();
        }
    }
}

/// <summary>
/// Configuration for database writer
/// Stored as JSON in ImportProfile.DestinationConfiguration
/// </summary>
public class DatabaseWriterConfig
{
    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; set; } = "";

    [JsonPropertyName("upsertKeyColumns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpsertKeyColumns { get; set; }

    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 1000;

    [JsonPropertyName("timeout")]
    public int TimeoutSeconds { get; set; } = 300;
}
