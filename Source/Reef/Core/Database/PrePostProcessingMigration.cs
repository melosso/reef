using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

// To-do Remove this and Migrate to DatabaseInitialiser.cs

/// <summary>
/// Database migration to add pre-processing and post-processing capabilities to Profiles
/// Adds columns to Profiles and ProfileExecutions tables for tracking execution phases
/// Run this on application startup
/// </summary>
public class PrePostProcessingMigration
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the PrePostProcessingMigration class
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    public PrePostProcessingMigration(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Apply all pre/post-processing migrations
    /// </summary>
    public async Task ApplyAsync()
    {
        Log.Debug("Applying PrePostProcessing database migrations...");

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            await AddProfileColumnsAsync(conn);
            await AddProfileExecutionColumnsAsync(conn);
            await AddIndexesAsync(conn);

            Log.Debug("✓ PrePostProcessing migrations completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply PrePostProcessing migrations: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Add pre-processing and post-processing columns to Profiles table
    /// All columns are nullable to ensure backward compatibility
    /// </summary>
    private async Task AddProfileColumnsAsync(SqliteConnection conn)
    {
        Log.Debug("Adding pre/post-processing columns to Profiles table...");

        // Check if columns already exist
        var existingColumns = await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('Profiles') WHERE name IN ('PreProcessType', 'PreProcessConfig', 'PreProcessRollbackOnFailure', 'PostProcessSkipOnFailure', 'PostProcessRollbackOnFailure', 'PostProcessOnZeroRows')");

        var columnsList = existingColumns.ToList();

        // Add PreProcessType column
        if (!columnsList.Contains("PreProcessType"))
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN PreProcessType TEXT NULL");
            Log.Debug("✓ Added PreProcessType column");
        }

        // Add PreProcessConfig column
        if (!columnsList.Contains("PreProcessConfig"))
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN PreProcessConfig TEXT NULL");
            Log.Debug("✓ Added PreProcessConfig column");
        }

        // Add PreProcessRollbackOnFailure column (default true = 1)
        if (!columnsList.Contains("PreProcessRollbackOnFailure"))
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN PreProcessRollbackOnFailure INTEGER DEFAULT 1");
            Log.Debug("✓ Added PreProcessRollbackOnFailure column");
        }

        // Add PostProcessSkipOnFailure column (default true = 1)
        if (!columnsList.Contains("PostProcessSkipOnFailure"))
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN PostProcessSkipOnFailure INTEGER DEFAULT 1");
            Log.Debug("✓ Added PostProcessSkipOnFailure column");
        }

        // Add PostProcessRollbackOnFailure column (default false = 0)
        if (!columnsList.Contains("PostProcessRollbackOnFailure"))
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN PostProcessRollbackOnFailure INTEGER DEFAULT 0");
            Log.Debug("✓ Added PostProcessRollbackOnFailure column");
        }

        // Add PostProcessOnZeroRows column (default false = 0)
        if (!columnsList.Contains("PostProcessOnZeroRows"))
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN PostProcessOnZeroRows INTEGER DEFAULT 0");
            Log.Debug("✓ Added PostProcessOnZeroRows column");
        }

        Log.Debug("✓ Profile columns migration completed");
    }

    /// <summary>
    /// Add pre-processing and post-processing tracking columns to ProfileExecutions table
    /// Tracks status, timing, and errors for each phase
    /// </summary>
    private async Task AddProfileExecutionColumnsAsync(SqliteConnection conn)
    {
        Log.Debug("Adding pre/post-processing tracking columns to ProfileExecutions table...");

        // Check if columns already exist
        var existingColumns = await conn.QueryAsync<string>(
            @"SELECT name FROM pragma_table_info('ProfileExecutions') 
              WHERE name IN (
                'PreProcessStartedAt', 'PreProcessCompletedAt', 'PreProcessStatus', 
                'PreProcessError', 'PreProcessTimeMs',
                'PostProcessStartedAt', 'PostProcessCompletedAt', 'PostProcessStatus', 
                'PostProcessError', 'PostProcessTimeMs'
              )");

        var columnsList = existingColumns.ToList();

        // Pre-Processing tracking columns
        if (!columnsList.Contains("PreProcessStartedAt"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessStartedAt TEXT NULL");
            Log.Debug("✓ Added PreProcessStartedAt column");
        }

        if (!columnsList.Contains("PreProcessCompletedAt"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessCompletedAt TEXT NULL");
            Log.Debug("✓ Added PreProcessCompletedAt column");
        }

        if (!columnsList.Contains("PreProcessStatus"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessStatus TEXT NULL");
            Log.Debug("✓ Added PreProcessStatus column");
        }

        if (!columnsList.Contains("PreProcessError"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessError TEXT NULL");
            Log.Debug("✓ Added PreProcessError column");
        }

        if (!columnsList.Contains("PreProcessTimeMs"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PreProcessTimeMs INTEGER NULL");
            Log.Debug("✓ Added PreProcessTimeMs column");
        }

        // Post-Processing tracking columns
        if (!columnsList.Contains("PostProcessStartedAt"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessStartedAt TEXT NULL");
            Log.Debug("✓ Added PostProcessStartedAt column");
        }

        if (!columnsList.Contains("PostProcessCompletedAt"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessCompletedAt TEXT NULL");
            Log.Debug("✓ Added PostProcessCompletedAt column");
        }

        if (!columnsList.Contains("PostProcessStatus"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessStatus TEXT NULL");
            Log.Debug("✓ Added PostProcessStatus column");
        }

        if (!columnsList.Contains("PostProcessError"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessError TEXT NULL");
            Log.Debug("✓ Added PostProcessError column");
        }

        if (!columnsList.Contains("PostProcessTimeMs"))
        {
            await conn.ExecuteAsync("ALTER TABLE ProfileExecutions ADD COLUMN PostProcessTimeMs INTEGER NULL");
            Log.Debug("✓ Added PostProcessTimeMs column");
        }

        Log.Debug("✓ ProfileExecutions columns migration completed");
    }

    /// <summary>
    /// Add indexes for performance optimization
    /// Indexes on status and timestamp columns for reporting and monitoring
    /// </summary>
    private async Task AddIndexesAsync(SqliteConnection conn)
    {
        Log.Debug("Adding indexes for pre/post-processing columns...");

        var indexes = new[]
        {
            // Profiles table indexes
            "CREATE INDEX IF NOT EXISTS IX_Profiles_PreProcessType ON Profiles(PreProcessType) WHERE PreProcessType IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS IX_Profiles_PostProcessType ON Profiles(PostProcessType) WHERE PostProcessType IS NOT NULL",
            
            // ProfileExecutions table indexes for phase status tracking
            "CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_PreProcessStatus ON ProfileExecutions(PreProcessStatus) WHERE PreProcessStatus IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_PostProcessStatus ON ProfileExecutions(PostProcessStatus) WHERE PostProcessStatus IS NOT NULL",
            
            // Composite indexes for execution timeline analysis
            "CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_PreProcessTiming ON ProfileExecutions(PreProcessStartedAt, PreProcessCompletedAt) WHERE PreProcessStartedAt IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_PostProcessTiming ON ProfileExecutions(PostProcessStartedAt, PostProcessCompletedAt) WHERE PostProcessStartedAt IS NOT NULL"
        };

        foreach (var indexSql in indexes)
        {
            try
            {
                await conn.ExecuteAsync(indexSql);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create index, continuing: {IndexSql}", indexSql);
            }
        }

        Log.Debug("✓ Indexes created successfully");
    }

    /// <summary>
    /// Get migration statistics for verification
    /// </summary>
    /// <returns>Object containing column counts and migration status</returns>
    public async Task<object> GetStatsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Count profiles with pre-processing configured
        var profilesWithPreProcess = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Profiles WHERE PreProcessType IS NOT NULL");

        // Count profiles with post-processing configured
        var profilesWithPostProcess = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Profiles WHERE PostProcessType IS NOT NULL");

        // Count executions with pre-processing tracked
        var executionsWithPreProcess = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ProfileExecutions WHERE PreProcessStatus IS NOT NULL");

        // Count executions with post-processing tracked
        var executionsWithPostProcess = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ProfileExecutions WHERE PostProcessStatus IS NOT NULL");

        // Verify all Profile columns exist
        var profileColumns = await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('Profiles') WHERE name IN ('PreProcessType', 'PreProcessConfig', 'PreProcessRollbackOnFailure', 'PostProcessSkipOnFailure', 'PostProcessRollbackOnFailure', 'PostProcessOnZeroRows')");

        // Verify all ProfileExecution columns exist
        var executionColumns = await conn.QueryAsync<string>(
            @"SELECT name FROM pragma_table_info('ProfileExecutions') 
              WHERE name IN (
                'PreProcessStartedAt', 'PreProcessCompletedAt', 'PreProcessStatus', 
                'PreProcessError', 'PreProcessTimeMs',
                'PostProcessStartedAt', 'PostProcessCompletedAt', 'PostProcessStatus', 
                'PostProcessError', 'PostProcessTimeMs'
              )");

        return new
        {
            ProfileColumnsAdded = profileColumns.Count(),
            ExecutionColumnsAdded = executionColumns.Count(),
            ProfilesWithPreProcessing = profilesWithPreProcess,
            ProfilesWithPostProcessing = profilesWithPostProcess,
            ExecutionsWithPreProcessing = executionsWithPreProcess,
            ExecutionsWithPostProcessing = executionsWithPostProcess,
            MigrationComplete = profileColumns.Count() == 6 && executionColumns.Count() == 10
        };
    }
}
