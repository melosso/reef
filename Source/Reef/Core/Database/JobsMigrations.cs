using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

/// <summary>
/// Database migration to fix Jobs table and ensure correct schema
/// Run this on application startup
/// </summary>
public class JobsMigration
{
    private readonly string _connectionString;
    
    public JobsMigration(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    /// <summary>
    /// Apply all migrations
    /// </summary>
    public async Task ApplyAsync()
    {
        Log.Debug("Applying Jobs database migrations...");
        
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        
        await EnsureJobsTableExistsAsync(conn);
        await EnsureJobExecutionsTableExistsAsync(conn);
        await AddAutoPauseEnabledColumnAsync(conn);
        await FixExistingJobsAsync(conn);
        await AddMissingIndexesAsync(conn);

        Log.Debug("✓ Jobs migrations completed");
    }
    
    /// <summary>
    /// Ensure Jobs table exists with correct schema
    /// </summary>
    private async Task EnsureJobsTableExistsAsync(SqliteConnection conn)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS Jobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Type INTEGER NOT NULL DEFAULT 1,
                ProfileId INTEGER,
                DestinationId INTEGER,
                CustomActionJson TEXT,
                
                -- Scheduling
                ScheduleType INTEGER NOT NULL DEFAULT 0,
                CronExpression TEXT,
                IntervalMinutes INTEGER,
                StartDate TEXT, -- ISO 8601 UTC
                EndDate TEXT,   -- ISO 8601 UTC
                StartTime TEXT, -- TimeSpan as string
                EndTime TEXT,   -- TimeSpan as string
                WeekDays TEXT,  -- Comma-separated day numbers (0=Monday, 6=Sunday)
                MonthDay INTEGER, -- Day of month (1-31) for monthly schedules
                
                -- Configuration
                MaxRetries INTEGER NOT NULL DEFAULT 3,
                TimeoutMinutes INTEGER NOT NULL DEFAULT 60,
                Priority INTEGER NOT NULL DEFAULT 5,
                AllowConcurrent INTEGER NOT NULL DEFAULT 0,
                DependsOnJobIds TEXT,
                AutoPauseEnabled INTEGER NOT NULL DEFAULT 1,
                
                -- Status
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                Status INTEGER NOT NULL DEFAULT 0,
                NextRunTime TEXT,     -- ISO 8601 UTC - CRITICAL FIELD
                LastRunTime TEXT,     -- ISO 8601 UTC
                LastSuccessTime TEXT, -- ISO 8601 UTC
                LastFailureTime TEXT, -- ISO 8601 UTC
                ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
                
                -- Metadata
                Tags TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,  -- ISO 8601 UTC
                ModifiedAt TEXT,          -- ISO 8601 UTC
                CreatedBy TEXT,
                Hash TEXT NOT NULL DEFAULT '',
                
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE SET NULL,
                FOREIGN KEY (DestinationId) REFERENCES Destinations(Id) ON DELETE SET NULL
            );";
        
        await conn.ExecuteAsync(sql);
    }
    
    /// <summary>
    /// Ensure JobExecutions table exists
    /// </summary>
    private async Task EnsureJobExecutionsTableExistsAsync(SqliteConnection conn)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS JobExecutions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId INTEGER NOT NULL,
                StartedAt TEXT NOT NULL,  -- ISO 8601 UTC
                CompletedAt TEXT,         -- ISO 8601 UTC
                Status INTEGER NOT NULL DEFAULT 2,
                AttemptNumber INTEGER NOT NULL DEFAULT 1,
                OutputData TEXT,
                ErrorMessage TEXT,
                StackTrace TEXT,
                BytesProcessed INTEGER,
                RowsProcessed INTEGER,
                TriggeredBy TEXT,
                ServerNode TEXT,
                ExecutionContext TEXT,
                
                FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
            );";
        
        await conn.ExecuteAsync(sql);
    }
    
    /// <summary>
    /// Add AutoPauseEnabled column to existing tables (migration)
    /// </summary>
    private async Task AddAutoPauseEnabledColumnAsync(SqliteConnection conn)
    {
        try
        {
            // Check if column exists
            var columns = await conn.QueryAsync<dynamic>("PRAGMA table_info(Jobs)");
            var hasAutoPauseEnabled = columns.Any(c => ((string)c.name).Equals("AutoPauseEnabled", StringComparison.OrdinalIgnoreCase));

            if (!hasAutoPauseEnabled)
            {
                Log.Information("Adding AutoPauseEnabled column to Jobs table...");
                await conn.ExecuteAsync("ALTER TABLE Jobs ADD COLUMN AutoPauseEnabled INTEGER NOT NULL DEFAULT 1");
                Log.Information("✓ AutoPauseEnabled column added successfully");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add AutoPauseEnabled column");
        }
    }

    /// <summary>
    /// Fix existing jobs with corrupted NextRunTime
    /// </summary>
    private async Task FixExistingJobsAsync(SqliteConnection conn)
    {
        // Check for jobs with NextRunTime in the past
        var now = DateTime.UtcNow.ToString("O"); // ISO 8601 format
        
        var corruptedJobs = await conn.QueryAsync<dynamic>(@"
            SELECT Id, Name, NextRunTime, LastRunTime, IsEnabled, ScheduleType 
            FROM Jobs 
            WHERE IsEnabled = 1 
            AND ScheduleType != 0
            AND NextRunTime IS NOT NULL 
            AND NextRunTime < @Now",
            new { Now = now });
        
        foreach (var job in corruptedJobs)
        {
            Log.Warning("Fixing corrupted job {JobId} ({JobName}) - NextRunTime: {NextRun}, LastRunTime: {LastRun}", 
                job.Id, job.Name, job.NextRunTime, job.LastRunTime);
            
            // Set NextRunTime to NULL so it will be recalculated by the JobService
            await conn.ExecuteAsync(
                "UPDATE Jobs SET NextRunTime = NULL, ModifiedAt = @Now WHERE Id = @Id",
                new { Id = (int)job.Id, Now = now });
        }
        
        if (corruptedJobs.Any())
        {
            Log.Information("Fixed {Count} jobs with NextRunTime in the past", corruptedJobs.Count());
        }
    }
    
    /// <summary>
    /// Add missing indexes for performance
    /// </summary>
    private async Task AddMissingIndexesAsync(SqliteConnection conn)
    {
        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS IX_Jobs_NextRunTime ON Jobs(NextRunTime) WHERE IsEnabled = 1 AND NextRunTime IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS IX_Jobs_Status ON Jobs(Status)",
            "CREATE INDEX IF NOT EXISTS IX_Jobs_ProfileId ON Jobs(ProfileId)",
            "CREATE INDEX IF NOT EXISTS IX_Jobs_IsEnabled_Status ON Jobs(IsEnabled, Status)",
            "CREATE INDEX IF NOT EXISTS IX_JobExecutions_JobId ON JobExecutions(JobId)",
            "CREATE INDEX IF NOT EXISTS IX_JobExecutions_StartedAt ON JobExecutions(StartedAt DESC)"
        };
        
        foreach (var indexSql in indexes)
        {
            await conn.ExecuteAsync(indexSql);
        }
    }
    
    /// <summary>
    /// Get database statistics
    /// </summary>
    public async Task<object> GetStatsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        
        var jobCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Jobs");
        var enabledJobs = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Jobs WHERE IsEnabled = 1");
        var executionCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM JobExecutions");
        var corruptedJobs = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Jobs WHERE IsEnabled = 1 AND NextRunTime < @Now", 
            new { Now = DateTime.UtcNow.ToString("O") });
        
        return new
        {
            TotalJobs = jobCount,
            EnabledJobs = enabledJobs,
            TotalExecutions = executionCount,
            CorruptedJobs = corruptedJobs
        };
    }
}