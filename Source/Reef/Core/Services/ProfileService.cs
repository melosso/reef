using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Security;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing profile configuration and CRUD operations
/// Handles query profiles with scheduling, formatting, and destination configuration
/// </summary>
public class ProfileService
{
    private readonly string _connectionString;
    private readonly EncryptionService _encryptionService;
    private readonly HashValidator _hashValidator;

    public ProfileService(
        DatabaseConfig config,
        EncryptionService encryptionService,
        HashValidator hashValidator)
    {
        _connectionString = config.ConnectionString;
        _encryptionService = encryptionService;
        _hashValidator = hashValidator;
    }

    /// <summary>
    /// Get all profiles with connection information joined
    /// </summary>
    public async Task<List<ProfileWithConnection>> GetAllAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT 
                p.*,
                c.Name as ConnectionName,
                c.Type as ConnectionType,
                c.IsActive as ConnectionIsActive,
                pg.Name as GroupName
            FROM Profiles p
            INNER JOIN Connections c ON p.ConnectionId = c.Id
            LEFT JOIN ProfileGroups pg ON p.GroupId = pg.Id
            ORDER BY p.Name";

        var profiles = await connection.QueryAsync<ProfileWithConnection>(sql);
        return profiles.ToList();
    }

    /// <summary>
    /// Get profile by ID with reference names
    /// </summary>
    public async Task<ProfileWithConnection?> GetByIdAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT
                p.*,
                c.Name as ConnectionName,
                c.Type as ConnectionType,
                c.IsActive as ConnectionIsActive,
                pg.Name as GroupName,
                qt.Name as TemplateName,
                d.Name as OutputDestinationName,
                emt.Name as EmailTemplateName
            FROM Profiles p
            INNER JOIN Connections c ON p.ConnectionId = c.Id
            LEFT JOIN ProfileGroups pg ON p.GroupId = pg.Id
            LEFT JOIN QueryTemplates qt ON p.TemplateId = qt.Id
            LEFT JOIN Destinations d ON p.OutputDestinationId = d.Id
            LEFT JOIN QueryTemplates emt ON p.EmailTemplateId = emt.Id
            WHERE p.Id = @Id";

        return await connection.QueryFirstOrDefaultAsync<ProfileWithConnection>(sql, new { Id = id });
    }

    /// <summary>
    /// Get profile by name
    /// </summary>
    public async Task<Profile?> GetByNameAsync(string name)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM Profiles WHERE Name = @Name";
        return await connection.QueryFirstOrDefaultAsync<Profile>(sql, new { Name = name });
    }

    /// <summary>
    /// Get all profiles for a specific connection
    /// </summary>
    public async Task<List<Profile>> GetByConnectionIdAsync(int connectionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM Profiles WHERE ConnectionId = @ConnectionId ORDER BY Name";
        var profiles = await connection.QueryAsync<Profile>(sql, new { ConnectionId = connectionId });
        return profiles.ToList();
    }

    /// <summary>
    /// Get all profiles in a specific group
    /// </summary>
    public async Task<List<Profile>> GetByGroupIdAsync(int groupId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM Profiles WHERE GroupId = @GroupId ORDER BY Name";
        var profiles = await connection.QueryAsync<Profile>(sql, new { GroupId = groupId });
        return profiles.ToList();
    }

    /// <summary>
    /// Create a new profile with encryption and hash computation
    /// </summary>
    public async Task<int> CreateAsync(Profile profile, int? createdByUserId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // Validate connection exists
            var connectionExists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Connections WHERE Id = @Id", 
                new { Id = profile.ConnectionId });

            if (connectionExists == 0)
            {
                throw new InvalidOperationException($"Connection with ID {profile.ConnectionId} does not exist");
            }

            // Validate cron expression if schedule type is Cron
            if (profile.ScheduleType == "Cron" && !string.IsNullOrEmpty(profile.ScheduleCron))
            {
                ValidateCronExpression(profile.ScheduleCron);
            }

            // Validate output destination config is valid JSON if provided
            if (!string.IsNullOrEmpty(profile.OutputDestinationConfig))
            {
                try
                {
                    System.Text.Json.JsonDocument.Parse(profile.OutputDestinationConfig);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new InvalidOperationException("OutputDestinationConfig must be valid JSON", ex);
                }
            }

            // Validate pre-processing configuration
            if (!string.IsNullOrEmpty(profile.PreProcessConfig))
            {
                ValidateProcessingConfiguration(profile.PreProcessConfig, "PreProcessConfig");
            }

            // Validate post-processing configuration
            if (!string.IsNullOrEmpty(profile.PostProcessConfig))
            {
                ValidateProcessingConfiguration(profile.PostProcessConfig, "PostProcessConfig");
            }

            // Validate Multi-Output Splitting configuration
            // Note: For email profiles, split key column is optional (enables grouping by column if provided)
            // For file exports, split key column is required if split is enabled
            if (profile.SplitEnabled && !profile.IsEmailExport)
            {
                if (string.IsNullOrWhiteSpace(profile.SplitKeyColumn))
                {
                    throw new InvalidOperationException("SplitKeyColumn is required when SplitEnabled is true");
                }

                if (profile.SplitBatchSize < 1)
                {
                    throw new InvalidOperationException("SplitBatchSize must be at least 1");
                }

                // Validate filename template has required placeholders (set default if empty)
                if (string.IsNullOrWhiteSpace(profile.SplitFilenameTemplate))
                {
                    profile.SplitFilenameTemplate = "{profile}_{splitkey}_{timestamp}.{format}";
                }
            }

            // Encrypt query if it contains sensitive data (passwords, etc.)
            // For now, we'll store queries in plain text, but flag this for future enhancement
            // if (ContainsSensitiveData(profile.Query))
            // {
            //     profile.Query = _encryptionService.Encrypt(profile.Query);
            // }

            // Compute hash for tamper detection
            profile.Hash = _hashValidator.ComputeHash(profile);
            profile.CreatedBy = createdByUserId;
            profile.CreatedAt = DateTime.UtcNow;
            profile.UpdatedAt = DateTime.UtcNow;

            // Generate unique short code (P-XXXX) with collision retry
            if (string.IsNullOrEmpty(profile.Code))
            {
                profile.Code = await GenerateUniqueCodeAsync(connection);
            }

            const string sql = @"
                INSERT INTO Profiles (
                    Name, ConnectionId, GroupId, Query, ScheduleType, ScheduleCron,
                    ScheduleIntervalMinutes, OutputFormat, OutputDestinationType,
                    OutputDestinationConfig, OutputPropertiesJson, OutputDestinationId, OutputDestinationEndpointId,
                    TemplateId, TransformationOptionsJson,
                    PreProcessType, PreProcessConfig, PreProcessRollbackOnFailure,
                    PostProcessType, PostProcessConfig, PostProcessSkipOnFailure, PostProcessRollbackOnFailure, PostProcessOnZeroRows,
                    NotificationConfig,
                    IsEmailExport, EmailTemplateId, EmailRecipientsColumn, EmailRecipientsHardcoded, UseHardcodedRecipients, EmailCcColumn, EmailCcHardcoded, UseHardcodedCc, EmailSubjectColumn, EmailSubjectHardcoded, UseHardcodedSubject, EmailSuccessThresholdPercent, EmailAttachmentConfig, EmailApprovalRequired, EmailApprovalRoles,
                    DependsOnProfileIds,
                    DeltaSyncEnabled, DeltaSyncReefIdColumn, DeltaSyncHashAlgorithm,
                    DeltaSyncDuplicateStrategy, DeltaSyncNullStrategy, DeltaSyncNumericPrecision,
                    DeltaSyncTrackDeletes, DeltaSyncRetentionDays, DeltaSyncResetOnSchemaChange, DeltaSyncRemoveNonPrintable, DeltaSyncReefIdNormalization,
                    ExcludeReefIdFromOutput, ExcludeSplitKeyFromOutput,
                    SplitEnabled, SplitKeyColumn, SplitFilenameTemplate, SplitBatchSize, PostProcessPerSplit, EmailGroupBySplitKey, FilenameTemplate,
                    IsEnabled, Hash, CreatedAt, UpdatedAt, CreatedBy, Code
                ) VALUES (
                    @Name, @ConnectionId, @GroupId, @Query, @ScheduleType, @ScheduleCron,
                    @ScheduleIntervalMinutes, @OutputFormat, @OutputDestinationType,
                    @OutputDestinationConfig, @OutputPropertiesJson, @OutputDestinationId, @OutputDestinationEndpointId,
                    @TemplateId, @TransformationOptionsJson,
                    @PreProcessType, @PreProcessConfig, @PreProcessRollbackOnFailure,
                    @PostProcessType, @PostProcessConfig, @PostProcessSkipOnFailure, @PostProcessRollbackOnFailure, @PostProcessOnZeroRows,
                    @NotificationConfig,
                    @IsEmailExport, @EmailTemplateId, @EmailRecipientsColumn, @EmailRecipientsHardcoded, @UseHardcodedRecipients, @EmailCcColumn, @EmailCcHardcoded, @UseHardcodedCc, @EmailSubjectColumn, @EmailSubjectHardcoded, @UseHardcodedSubject, @EmailSuccessThresholdPercent, @EmailAttachmentConfig, @EmailApprovalRequired, @EmailApprovalRoles,
                    @DependsOnProfileIds,
                    @DeltaSyncEnabled, @DeltaSyncReefIdColumn, @DeltaSyncHashAlgorithm,
                    @DeltaSyncDuplicateStrategy, @DeltaSyncNullStrategy, @DeltaSyncNumericPrecision,
                    @DeltaSyncTrackDeletes, @DeltaSyncRetentionDays, @DeltaSyncResetOnSchemaChange, @DeltaSyncRemoveNonPrintable, @DeltaSyncReefIdNormalization,
                    @ExcludeReefIdFromOutput, @ExcludeSplitKeyFromOutput,
                    @SplitEnabled, @SplitKeyColumn, @SplitFilenameTemplate, @SplitBatchSize, @PostProcessPerSplit, @EmailGroupBySplitKey, @FilenameTemplate,
                    @IsEnabled, @Hash, @CreatedAt, @UpdatedAt, @CreatedBy, @Code
                );
                SELECT last_insert_rowid();";

            var id = await connection.ExecuteScalarAsync<int>(sql, profile);
            
            // Create scheduled task if profile has a schedule
            if (!string.IsNullOrEmpty(profile.ScheduleType) && profile.ScheduleType != "Webhook")
            {
                await CreateScheduledTask(connection, id, profile);
            }

            Log.Information("Profile created: {Name} (ID: {Id}) by {CreatedBy}", profile.Name, id, createdByUserId);
            return id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating profile {Name}", profile.Name);
            throw;
        }
    }

    /// <summary>
    /// Update an existing profile
    /// </summary>
    public async Task<bool> UpdateAsync(Profile profile)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // Validate connection exists
            var connectionExists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Connections WHERE Id = @Id", 
                new { Id = profile.ConnectionId });

            if (connectionExists == 0)
            {
                throw new InvalidOperationException($"Connection with ID {profile.ConnectionId} does not exist");
            }

            // Validate cron expression if schedule type is Cron
            if (profile.ScheduleType == "Cron" && !string.IsNullOrEmpty(profile.ScheduleCron))
            {
                ValidateCronExpression(profile.ScheduleCron);
            }

            // Validate output destination config is valid JSON if provided
            if (!string.IsNullOrEmpty(profile.OutputDestinationConfig))
            {
                try
                {
                    System.Text.Json.JsonDocument.Parse(profile.OutputDestinationConfig);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new InvalidOperationException("OutputDestinationConfig must be valid JSON", ex);
                }
            }

            // Validate pre-processing configuration
            if (!string.IsNullOrEmpty(profile.PreProcessConfig))
            {
                ValidateProcessingConfiguration(profile.PreProcessConfig, "PreProcessConfig");
            }

            // Validate post-processing configuration
            if (!string.IsNullOrEmpty(profile.PostProcessConfig))
            {
                ValidateProcessingConfiguration(profile.PostProcessConfig, "PostProcessConfig");
            }

            // Validate Multi-Output Splitting configuration
            // Note: For email profiles, split key column is optional (enables grouping by column if provided)
            // For file exports, split key column is required if split is enabled
            if (profile.SplitEnabled && !profile.IsEmailExport)
            {
                if (string.IsNullOrWhiteSpace(profile.SplitKeyColumn))
                {
                    throw new InvalidOperationException("SplitKeyColumn is required when SplitEnabled is true");
                }

                if (profile.SplitBatchSize < 1)
                {
                    throw new InvalidOperationException("SplitBatchSize must be at least 1");
                }

                // Validate filename template has required placeholders (set default if empty)
                if (string.IsNullOrWhiteSpace(profile.SplitFilenameTemplate))
                {
                    profile.SplitFilenameTemplate = "{profile}_{splitkey}_{timestamp}.{format}";
                }
            }

            // Recompute hash
            profile.Hash = _hashValidator.ComputeHash(profile);
            profile.UpdatedAt = DateTime.UtcNow;

            const string sql = @"
                UPDATE Profiles SET
                    Name = @Name,
                    ConnectionId = @ConnectionId,
                    GroupId = @GroupId,
                    Query = @Query,
                    ScheduleType = @ScheduleType,
                    ScheduleCron = @ScheduleCron,
                    ScheduleIntervalMinutes = @ScheduleIntervalMinutes,
                    OutputFormat = @OutputFormat,
                    OutputDestinationType = @OutputDestinationType,
                    OutputDestinationConfig = @OutputDestinationConfig,
                    OutputPropertiesJson = @OutputPropertiesJson,
                    OutputDestinationId = @OutputDestinationId,
                    OutputDestinationEndpointId = @OutputDestinationEndpointId,
                    TemplateId = @TemplateId,
                    TransformationOptionsJson = @TransformationOptionsJson,
                    PreProcessType = @PreProcessType,
                    PreProcessConfig = @PreProcessConfig,
                    PreProcessRollbackOnFailure = @PreProcessRollbackOnFailure,
                    PostProcessType = @PostProcessType,
                    PostProcessConfig = @PostProcessConfig,
                    PostProcessSkipOnFailure = @PostProcessSkipOnFailure,
                    PostProcessRollbackOnFailure = @PostProcessRollbackOnFailure,
                    PostProcessOnZeroRows = @PostProcessOnZeroRows,
                    NotificationConfig = @NotificationConfig,
                    IsEmailExport = @IsEmailExport,
                    EmailTemplateId = @EmailTemplateId,
                    EmailRecipientsColumn = @EmailRecipientsColumn,
                    EmailRecipientsHardcoded = @EmailRecipientsHardcoded,
                    UseHardcodedRecipients = @UseHardcodedRecipients,
                    EmailCcColumn = @EmailCcColumn,
                    EmailCcHardcoded = @EmailCcHardcoded,
                    UseHardcodedCc = @UseHardcodedCc,
                    EmailSubjectColumn = @EmailSubjectColumn,
                    EmailSubjectHardcoded = @EmailSubjectHardcoded,
                    UseHardcodedSubject = @UseHardcodedSubject,
                    EmailSuccessThresholdPercent = @EmailSuccessThresholdPercent,
                    EmailAttachmentConfig = @EmailAttachmentConfig,
                    EmailApprovalRequired = @EmailApprovalRequired,
                    EmailApprovalRoles = @EmailApprovalRoles,
                    DependsOnProfileIds = @DependsOnProfileIds,
                    DeltaSyncEnabled = @DeltaSyncEnabled,
                    DeltaSyncReefIdColumn = @DeltaSyncReefIdColumn,
                    DeltaSyncHashAlgorithm = @DeltaSyncHashAlgorithm,
                    DeltaSyncDuplicateStrategy = @DeltaSyncDuplicateStrategy,
                    DeltaSyncNullStrategy = @DeltaSyncNullStrategy,
                    DeltaSyncNumericPrecision = @DeltaSyncNumericPrecision,
                    DeltaSyncTrackDeletes = @DeltaSyncTrackDeletes,
                    DeltaSyncRetentionDays = @DeltaSyncRetentionDays,
                    DeltaSyncResetOnSchemaChange = @DeltaSyncResetOnSchemaChange,
                    DeltaSyncRemoveNonPrintable = @DeltaSyncRemoveNonPrintable,
                    DeltaSyncReefIdNormalization = @DeltaSyncReefIdNormalization,
                    ExcludeReefIdFromOutput = @ExcludeReefIdFromOutput,
                    ExcludeSplitKeyFromOutput = @ExcludeSplitKeyFromOutput,
                    SplitEnabled = @SplitEnabled,
                    SplitKeyColumn = @SplitKeyColumn,
                    SplitFilenameTemplate = @SplitFilenameTemplate,
                    FilenameTemplate = @FilenameTemplate,
                    SplitBatchSize = @SplitBatchSize,
                    PostProcessPerSplit = @PostProcessPerSplit,
                    EmailGroupBySplitKey = @EmailGroupBySplitKey,
                    IsEnabled = @IsEnabled,
                    Hash = @Hash,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            var rowsAffected = await connection.ExecuteAsync(sql, profile);

            // Update or create scheduled task
            if (!string.IsNullOrEmpty(profile.ScheduleType) && profile.ScheduleType != "Webhook")
            {
                await UpdateScheduledTask(connection, profile.Id, profile);
            }
            else
            {
                // Remove scheduled task if schedule was removed
                await connection.ExecuteAsync("DELETE FROM ScheduledTasks WHERE ProfileId = @ProfileId", 
                    new { ProfileId = profile.Id });
            }

            if (rowsAffected > 0)
            {
                Log.Information("Profile updated: {Name} (#{Id})", profile.Name, profile.Id);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating profile {Id}", profile.Id);
            throw;
        }
    }

    /// <summary>
    /// Delete a profile by ID
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // Get profile name for logging
            var profile = await GetByIdAsync(id);
            if (profile == null)
            {
                return false;
            }

            // Delete scheduled task first (if exists)
            await connection.ExecuteAsync("DELETE FROM ScheduledTasks WHERE ProfileId = @ProfileId", 
                new { ProfileId = id });

            // Delete profile dependencies
            await connection.ExecuteAsync("DELETE FROM ProfileDependencies WHERE ProfileId = @ProfileId OR DependsOnProfileId = @ProfileId", 
                new { ProfileId = id });

            // Delete the profile
            const string sql = "DELETE FROM Profiles WHERE Id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });

            if (rowsAffected > 0)
            {
                Log.Information("Profile deleted: {Name} (ID: {Id})", profile.Name, id);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting profile {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Enable a profile
    /// </summary>
    public async Task<bool> EnableAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            const string sql = "UPDATE Profiles SET IsEnabled = 1, UpdatedAt = @UpdatedAt WHERE Id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id, UpdatedAt = DateTime.UtcNow });

            if (rowsAffected > 0)
            {
                Log.Information("Profile enabled: ID {Id}", id);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enabling profile {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Disable a profile
    /// </summary>
    public async Task<bool> DisableAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            const string sql = "UPDATE Profiles SET IsEnabled = 0, UpdatedAt = @UpdatedAt WHERE Id = @Id";
            var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id, UpdatedAt = DateTime.UtcNow });

            if (rowsAffected > 0)
            {
                Log.Information("Profile disabled: ID {Id}", id);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disabling profile {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Generate a unique P-XXXX code for a new profile, retrying on collision.
    /// </summary>
    private static async Task<string> GenerateUniqueCodeAsync(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = Reef.Helpers.ProfileCodeGenerator.Generate();
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Profiles WHERE Code = @Code", new { Code = code });
            if (exists == 0) return code;
        }
        // Extremely unlikely to reach here; fall back to a GUID-based suffix
        return Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }

    /// <summary>
    /// Validate cron expression syntax using NCrontab
    /// </summary>
    private void ValidateCronExpression(string cronExpression)
    {
        try
        {
            NCrontab.CrontabSchedule.Parse(cronExpression);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid cron expression: {cronExpression}", ex);
        }
    }

    /// <summary>
    /// Validate pre-processing or post-processing configuration JSON
    /// Ensures it's valid JSON and conforms to ProcessingConfig schema
    /// </summary>
    /// <param name="configJson">JSON configuration string</param>
    /// <param name="fieldName">Field name for error messages (PreProcessConfig or PostProcessConfig)</param>
    private void ValidateProcessingConfiguration(string configJson, string fieldName)
    {
        try
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<ProcessingConfig>(configJson);
            
            if (config == null)
            {
                throw new InvalidOperationException($"{fieldName} could not be deserialized");
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.Type))
            {
                throw new InvalidOperationException($"{fieldName}: 'type' is required");
            }

            if (!config.Type.Equals("Query", StringComparison.OrdinalIgnoreCase) && 
                !config.Type.Equals("StoredProcedure", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{fieldName}: 'type' must be 'Query' or 'StoredProcedure'");
            }

            if (string.IsNullOrWhiteSpace(config.Command))
            {
                throw new InvalidOperationException($"{fieldName}: 'command' is required");
            }

            // Validate timeout is positive
            if (config.Timeout <= 0)
            {
                throw new InvalidOperationException($"{fieldName}: 'timeout' must be greater than 0");
            }

            // Validate parameters if provided
            if (config.Parameters != null)
            {
                foreach (var param in config.Parameters)
                {
                    if (string.IsNullOrWhiteSpace(param.Name))
                    {
                        throw new InvalidOperationException($"{fieldName}: parameter 'name' cannot be empty");
                    }
                    
                    if (param.Value == null)
                    {
                        throw new InvalidOperationException($"{fieldName}: parameter '{param.Name}' value cannot be null");
                    }
                }
            }

            Log.Debug("{FieldName} validation passed: Type={Type}, Command={Command}", 
                fieldName, config.Type, config.Command.Substring(0, Math.Min(50, config.Command.Length)));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"{fieldName} must be valid JSON conforming to ProcessingConfig schema", ex);
        }
    }

    /// <summary>
    /// Create scheduled task for a profile
    /// </summary>
    private async Task CreateScheduledTask(SqliteConnection connection, int profileId, Profile profile)
    {
        var nextRunAt = CalculateNextRunTime(profile);

        const string sql = @"
            INSERT INTO ScheduledTasks (ProfileId, NextRunAt, CreatedAt, UpdatedAt)
            VALUES (@ProfileId, @NextRunAt, @CreatedAt, @UpdatedAt)";

        await connection.ExecuteAsync(sql, new 
        { 
            ProfileId = profileId, 
            NextRunAt = nextRunAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Update scheduled task for a profile
    /// </summary>
    private async Task UpdateScheduledTask(SqliteConnection connection, int profileId, Profile profile)
    {
        // Check if scheduled task exists
        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ScheduledTasks WHERE ProfileId = @ProfileId", 
            new { ProfileId = profileId });

        if (exists > 0)
        {
            var nextRunAt = CalculateNextRunTime(profile);
            const string sql = @"
                UPDATE ScheduledTasks 
                SET NextRunAt = @NextRunAt, UpdatedAt = @UpdatedAt
                WHERE ProfileId = @ProfileId";

            await connection.ExecuteAsync(sql, new 
            { 
                ProfileId = profileId, 
                NextRunAt = nextRunAt,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            await CreateScheduledTask(connection, profileId, profile);
        }
    }

    /// <summary>
    /// Calculate next run time based on schedule type
    /// </summary>
    private DateTime CalculateNextRunTime(Profile profile)
    {
        var now = DateTime.UtcNow;

        if (profile.ScheduleType == "Cron" && !string.IsNullOrEmpty(profile.ScheduleCron))
        {
            try
            {
                var schedule = NCrontab.CrontabSchedule.Parse(profile.ScheduleCron);
                return schedule.GetNextOccurrence(now);
            }
            catch
            {
                // Default to 1 hour from now if cron parsing fails
                return now.AddHours(1);
            }
        }
        else if (profile.ScheduleType == "Interval" && profile.ScheduleIntervalMinutes.HasValue)
        {
            return now.AddMinutes(profile.ScheduleIntervalMinutes.Value);
        }

        // Default: 1 hour from now
        return now.AddHours(1);
    }
}

/// <summary>
/// Extended profile model with connection information
/// </summary>
public class ProfileWithConnection : Profile
{
    public string? ConnectionName { get; set; }
    public string? ConnectionType { get; set; }
    public string? GroupName { get; set; }
    public bool ConnectionIsActive { get; set; }
    public string? TemplateName { get; set; }
    public string? OutputDestinationName { get; set; }
    public string? EmailTemplateName { get; set; }
}