using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

/// <summary>
/// Database migration adding stdout/stderr/exit-code tracking columns to
/// ProfileExecutions, used by the Script pre/post-processing type so the
/// Execution History UI can surface what a script actually printed.
/// Run this on application startup.
/// </summary>
public class ScriptingMigration
{
    private readonly string _connectionString;

    public ScriptingMigration(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task ApplyAsync()
    {
        Log.Debug("Applying Scripting database migrations...");

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await AddProfileExecutionColumnsAsync(conn);

            Log.Debug("✓ Scripting migrations completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Scripting migrations: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    private async Task AddProfileExecutionColumnsAsync(SqliteConnection conn)
    {
        var existingColumns = (await conn.QueryAsync<string>(
            @"SELECT name FROM pragma_table_info('ProfileExecutions')
              WHERE name IN (
                'PreProcessStdout', 'PreProcessStderr', 'PreProcessExitCode',
                'PostProcessStdout', 'PostProcessStderr', 'PostProcessExitCode'
              )")).ToList();

        if (!existingColumns.Contains("PreProcessStdout"))
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessStdout TEXT NULL");

        if (!existingColumns.Contains("PreProcessStderr"))
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessStderr TEXT NULL");

        if (!existingColumns.Contains("PreProcessExitCode"))
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessExitCode INTEGER NULL");

        if (!existingColumns.Contains("PostProcessStdout"))
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessStdout TEXT NULL");

        if (!existingColumns.Contains("PostProcessStderr"))
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessStderr TEXT NULL");

        if (!existingColumns.Contains("PostProcessExitCode"))
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessExitCode INTEGER NULL");

        Log.Debug("✓ ProfileExecutions scripting columns migration completed");
    }

    public async Task<object> GetStatsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var columns = (await conn.QueryAsync<string>(
            @"SELECT name FROM pragma_table_info('ProfileExecutions')
              WHERE name IN (
                'PreProcessStdout', 'PreProcessStderr', 'PreProcessExitCode',
                'PostProcessStdout', 'PostProcessStderr', 'PostProcessExitCode'
              )")).ToList();

        return new
        {
            ColumnsAdded = columns.Count,
            MigrationComplete = columns.Count == 6
        };
    }
}
