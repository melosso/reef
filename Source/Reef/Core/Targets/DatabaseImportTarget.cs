using System.Data;
using System.Data.Common;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Reef.Core.Models;
using Reef.Core.Services;
using Serilog;

namespace Reef.Core.Targets;

/// <summary>
/// Writes imported rows to a SQL Server, MySQL, or PostgreSQL database.
/// Supports Insert, Upsert, FullReplace, and Append load strategies.
/// </summary>
public class DatabaseImportTarget : IImportTarget
{
    private readonly EncryptionService _encryptionService;

    public DatabaseImportTarget(EncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    // ── WriteBatchAsync (Insert / Upsert / Append) ──────────

    public async Task<ImportBatchResult> WriteBatchAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct = default)
    {
        if (!rows.Any()) return new ImportBatchResult();

        using var db = OpenConnection(context.TargetConnection!);
        await db.OpenAsync(ct);

        return context.LoadStrategy switch
        {
            "Upsert" => await UpsertBatchAsync(db, rows, context, ct),
            "Append" or "Insert" => await InsertBatchAsync(db, rows, context, ct),
            _ => await InsertBatchAsync(db, rows, context, ct)
        };
    }

    // ── FullReplaceAsync ─────────────────────────────────────

    public async Task<ImportBatchResult> FullReplaceAsync(
        List<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct = default)
    {
        using var db = OpenConnection(context.TargetConnection!);
        await db.OpenAsync(ct);

        var result = new ImportBatchResult();
        using var tx = await db.BeginTransactionAsync(ct);

        try
        {
            // Truncate target table
            var truncateSql = $"TRUNCATE TABLE {QuoteTable(context.TargetConnection!.Type, context.TargetTable)}";
            await db.ExecuteAsync(new CommandDefinition(truncateSql, transaction: tx,
                commandTimeout: context.CommandTimeoutSeconds, cancellationToken: ct));

            Log.Debug("FullReplace: truncated {Table}", context.TargetTable);

            // Insert all rows in batches
            foreach (var batch in rows.Chunk(context.BatchSize))
            {
                ct.ThrowIfCancellationRequested();
                var batchResult = await InsertBatchInternalAsync(db, tx, batch, context, ct);
                result.RowsInserted += batchResult.RowsInserted;
                result.RowsSkipped += batchResult.RowsSkipped;
                result.RowsFailed += batchResult.RowsFailed;
                result.Errors.AddRange(batchResult.Errors);
            }

            await tx.CommitAsync(ct);
            Log.Information("FullReplace: inserted {Count} rows into {Table}", result.RowsInserted, context.TargetTable);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // ── ApplyDeletesAsync ────────────────────────────────────

    public async Task<int> ApplyDeletesAsync(
        IReadOnlyList<string> deletedReefIds,
        string reefIdColumn,
        ImportProfile profile,
        CancellationToken ct = default)
    {
        if (!deletedReefIds.Any()) return 0;

        using var db = OpenConnection(await GetTargetConnectionAsync(profile));
        await db.OpenAsync(ct);

        int affected = 0;

        switch (profile.DeltaSyncDeleteStrategy?.ToUpperInvariant())
        {
            case "HARDDELETE":
                foreach (var batch in deletedReefIds.Chunk(500))
                {
                    var ids = string.Join(",", batch.Select(id => $"'{EscapeString(id)}'"));
                    var sql = $"DELETE FROM {profile.TargetTable} WHERE {reefIdColumn} IN ({ids})";
                    affected += await db.ExecuteAsync(new CommandDefinition(sql,
                        commandTimeout: profile.CommandTimeoutSeconds, cancellationToken: ct));
                }
                break;

            case "SOFTDELETE":
            default:
                if (!string.IsNullOrWhiteSpace(profile.DeltaSyncDeleteColumn))
                {
                    var deleteValue = profile.DeltaSyncDeleteValue ?? "1";
                    foreach (var batch in deletedReefIds.Chunk(500))
                    {
                        var ids = string.Join(",", batch.Select(id => $"'{EscapeString(id)}'"));
                        var sql = $"UPDATE {profile.TargetTable} SET {profile.DeltaSyncDeleteColumn} = {deleteValue} WHERE {reefIdColumn} IN ({ids})";
                        affected += await db.ExecuteAsync(new CommandDefinition(sql,
                            commandTimeout: profile.CommandTimeoutSeconds, cancellationToken: ct));
                    }
                }
                break;
        }

        Log.Information("ApplyDeletes: {Count} rows deleted/marked from {Table}", affected, profile.TargetTable);
        return affected;
    }

    // ── GetTableSchemaAsync ──────────────────────────────────

    public async Task<List<TargetColumnInfo>> GetTableSchemaAsync(
        Connection connection,
        string tableName,
        CancellationToken ct = default)
    {
        using var db = OpenConnection(connection);
        await db.OpenAsync(ct);

        var (schema, table) = ParseTableName(tableName);

        var sql = connection.Type.ToUpperInvariant() switch
        {
            "SQLSERVER" or "MSSQL" => SqlServerSchemaQuery(schema, table),
            "MYSQL" or "MARIADB" => MySqlSchemaQuery(table),
            "POSTGRESQL" or "POSTGRES" => PostgresSchemaQuery(schema, table),
            _ => SqlServerSchemaQuery(schema, table)
        };

        try
        {
            var rows = await db.QueryAsync<dynamic>(new CommandDefinition(sql, commandTimeout: 30, cancellationToken: ct));
            return rows.Select(r => new TargetColumnInfo
            {
                ColumnName = (string)r.ColumnName,
                DataType = (string)r.DataType,
                IsNullable = ((string?)r.IsNullable)?.Equals("YES", StringComparison.OrdinalIgnoreCase) ?? true,
                IsPrimaryKey = (long?)r.IsPrimaryKey > 0 || ((bool?)r.IsPrimaryKey == true),
                MaxLength = r.MaxLength is DBNull ? null : (int?)r.MaxLength,
                Precision = r.Precision is DBNull ? null : (int?)r.Precision,
                Scale = r.Scale is DBNull ? null : (int?)r.Scale
            }).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GetTableSchemaAsync: could not retrieve schema for {Table}", tableName);
            return new List<TargetColumnInfo>();
        }
    }

    // ── TestAsync ────────────────────────────────────────────

    public async Task<(bool Success, string? Message)> TestAsync(
        Connection connection,
        string tableName,
        CancellationToken ct = default)
    {
        try
        {
            using var db = OpenConnection(connection);
            await db.OpenAsync(ct);

            // Check if table exists
            var (schema, table) = ParseTableName(tableName);
            var exists = await TableExistsAsync(db, connection.Type, schema, table, ct);
            return exists
                ? (true, $"Connected. Table '{tableName}' exists.")
                : (false, $"Connected but table '{tableName}' not found.");
        }
        catch (Exception ex)
        {
            return (false, $"Connection test failed: {ex.Message}");
        }
    }

    // ── Insert batch ─────────────────────────────────────────

    private async Task<ImportBatchResult> InsertBatchAsync(
        DbConnection db,
        IReadOnlyList<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct)
    {
        var result = new ImportBatchResult();

        foreach (var batch in rows.Chunk(context.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            using var tx = await db.BeginTransactionAsync(ct);
            try
            {
                var batchResult = await InsertBatchInternalAsync(db, tx, batch, context, ct);
                result.RowsInserted += batchResult.RowsInserted;
                result.RowsSkipped += batchResult.RowsSkipped;
                result.RowsFailed += batchResult.RowsFailed;
                result.Errors.AddRange(batchResult.Errors);

                if (batchResult.RowsFailed > 0 && context.OnRowFailure == "Rollback")
                {
                    await tx.RollbackAsync(ct);
                    continue;
                }

                await tx.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                if (context.OnRowFailure == "Fail")
                    throw;
                Log.Warning(ex, "Insert batch failed, skipping batch: {Message}", ex.Message);
                result.RowsFailed += batch.Length;
                result.Errors.Add(new ImportRowError
                {
                    RowNumber = -1,
                    ErrorMessage = ex.Message,
                    ErrorType = ClassifyException(ex)
                });
            }
        }

        return result;
    }

    private async Task<ImportBatchResult> InsertBatchInternalAsync(
        IDbConnection db,
        IDbTransaction tx,
        Dictionary<string, object?>[] rows,
        ImportWriteContext context,
        CancellationToken ct)
    {
        var result = new ImportBatchResult();
        var connType = context.TargetConnection!.Type.ToUpperInvariant();
        var table = context.TargetTable;

        int rowNum = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            rowNum++;

            var mappedRow = ApplyMappings(row, context.ColumnMappings);
            if (!mappedRow.Any()) { result.RowsSkipped++; continue; }

            try
            {
                var cols = string.Join(", ", mappedRow.Keys.Select(c => QuoteIdentifier(connType, c)));
                var parms = string.Join(", ", mappedRow.Keys.Select(c => $"@{c}"));
                var sql = $"INSERT INTO {QuoteTable(connType, table)} ({cols}) VALUES ({parms})";

                var dynParams = new DynamicParameters();
                foreach (var kvp in mappedRow) dynParams.Add($"@{kvp.Key}", kvp.Value ?? DBNull.Value);

                await db.ExecuteAsync(new CommandDefinition(sql, dynParams, tx,
                    commandTimeout: context.CommandTimeoutSeconds, cancellationToken: ct));

                result.RowsInserted++;
            }
            catch (Exception ex) when (IsConstraintViolation(ex))
            {
                if (context.OnConstraintViolation == "SkipRow")
                {
                    result.RowsSkipped++;
                    Log.Debug("Insert: skipped constraint violation at row {RowNum}", rowNum);
                }
                else if (context.OnConstraintViolation == "Overwrite")
                {
                    // Fall through to update — try UPDATE as fallback
                    try
                    {
                        var updated = await TryUpdateAsync(db, tx, table, connType, mappedRow, context, ct);
                        if (updated) result.RowsUpdated++;
                        else { result.RowsSkipped++; }
                    }
                    catch (Exception ue)
                    {
                        HandleRowError(result, rowNum, null, ue, context.OnRowFailure);
                    }
                }
                else
                {
                    HandleRowError(result, rowNum, null, ex, context.OnRowFailure);
                    if (context.OnRowFailure == "Fail") throw;
                }
            }
            catch (Exception ex)
            {
                HandleRowError(result, rowNum, null, ex, context.OnRowFailure);
                if (context.OnRowFailure == "Fail") throw;
            }
        }

        return result;
    }

    // ── Upsert batch ─────────────────────────────────────────

    private async Task<ImportBatchResult> UpsertBatchAsync(
        DbConnection db,
        IReadOnlyList<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct)
    {
        var result = new ImportBatchResult();
        var connType = context.TargetConnection!.Type.ToUpperInvariant();

        int rowNum = 0;
        foreach (var batch in rows.Chunk(context.BatchSize))
        {
            ct.ThrowIfCancellationRequested();
            using var tx = await db.BeginTransactionAsync(ct);
            try
            {
                foreach (var row in batch)
                {
                    rowNum++;
                    var mappedRow = ApplyMappings(row, context.ColumnMappings);
                    if (!mappedRow.Any()) { result.RowsSkipped++; continue; }

                    try
                    {
                        var upsertResult = await UpsertRowAsync(db, tx, connType, context.TargetTable, mappedRow, context, ct);
                        result.RowsInserted += upsertResult.Inserted;
                        result.RowsUpdated += upsertResult.Updated;
                    }
                    catch (Exception ex)
                    {
                        HandleRowError(result, rowNum, null, ex, context.OnRowFailure);
                        if (context.OnRowFailure == "Fail") throw;
                    }
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                if (context.OnRowFailure == "Fail") throw;
            }
        }

        return result;
    }

    private async Task<(int Inserted, int Updated)> UpsertRowAsync(
        IDbConnection db,
        IDbTransaction tx,
        string connType,
        string table,
        Dictionary<string, object?> row,
        ImportWriteContext context,
        CancellationToken ct)
    {
        var keyColumns = context.UpsertKeyColumns.Any()
            ? context.UpsertKeyColumns
            : context.ColumnMappings.Where(m => m.IsKeyColumn).Select(m => m.TargetColumn).ToList();

        if (!keyColumns.Any())
        {
            // No key defined → fall back to plain insert
            var cols = string.Join(", ", row.Keys.Select(c => QuoteIdentifier(connType, c)));
            var parms = string.Join(", ", row.Keys.Select(c => $"@{c}"));
            var insertSql = $"INSERT INTO {QuoteTable(connType, table)} ({cols}) VALUES ({parms})";
            var dp = new DynamicParameters();
            foreach (var kvp in row) dp.Add($"@{kvp.Key}", kvp.Value ?? DBNull.Value);
            await db.ExecuteAsync(new CommandDefinition(insertSql, dp, tx,
                commandTimeout: context.CommandTimeoutSeconds, cancellationToken: ct));
            return (1, 0);
        }

        return connType switch
        {
            "SQLSERVER" or "MSSQL" => await SqlServerUpsertAsync(db, tx, table, row, keyColumns, context, ct),
            "MYSQL" or "MARIADB" => await MySqlUpsertAsync(db, tx, table, row, keyColumns, context, ct),
            "POSTGRESQL" or "POSTGRES" => await PostgresUpsertAsync(db, tx, table, row, keyColumns, context, ct),
            _ => await SqlServerUpsertAsync(db, tx, table, row, keyColumns, context, ct)
        };
    }

    private async Task<(int, int)> SqlServerUpsertAsync(
        IDbConnection db, IDbTransaction tx, string table,
        Dictionary<string, object?> row, List<string> keys,
        ImportWriteContext context, CancellationToken ct)
    {
        var allCols = row.Keys.ToList();
        var valueCols = allCols.Except(keys).ToList();

        var onMatch = valueCols.Any()
            ? "WHEN MATCHED THEN UPDATE SET " + string.Join(", ", valueCols.Select(c => $"t.[{c}] = s.[{c}]"))
            : "WHEN MATCHED THEN UPDATE SET t.[__noop__] = 0"; // forces MERGE to compile; real logic: do nothing

        if (!valueCols.Any()) onMatch = ""; // skip update if only key columns

        var colList = string.Join(", ", allCols.Select(c => $"[{c}]"));
        var valList = string.Join(", ", allCols.Select(c => $"s.[{c}]"));
        var onClause = string.Join(" AND ", keys.Select(k => $"t.[{k}] = s.[{k}]"));

        var sql = $@"MERGE INTO [{table}] AS t
USING (SELECT {string.Join(", ", allCols.Select(c => $"@{c} AS [{c}]"))}) AS s
ON ({onClause})
{(valueCols.Any() ? onMatch : "")}
WHEN NOT MATCHED THEN INSERT ({colList}) VALUES ({valList})
OUTPUT $action;";

        var dp = new DynamicParameters();
        foreach (var kvp in row) dp.Add($"@{kvp.Key}", kvp.Value ?? DBNull.Value);

        var action = await db.QueryFirstOrDefaultAsync<string>(
            new CommandDefinition(sql, dp, tx, commandTimeout: context.CommandTimeoutSeconds, cancellationToken: ct));

        return action == "INSERT" ? (1, 0) : (0, 1);
    }

    private async Task<(int, int)> MySqlUpsertAsync(
        IDbConnection db, IDbTransaction tx, string table,
        Dictionary<string, object?> row, List<string> keys,
        ImportWriteContext context, CancellationToken ct)
    {
        var allCols = row.Keys.ToList();
        var valueCols = allCols.Except(keys).ToList();

        var colList = string.Join(", ", allCols.Select(c => $"`{c}`"));
        var parmList = string.Join(", ", allCols.Select(c => $"@{c}"));
        var updateSet = valueCols.Any()
            ? "ON DUPLICATE KEY UPDATE " + string.Join(", ", valueCols.Select(c => $"`{c}` = VALUES(`{c}`)"))
            : "ON DUPLICATE KEY UPDATE `{keys[0]}` = `{keys[0]}`"; // no-op

        var sql = $"INSERT INTO `{table}` ({colList}) VALUES ({parmList}) {updateSet}";

        var dp = new DynamicParameters();
        foreach (var kvp in row) dp.Add($"@{kvp.Key}", kvp.Value ?? DBNull.Value);

        var rowsAffected = await db.ExecuteAsync(
            new CommandDefinition(sql, dp, tx, commandTimeout: context.CommandTimeoutSeconds, cancellationToken: ct));

        // MySQL returns 1 for insert, 2 for update
        return rowsAffected == 1 ? (1, 0) : (0, 1);
    }

    private async Task<(int, int)> PostgresUpsertAsync(
        IDbConnection db, IDbTransaction tx, string table,
        Dictionary<string, object?> row, List<string> keys,
        ImportWriteContext context, CancellationToken ct)
    {
        var allCols = row.Keys.ToList();
        var valueCols = allCols.Except(keys).ToList();

        var colList = string.Join(", ", allCols.Select(c => $"\"{c}\""));
        var parmList = string.Join(", ", allCols.Select(c => $"@{c}"));
        var keyList = string.Join(", ", keys.Select(k => $"\"{k}\""));

        string conflictAction;
        if (valueCols.Any())
        {
            conflictAction = "DO UPDATE SET " + string.Join(", ", valueCols.Select(c => $"\"{c}\" = EXCLUDED.\"{c}\""));
        }
        else
        {
            conflictAction = "DO NOTHING";
        }

        var sql = $@"INSERT INTO ""{table}"" ({colList}) VALUES ({parmList})
ON CONFLICT ({keyList}) {conflictAction}
RETURNING xmax";

        var dp = new DynamicParameters();
        foreach (var kvp in row) dp.Add($"@{kvp.Key}", kvp.Value ?? DBNull.Value);

        var xmax = await db.QueryFirstOrDefaultAsync<long?>(
            new CommandDefinition(sql, dp, tx, commandTimeout: context.CommandTimeoutSeconds, cancellationToken: ct));

        // xmax = 0 means INSERT, non-zero means UPDATE
        return xmax == 0 ? (1, 0) : (0, 1);
    }

    // ── Schema Queries ───────────────────────────────────────

    private static string SqlServerSchemaQuery(string? schema, string table) => $@"
        SELECT
            c.COLUMN_NAME AS ColumnName,
            c.DATA_TYPE AS DataType,
            c.IS_NULLABLE AS IsNullable,
            COALESCE(pk.is_pk, 0) AS IsPrimaryKey,
            c.CHARACTER_MAXIMUM_LENGTH AS MaxLength,
            c.NUMERIC_PRECISION AS Precision,
            c.NUMERIC_SCALE AS Scale
        FROM INFORMATION_SCHEMA.COLUMNS c
        LEFT JOIN (
            SELECT kcu.COLUMN_NAME, 1 AS is_pk
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
            JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
            WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
              AND kcu.TABLE_NAME = '{EscapeString(table)}'
              {(!string.IsNullOrWhiteSpace(schema) ? $"AND kcu.TABLE_SCHEMA = '{EscapeString(schema)}'" : "")}
        ) pk ON c.COLUMN_NAME = pk.COLUMN_NAME
        WHERE c.TABLE_NAME = '{EscapeString(table)}'
          {(!string.IsNullOrWhiteSpace(schema) ? $"AND c.TABLE_SCHEMA = '{EscapeString(schema)}'" : "")}
        ORDER BY c.ORDINAL_POSITION";

    private static string MySqlSchemaQuery(string table) => $@"
        SELECT
            COLUMN_NAME AS ColumnName,
            DATA_TYPE AS DataType,
            IS_NULLABLE AS IsNullable,
            IF(COLUMN_KEY = 'PRI', 1, 0) AS IsPrimaryKey,
            CHARACTER_MAXIMUM_LENGTH AS MaxLength,
            NUMERIC_PRECISION AS Precision,
            NUMERIC_SCALE AS Scale
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_NAME = '{EscapeString(table)}'
        ORDER BY ORDINAL_POSITION";

    private static string PostgresSchemaQuery(string? schema, string table) => $@"
        SELECT
            a.attname AS ColumnName,
            pg_catalog.format_type(a.atttypid, a.atttypmod) AS DataType,
            CASE WHEN a.attnotnull THEN 'NO' ELSE 'YES' END AS IsNullable,
            CASE WHEN pk.attname IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
            NULL AS MaxLength,
            NULL AS Precision,
            NULL AS Scale
        FROM pg_catalog.pg_attribute a
        JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN (
            SELECT pa.attname
            FROM pg_index pi
            JOIN pg_attribute pa ON pa.attrelid = pi.indrelid AND pa.attnum = ANY(pi.indkey)
            WHERE pi.indisprimary AND pi.indrelid = c.oid
        ) pk ON pk.attname = a.attname
        WHERE c.relname = '{EscapeString(table)}'
          AND n.nspname = '{EscapeString(schema ?? "public")}'
          AND a.attnum > 0
          AND NOT a.attisdropped
        ORDER BY a.attnum";

    // ── Helpers ──────────────────────────────────────────────

    private DbConnection OpenConnection(Connection connection)
    {
        var cs = _encryptionService.IsEncrypted(connection.ConnectionString)
            ? _encryptionService.Decrypt(connection.ConnectionString)
            : connection.ConnectionString;

        return connection.Type.ToUpperInvariant() switch
        {
            "SQLSERVER" or "MSSQL" => new SqlConnection(cs),
            "MYSQL" or "MARIADB" => new MySqlConnection(cs),
            "POSTGRESQL" or "POSTGRES" => new NpgsqlConnection(cs),
            _ => new SqlConnection(cs)
        };
    }

    // Placeholder: the actual connection object must be loaded via ConnectionService by the caller
    private Task<Connection> GetTargetConnectionAsync(ImportProfile profile)
        => throw new InvalidOperationException("Use the overload that accepts a Connection object");

    private static Dictionary<string, object?> ApplyMappings(
        Dictionary<string, object?> sourceRow,
        List<ImportColumnMapping> mappings)
    {
        if (!mappings.Any())
        {
            // No mappings defined — pass row through as-is
            return new Dictionary<string, object?>(sourceRow, StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, object?>();
        foreach (var mapping in mappings)
        {
            object? value = null;

            if (sourceRow.TryGetValue(mapping.SourceColumn, out var raw))
            {
                value = raw;
            }
            else if (mapping.DefaultValue != null)
            {
                value = mapping.DefaultValue;
            }

            if (value == null && mapping.SkipOnNull) continue;

            // Type cast
            if (!string.IsNullOrWhiteSpace(mapping.DataType) && value != null)
            {
                value = CastValue(value, mapping.DataType);
            }

            result[mapping.TargetColumn] = value;
        }

        return result;
    }

    private static object? CastValue(object? value, string dataType)
    {
        if (value == null) return null;
        var str = value.ToString()!;

        return dataType.ToLowerInvariant() switch
        {
            "int" or "integer" => int.TryParse(str, out var i) ? i : (object?)value,
            "long" or "bigint" => long.TryParse(str, out var l) ? l : (object?)value,
            "decimal" or "numeric" or "float" or "double" => decimal.TryParse(str,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (object?)value,
            "bool" or "boolean" or "bit" => bool.TryParse(str, out var b) ? b
                : str is "1" or "yes" or "true" or "y" ? true
                : str is "0" or "no" or "false" or "n" ? false
                : (object?)value,
            "datetime" or "date" => DateTime.TryParse(str, out var dt) ? dt : (object?)value,
            "string" or "varchar" or "nvarchar" or "text" => str,
            _ => value
        };
    }

    private static async Task<bool> TryUpdateAsync(
        IDbConnection db, IDbTransaction tx, string table, string connType,
        Dictionary<string, object?> row, ImportWriteContext context, CancellationToken ct)
    {
        var keys = context.UpsertKeyColumns;
        if (!keys.Any()) return false;

        var valueCols = row.Keys.Except(keys).ToList();
        if (!valueCols.Any()) return false;

        var setClause = string.Join(", ", valueCols.Select(c => $"{QuoteIdentifier(connType, c)} = @{c}"));
        var whereClause = string.Join(" AND ", keys.Select(k => $"{QuoteIdentifier(connType, k)} = @{k}"));
        var sql = $"UPDATE {QuoteTable(connType, table)} SET {setClause} WHERE {whereClause}";

        var dp = new DynamicParameters();
        foreach (var kvp in row) dp.Add($"@{kvp.Key}", kvp.Value ?? DBNull.Value);

        var affected = await db.ExecuteAsync(new CommandDefinition(sql, dp, tx,
            commandTimeout: context.CommandTimeoutSeconds, cancellationToken: ct));

        return affected > 0;
    }

    private static async Task<bool> TableExistsAsync(IDbConnection db, string connType, string? schema, string table, CancellationToken ct)
    {
        var sql = connType.ToUpperInvariant() switch
        {
            "MYSQL" or "MARIADB" => $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{EscapeString(table)}'",
            "POSTGRESQL" or "POSTGRES" =>
                $"SELECT COUNT(*) FROM pg_tables WHERE tablename = '{EscapeString(table)}' AND schemaname = '{EscapeString(schema ?? "public")}'",
            _ => $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = '{EscapeString(table)}'" +
                 (!string.IsNullOrWhiteSpace(schema) ? $" AND TABLE_SCHEMA = '{EscapeString(schema)}'" : "")
        };

        var count = await db.ExecuteScalarAsync<int>(new CommandDefinition(sql, commandTimeout: 10, cancellationToken: ct));
        return count > 0;
    }

    private static void HandleRowError(ImportBatchResult result, int rowNum, string? reefId, Exception ex, string strategy)
    {
        var error = new ImportRowError
        {
            RowNumber = rowNum,
            ReefId = reefId,
            ErrorMessage = ex.Message,
            ErrorType = ClassifyException(ex)
        };

        result.Errors.Add(error);
        result.RowsFailed++;

        Log.Warning("Row {RowNum} failed ({Type}): {Message}", rowNum, error.ErrorType, ex.Message);
    }

    private static bool IsConstraintViolation(Exception ex) => ex switch
    {
        SqlException se => se.Number is 2601 or 2627,
        MySqlException me => me.Number is 1062,
        PostgresException pe => pe.SqlState == "23505",
        _ => ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
             || ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
             || ex.Message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
    };

    private static string ClassifyException(Exception ex) => ex switch
    {
        SqlException se when se.Number is 2601 or 2627 => "Constraint",
        MySqlException me when me.Number == 1062 => "Constraint",
        PostgresException pe when pe.SqlState == "23505" => "Constraint",
        SqlException se when se.Number == -2 => "Timeout",
        MySqlException me when me.IsTransient => "Timeout",
        _ when ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => "Timeout",
        _ when ex.Message.Contains("type", StringComparison.OrdinalIgnoreCase) => "Type",
        _ => "Unknown"
    };

    private static string QuoteIdentifier(string connType, string name) => connType.ToUpperInvariant() switch
    {
        "MYSQL" or "MARIADB" => $"`{name}`",
        "POSTGRESQL" or "POSTGRES" => $"\"{name}\"",
        _ => $"[{name}]"
    };

    private static string QuoteTable(string connType, string tableName)
    {
        var (schema, table) = ParseTableName(tableName);
        var quotedTable = QuoteIdentifier(connType, table);
        return string.IsNullOrWhiteSpace(schema)
            ? quotedTable
            : $"{QuoteIdentifier(connType, schema)}.{quotedTable}";
    }

    private static (string? Schema, string Table) ParseTableName(string tableName)
    {
        var parts = tableName.Split('.', 2);
        return parts.Length == 2 ? (parts[0].Trim('[', ']', '`', '"'), parts[1].Trim('[', ']', '`', '"'))
                                 : (null, tableName.Trim('[', ']', '`', '"'));
    }

    private static string EscapeString(string s) => s.Replace("'", "''");
}
