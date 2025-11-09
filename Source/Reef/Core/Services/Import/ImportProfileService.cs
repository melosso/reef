using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using Reef.Core.Security;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import;

/// <summary>
/// Service for managing import profiles (CRUD operations)
/// Follows the same pattern as ProfileService for consistency
/// </summary>
public class ImportProfileService : IImportProfileService
{
    private readonly string _connectionString;
    private readonly HashValidator _hashValidator;
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ImportProfileService));

    public ImportProfileService(DatabaseConfig config, HashValidator hashValidator)
    {
        _connectionString = config.ConnectionString;
        _hashValidator = hashValidator;
    }

    /// <summary>
    /// Creates a new import profile
    /// </summary>
    public async Task<int> CreateAsync(ImportProfile profile, string createdBy, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new ArgumentException("Profile name is required");
            if (profile.SourceType == DataSourceType.Unknown)
                throw new ArgumentException("Source type is required");
            if (string.IsNullOrWhiteSpace(profile.SourceUri))
                throw new ArgumentException("Source URI is required");
            if (profile.DestinationType == ImportDestinationType.Unknown)
                throw new ArgumentException("Destination type is required");
            if (string.IsNullOrWhiteSpace(profile.DestinationUri))
                throw new ArgumentException("Destination URI is required");

            // Compute hash for integrity
            profile.Hash = _hashValidator.ComputeHash(profile);
            profile.CreatedBy = createdBy;
            profile.CreatedAt = DateTime.UtcNow;
            profile.UpdatedAt = DateTime.UtcNow;

            const string sql = @"
                INSERT INTO ImportProfiles (
                    Name, Description,
                    SourceConnectionId, SourceType, SourceUri, SourceConfiguration,
                    DestinationConnectionId, DestinationType, DestinationUri, DestinationConfiguration,
                    FieldMappingsJson, ValidationRulesJson,
                    ScheduleType, ScheduleCron, ScheduleIntervalMinutes, WebhookSecret,
                    PreProcessTemplate, PostProcessTemplate,
                    ErrorStrategy, MaxRetries, RetryDelaySeconds,
                    DeltaSyncMode, DeltaSyncKeyColumns, TrackChanges,
                    LogDetailedErrors, ExecutionHistoryRetentionDays,
                    IsEnabled, Hash, CreatedAt, UpdatedAt, CreatedBy, LastExecutedAt
                ) VALUES (
                    @Name, @Description,
                    @SourceConnectionId, @SourceType, @SourceUri, @SourceConfiguration,
                    @DestinationConnectionId, @DestinationType, @DestinationUri, @DestinationConfiguration,
                    @FieldMappingsJson, @ValidationRulesJson,
                    @ScheduleType, @ScheduleCron, @ScheduleIntervalMinutes, @WebhookSecret,
                    @PreProcessTemplate, @PostProcessTemplate,
                    @ErrorStrategy, @MaxRetries, @RetryDelaySeconds,
                    @DeltaSyncMode, @DeltaSyncKeyColumns, @TrackChanges,
                    @LogDetailedErrors, @ExecutionHistoryRetentionDays,
                    @IsEnabled, @Hash, @CreatedAt, @UpdatedAt, @CreatedBy, @LastExecutedAt
                );
                SELECT last_insert_rowid();";

            var id = await connection.ExecuteScalarAsync<int>(sql, new
            {
                profile.Name,
                profile.Description,
                profile.SourceConnectionId,
                SourceType = profile.SourceType.ToString(),
                profile.SourceUri,
                profile.SourceConfiguration,
                profile.DestinationConnectionId,
                DestinationType = profile.DestinationType.ToString(),
                profile.DestinationUri,
                profile.DestinationConfiguration,
                profile.FieldMappingsJson,
                profile.ValidationRulesJson,
                profile.ScheduleType,
                profile.ScheduleCron,
                profile.ScheduleIntervalMinutes,
                profile.WebhookSecret,
                profile.PreProcessTemplate,
                profile.PostProcessTemplate,
                ErrorStrategy = profile.ErrorStrategy.ToString(),
                profile.MaxRetries,
                profile.RetryDelaySeconds,
                DeltaSyncMode = profile.DeltaSyncMode.ToString(),
                profile.DeltaSyncKeyColumns,
                TrackChanges = profile.TrackChanges ? 1 : 0,
                LogDetailedErrors = profile.LogDetailedErrors ? 1 : 0,
                profile.ExecutionHistoryRetentionDays,
                IsEnabled = profile.IsEnabled ? 1 : 0,
                profile.Hash,
                profile.CreatedAt,
                profile.UpdatedAt,
                profile.CreatedBy,
                profile.LastExecutedAt
            });

            Log.Information("Import profile created: {Name} (ID: {Id}) by {CreatedBy}", profile.Name, id, createdBy);
            return id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating import profile {Name}", profile.Name);
            throw;
        }
    }

    /// <summary>
    /// Gets an import profile by ID
    /// </summary>
    public async Task<ImportProfile?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT
                Id, Name, Description,
                SourceConnectionId, SourceType, SourceUri, SourceConfiguration,
                DestinationConnectionId, DestinationType, DestinationUri, DestinationConfiguration,
                FieldMappingsJson, ValidationRulesJson,
                ScheduleType, ScheduleCron, ScheduleIntervalMinutes, WebhookSecret,
                PreProcessTemplate, PostProcessTemplate,
                ErrorStrategy, MaxRetries, RetryDelaySeconds,
                DeltaSyncMode, DeltaSyncKeyColumns, TrackChanges,
                LogDetailedErrors, ExecutionHistoryRetentionDays,
                IsEnabled, Hash, CreatedAt, UpdatedAt, CreatedBy, LastExecutedAt
            FROM ImportProfiles
            WHERE Id = @Id";

        var row = await connection.QueryFirstOrDefaultAsync<dynamic>(sql, new { Id = id });
        if (row == null)
            return null;

        return MapToProfile(row);
    }

    /// <summary>
    /// Gets all import profiles
    /// </summary>
    public async Task<List<ImportProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT
                Id, Name, Description,
                SourceConnectionId, SourceType, SourceUri, SourceConfiguration,
                DestinationConnectionId, DestinationType, DestinationUri, DestinationConfiguration,
                FieldMappingsJson, ValidationRulesJson,
                ScheduleType, ScheduleCron, ScheduleIntervalMinutes, WebhookSecret,
                PreProcessTemplate, PostProcessTemplate,
                ErrorStrategy, MaxRetries, RetryDelaySeconds,
                DeltaSyncMode, DeltaSyncKeyColumns, TrackChanges,
                LogDetailedErrors, ExecutionHistoryRetentionDays,
                IsEnabled, Hash, CreatedAt, UpdatedAt, CreatedBy, LastExecutedAt
            FROM ImportProfiles
            ORDER BY Name";

        var rows = await connection.QueryAsync<dynamic>(sql);
        return rows.Select(MapToProfile).ToList();
    }

    /// <summary>
    /// Updates an import profile
    /// </summary>
    public async Task<bool> UpdateAsync(ImportProfile profile, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            // Recompute hash
            profile.Hash = _hashValidator.ComputeHash(profile);
            profile.UpdatedAt = DateTime.UtcNow;

            const string sql = @"
                UPDATE ImportProfiles SET
                    Name = @Name,
                    Description = @Description,
                    SourceConnectionId = @SourceConnectionId,
                    SourceType = @SourceType,
                    SourceUri = @SourceUri,
                    SourceConfiguration = @SourceConfiguration,
                    DestinationConnectionId = @DestinationConnectionId,
                    DestinationType = @DestinationType,
                    DestinationUri = @DestinationUri,
                    DestinationConfiguration = @DestinationConfiguration,
                    FieldMappingsJson = @FieldMappingsJson,
                    ValidationRulesJson = @ValidationRulesJson,
                    ScheduleType = @ScheduleType,
                    ScheduleCron = @ScheduleCron,
                    ScheduleIntervalMinutes = @ScheduleIntervalMinutes,
                    WebhookSecret = @WebhookSecret,
                    PreProcessTemplate = @PreProcessTemplate,
                    PostProcessTemplate = @PostProcessTemplate,
                    ErrorStrategy = @ErrorStrategy,
                    MaxRetries = @MaxRetries,
                    RetryDelaySeconds = @RetryDelaySeconds,
                    DeltaSyncMode = @DeltaSyncMode,
                    DeltaSyncKeyColumns = @DeltaSyncKeyColumns,
                    TrackChanges = @TrackChanges,
                    LogDetailedErrors = @LogDetailedErrors,
                    ExecutionHistoryRetentionDays = @ExecutionHistoryRetentionDays,
                    IsEnabled = @IsEnabled,
                    Hash = @Hash,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                profile.Id,
                profile.Name,
                profile.Description,
                profile.SourceConnectionId,
                SourceType = profile.SourceType.ToString(),
                profile.SourceUri,
                profile.SourceConfiguration,
                profile.DestinationConnectionId,
                DestinationType = profile.DestinationType.ToString(),
                profile.DestinationUri,
                profile.DestinationConfiguration,
                profile.FieldMappingsJson,
                profile.ValidationRulesJson,
                profile.ScheduleType,
                profile.ScheduleCron,
                profile.ScheduleIntervalMinutes,
                profile.WebhookSecret,
                profile.PreProcessTemplate,
                profile.PostProcessTemplate,
                ErrorStrategy = profile.ErrorStrategy.ToString(),
                profile.MaxRetries,
                profile.RetryDelaySeconds,
                DeltaSyncMode = profile.DeltaSyncMode.ToString(),
                profile.DeltaSyncKeyColumns,
                TrackChanges = profile.TrackChanges ? 1 : 0,
                LogDetailedErrors = profile.LogDetailedErrors ? 1 : 0,
                profile.ExecutionHistoryRetentionDays,
                IsEnabled = profile.IsEnabled ? 1 : 0,
                profile.Hash,
                profile.UpdatedAt
            });

            if (rowsAffected > 0)
                Log.Information("Import profile updated: {Name} (ID: {Id})", profile.Name, profile.Id);

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating import profile {Id}", profile.Id);
            throw;
        }
    }

    /// <summary>
    /// Deletes an import profile
    /// </summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            const string sql = "DELETE FROM ImportProfiles WHERE Id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });

            if (rowsAffected > 0)
                Log.Information("Import profile deleted: ID {Id}", id);

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting import profile {Id}", id);
            throw;
        }
    }

    // ===== Helper Methods =====

    private ImportProfile MapToProfile(dynamic row)
    {
        return new ImportProfile
        {
            Id = row.Id,
            Name = row.Name,
            Description = row.Description,
            SourceConnectionId = row.SourceConnectionId,
            SourceType = Enum.Parse<DataSourceType>(row.SourceType),
            SourceUri = row.SourceUri,
            SourceConfiguration = row.SourceConfiguration,
            DestinationConnectionId = row.DestinationConnectionId,
            DestinationType = Enum.Parse<ImportDestinationType>(row.DestinationType),
            DestinationUri = row.DestinationUri,
            DestinationConfiguration = row.DestinationConfiguration,
            FieldMappingsJson = row.FieldMappingsJson,
            ValidationRulesJson = row.ValidationRulesJson,
            ScheduleType = row.ScheduleType,
            ScheduleCron = row.ScheduleCron,
            ScheduleIntervalMinutes = row.ScheduleIntervalMinutes,
            WebhookSecret = row.WebhookSecret,
            PreProcessTemplate = row.PreProcessTemplate,
            PostProcessTemplate = row.PostProcessTemplate,
            ErrorStrategy = Enum.Parse<ImportErrorStrategy>(row.ErrorStrategy),
            MaxRetries = row.MaxRetries,
            RetryDelaySeconds = row.RetryDelaySeconds,
            DeltaSyncMode = Enum.Parse<DeltaSyncMode>(row.DeltaSyncMode),
            DeltaSyncKeyColumns = row.DeltaSyncKeyColumns,
            TrackChanges = row.TrackChanges == 1,
            LogDetailedErrors = row.LogDetailedErrors == 1,
            ExecutionHistoryRetentionDays = row.ExecutionHistoryRetentionDays,
            IsEnabled = row.IsEnabled == 1,
            Hash = row.Hash,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            CreatedBy = row.CreatedBy,
            LastExecutedAt = row.LastExecutedAt
        };
    }
}
