using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

/// <summary>
/// Database migration to add TransformationOptionsJson column to Profiles table
/// This column stores serialized ForJsonOptions/ForXmlOptions for SQL Server native transformations
/// Run this on application startup
/// </summary>
public class TransformationOptionsMigration
{
    private readonly string _connectionString;
    
    /// <summary>
    /// Initializes a new instance of the TransformationOptionsMigration class
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    public TransformationOptionsMigration(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    /// <summary>
    /// Apply transformation options migration
    /// </summary>
    public async Task ApplyAsync()
    {
        Log.Debug("Applying TransformationOptions database migration...");
        
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            
            await AddTransformationOptionsColumnAsync(conn);
            
            Log.Debug("✓ TransformationOptions migration completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply TransformationOptions migration: {ErrorMessage}", ex.Message);
            throw;
        }
    }
    
    /// <summary>
    /// Add TransformationOptionsJson column to Profiles table
    /// Column is nullable to ensure backward compatibility
    /// </summary>
    private async Task AddTransformationOptionsColumnAsync(SqliteConnection conn)
    {
        Log.Debug("Adding TransformationOptionsJson column to Profiles table...");
        
        // Check if column already exists
        var existingColumn = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT name FROM pragma_table_info('Profiles') WHERE name = 'TransformationOptionsJson'");
        
        if (existingColumn == null)
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN TransformationOptionsJson TEXT NULL");
            Log.Information("✓ Added TransformationOptionsJson column to Profiles table");
        }
        else
        {
            Log.Debug("✓ TransformationOptionsJson column already exists, skipping");
        }
    }
    
    /// <summary>
    /// Get migration statistics
    /// </summary>
    public async Task<object> GetStatsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        
        var columnExists = await conn.QueryFirstOrDefaultAsync<string>(
            "SELECT name FROM pragma_table_info('Profiles') WHERE name = 'TransformationOptionsJson'");
        
        var profilesWithOptions = 0;
        if (columnExists != null)
        {
            profilesWithOptions = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Profiles WHERE TransformationOptionsJson IS NOT NULL AND TransformationOptionsJson != ''");
        }
        
        return new
        {
            ColumnExists = columnExists != null,
            ProfilesWithTransformationOptions = profilesWithOptions,
            MigrationStatus = columnExists != null ? "Completed" : "Pending"
        };
    }
}
