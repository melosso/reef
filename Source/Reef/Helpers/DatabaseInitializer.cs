
using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;
using Reef.Core.Services;

namespace Reef.Core.Data;


public class DatabaseInitializer
{
    private readonly string _connectionString;
    private readonly EncryptionService? _encryptionService;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<DatabaseInitializer>();

    public DatabaseInitializer(string connectionString, EncryptionService? encryptionService = null)
    {
        _connectionString = connectionString;
        _encryptionService = encryptionService;
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Create all tables
        await CreateConnectionsTableAsync(connection);
        await CreateProfileGroupsTableAsync(connection);
        await CreateProfilesTableAsync(connection);
        await CreateProfileParametersTableAsync(connection);
        await CreateProfileExecutionsTableAsync(connection);
        await CreateApiKeysTableAsync(connection);
        await CreateWebhookTriggersTableAsync(connection);
        await CreateUsersTableAsync(connection);
        await CreateAuditLogTableAsync(connection);
        await CreateProfileDependenciesTableAsync(connection);
        await CreateScheduledTasksTableAsync(connection);

        // Reef Tables
        await CreateDestinationsTableAsync(connection);
        await CreateQueryTemplatesTableAsync(connection);
        await CreateProfileTransformationsTableAsync(connection);
        await CreateJobsTableAsync(connection);
        await CreateJobExecutionsTableAsync(connection);
        await CreateJobDependenciesTableAsync(connection);

        // Delta Sync Table
        await CreateDeltaSyncStateTableAsync(connection);

        // Notification Settings Table
        await CreateNotificationSettingsTableAsync(connection);

        // Notification Email Templates Table
        await CreateNotificationEmailTemplateTableAsync(connection);

        // Email Approval Workflow Table
        await CreatePendingEmailApprovalsTableAsync(connection);

        // Application Startup Tracking
        await CreateApplicationStartupTableAsync(connection);

        // Apply schema migrations for existing databases
        await ApplyMigrationsAsync(connection);

        // Create default admin user if not exists
        await CreateDefaultUser(connection);
    }

    // Add missing table: ProfileDependencies
    private async Task CreateProfileDependenciesTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ProfileDependencies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                DependsOnProfileId INTEGER NOT NULL,
                Type INTEGER NOT NULL DEFAULT 1,
                Condition TEXT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (DependsOnProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                UNIQUE(ProfileId, DependsOnProfileId)
            );

            CREATE INDEX IF NOT EXISTS idx_profiledeps_profile ON ProfileDependencies(ProfileId);
            CREATE INDEX IF NOT EXISTS idx_profiledeps_parent ON ProfileDependencies(DependsOnProfileId);
        ";
        await connection.ExecuteAsync(sql);
    }

    // Add missing table: ScheduledTasks
    private async Task CreateScheduledTasksTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ScheduledTasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                NextRunAt TEXT NOT NULL,
                LastRunAt TEXT NULL,
                IsRunning INTEGER NOT NULL DEFAULT 0,
                FailureCount INTEGER NOT NULL DEFAULT 0,
                LastError TEXT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_ScheduledTasks_ProfileId ON ScheduledTasks(ProfileId);
            CREATE INDEX IF NOT EXISTS IX_ScheduledTasks_NextRunAt ON ScheduledTasks(NextRunAt);
            CREATE INDEX IF NOT EXISTS IX_ScheduledTasks_IsRunning ON ScheduledTasks(IsRunning);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateConnectionsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS Connections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL DEFAULT '',
                Type TEXT NOT NULL,
                ConnectionString TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Tags TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NULL,
                Hash TEXT NOT NULL,
                UpdatedAt TEXT NULL,
                CreatedBy TEXT NULL,
                LastTestedAt TEXT NULL,
                LastTestResult TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Connections_Name ON Connections(Name);
            CREATE INDEX IF NOT EXISTS IX_Connections_Type ON Connections(Type);
            CREATE INDEX IF NOT EXISTS IX_Connections_IsActive ON Connections(IsActive);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateProfileGroupsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ProfileGroups (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ParentId INTEGER NULL,
                Description TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL,
                ModifiedAt TEXT NULL,
                FOREIGN KEY (ParentId) REFERENCES ProfileGroups(Id) ON DELETE CASCADE,
                UNIQUE(Name, ParentId)
            );

            CREATE INDEX IF NOT EXISTS idx_profilegroups_parent ON ProfileGroups(ParentId);
            CREATE INDEX IF NOT EXISTS idx_profilegroups_sort ON ProfileGroups(SortOrder);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateProfilesTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS Profiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL DEFAULT '',
                ConnectionId INTEGER NOT NULL,
                GroupId INTEGER NULL,
                Query TEXT NOT NULL,

                -- Scheduling
                ScheduleType TEXT NULL,
                ScheduleCron TEXT NULL,
                ScheduleIntervalMinutes INTEGER NULL,

                -- Output
                OutputFormat TEXT NOT NULL DEFAULT 'JSON',
                OutputDestinationType TEXT NULL,
                OutputDestinationConfig TEXT NULL,
                OutputPropertiesJson TEXT NULL,
                OutputDestinationId INTEGER NULL,
                TemplateId INTEGER NULL,
                TransformationOptionsJson TEXT NULL,

                -- Pre-Processing
                PreProcessType TEXT NULL,
                PreProcessConfig TEXT NULL,
                PreProcessRollbackOnFailure INTEGER DEFAULT 1,

                -- Post-Processing
                PostProcessType TEXT NULL,
                PostProcessConfig TEXT NULL,
                PostProcessSkipOnFailure INTEGER DEFAULT 1,
                PostProcessRollbackOnFailure INTEGER DEFAULT 0,

                -- Delta Sync Configuration
                DeltaSyncEnabled INTEGER NOT NULL DEFAULT 0,
                DeltaSyncReefIdColumn TEXT NULL,
                DeltaSyncHashAlgorithm TEXT NULL DEFAULT 'SHA256',
                DeltaSyncTrackDeletes INTEGER NOT NULL DEFAULT 0,
                DeltaSyncRetentionDays INTEGER NULL,
                DeltaSyncDuplicateStrategy TEXT NULL DEFAULT 'Strict',
                DeltaSyncNullStrategy TEXT NULL DEFAULT 'Strict',
                DeltaSyncResetOnSchemaChange INTEGER NOT NULL DEFAULT 0,
                DeltaSyncNumericPrecision INTEGER NOT NULL DEFAULT 6,
                DeltaSyncRemoveNonPrintable INTEGER NOT NULL DEFAULT 0,
                DeltaSyncReefIdNormalization TEXT NULL DEFAULT 'Trim',

                -- Advanced Output Options
                ExcludeReefIdFromOutput INTEGER NOT NULL DEFAULT 1,
                ExcludeSplitKeyFromOutput INTEGER NOT NULL DEFAULT 1,
                FilenameTemplate TEXT NULL,

                -- Multi-Output Splitting
                SplitEnabled INTEGER NOT NULL DEFAULT 0,
                SplitKeyColumn TEXT NULL,
                SplitFilenameTemplate TEXT NULL DEFAULT '{profile}_{splitkey}_{timestamp}.{format}',
                SplitBatchSize INTEGER NOT NULL DEFAULT 1,
                PostProcessPerSplit INTEGER NOT NULL DEFAULT 0,

                -- Dependencies
                DependsOnProfileIds TEXT NULL,

                -- Notification
                NotificationConfig TEXT NULL,

                -- Email Export Configuration
                IsEmailExport INTEGER NOT NULL DEFAULT 0,
                EmailTemplateId INTEGER NULL,
                EmailRecipientsColumn TEXT NULL,
                EmailRecipientsHardcoded TEXT NULL,
                UseHardcodedRecipients INTEGER NOT NULL DEFAULT 0,
                EmailCcColumn TEXT NULL,
                EmailCcHardcoded TEXT NULL,
                UseHardcodedCc INTEGER NOT NULL DEFAULT 0,
                EmailSubjectColumn TEXT NULL,
                EmailSubjectHardcoded TEXT NULL,
                UseHardcodedSubject INTEGER NOT NULL DEFAULT 0,
                EmailSuccessThresholdPercent INTEGER NOT NULL DEFAULT 60,
                EmailAttachmentConfig TEXT NULL,

                -- Email Approval Workflow Configuration
                EmailApprovalRequired INTEGER NOT NULL DEFAULT 0,
                EmailApprovalRoles TEXT NULL,

                -- Status
                IsEnabled INTEGER NOT NULL DEFAULT 1,

                -- Metadata
                Tags TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NULL,
                ModifiedAt TEXT NULL,
                CreatedBy TEXT NULL,
                Hash TEXT NOT NULL,
                LastExecutedAt TEXT NULL,

                FOREIGN KEY (ConnectionId) REFERENCES Connections(Id) ON DELETE RESTRICT,
                FOREIGN KEY (GroupId) REFERENCES ProfileGroups(Id) ON DELETE SET NULL,
                FOREIGN KEY (OutputDestinationId) REFERENCES Destinations(Id) ON DELETE SET NULL,
                FOREIGN KEY (TemplateId) REFERENCES QueryTemplates(Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS idx_profiles_connection ON Profiles(ConnectionId);
            CREATE INDEX IF NOT EXISTS idx_profiles_group ON Profiles(GroupId);
            CREATE INDEX IF NOT EXISTS idx_profiles_enabled ON Profiles(IsEnabled);
            CREATE INDEX IF NOT EXISTS idx_profiles_destination ON Profiles(OutputDestinationId);
            CREATE INDEX IF NOT EXISTS idx_profiles_template ON Profiles(TemplateId);
            CREATE INDEX IF NOT EXISTS IX_Profiles_PreProcessType ON Profiles(PreProcessType) WHERE PreProcessType IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_Profiles_PostProcessType ON Profiles(PostProcessType) WHERE PostProcessType IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_Profiles_DeltaSyncEnabled ON Profiles(DeltaSyncEnabled) WHERE DeltaSyncEnabled = 1;
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateProfileParametersTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ProfileParameters (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Type TEXT NOT NULL,
                DefaultValue TEXT NULL,
                IsRequired INTEGER NOT NULL DEFAULT 0,
                Description TEXT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                UNIQUE(ProfileId, Name)
            );

            CREATE INDEX IF NOT EXISTS idx_profileparams_profile ON ProfileParameters(ProfileId);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateProfileExecutionsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ProfileExecutions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                JobId INTEGER NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                Status TEXT NOT NULL,
                RowCount INTEGER NULL,
                OutputPath TEXT NULL,
                FileSizeBytes INTEGER NULL,
                OutputSize INTEGER NULL,
                ErrorMessage TEXT NULL,
                OutputMessage TEXT NULL,
                StackTrace TEXT NULL,
                ExecutionTimeMs INTEGER NULL,
                TriggeredBy TEXT NULL,
                ParametersJson TEXT NULL,

                -- Pre-Processing Tracking
                PreProcessStartedAt TEXT NULL,
                PreProcessCompletedAt TEXT NULL,
                PreProcessStatus TEXT NULL,
                PreProcessError TEXT NULL,
                PreProcessTimeMs INTEGER NULL,

                -- Post-Processing Tracking
                PostProcessStartedAt TEXT NULL,
                PostProcessCompletedAt TEXT NULL,
                PostProcessStatus TEXT NULL,
                PostProcessError TEXT NULL,
                PostProcessTimeMs INTEGER NULL,

                -- Email Approval Workflow Tracking
                ApprovalStatus TEXT NULL,
                PendingEmailApprovalId INTEGER NULL,
                ApprovedAt TEXT NULL,

                -- Delta Sync Metrics
                DeltaSyncNewRows INTEGER NULL,
                DeltaSyncChangedRows INTEGER NULL,
                DeltaSyncDeletedRows INTEGER NULL,
                DeltaSyncUnchangedRows INTEGER NULL,
                DeltaSyncTotalHashedRows INTEGER NULL,

                -- Split Tracking
                WasSplit INTEGER NOT NULL DEFAULT 0,
                SplitCount INTEGER NULL,
                SplitSuccessCount INTEGER NULL,
                SplitFailureCount INTEGER NULL,

                -- Output Format
                OutputFormat TEXT NULL,

                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_profileexec_profile ON ProfileExecutions(ProfileId);
            CREATE INDEX IF NOT EXISTS idx_profileexec_started ON ProfileExecutions(StartedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_profileexec_status ON ProfileExecutions(Status);
            CREATE INDEX IF NOT EXISTS idx_profileexec_jobid ON ProfileExecutions(JobId);
            CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_PreProcessStatus ON ProfileExecutions(PreProcessStatus) WHERE PreProcessStatus IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_PostProcessStatus ON ProfileExecutions(PostProcessStatus) WHERE PostProcessStatus IS NOT NULL;
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateApiKeysTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ApiKeys (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                KeyHash TEXT NOT NULL UNIQUE,
                Permissions TEXT NOT NULL,
                ExpiresAt TEXT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                LastUsedAt TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_apikeys_hash ON ApiKeys(KeyHash);
            CREATE INDEX IF NOT EXISTS idx_apikeys_active ON ApiKeys(IsActive);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateWebhookTriggersTableAsync(SqliteConnection connection)
    {
        // Check if table exists first
        var tableExistsSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='WebhookTriggers';";
        var tableExists = await connection.QueryFirstOrDefaultAsync<string>(tableExistsSql);
        
        if (tableExists == null)
        {
            // Create new table with full schema
            var sql = @"
                CREATE TABLE WebhookTriggers (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProfileId INTEGER NULL,
                    JobId INTEGER NULL,
                    Token TEXT NOT NULL UNIQUE,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    LastTriggeredAt TEXT NULL,
                    TriggerCount INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                    FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
                );

                CREATE INDEX idx_webhooks_token ON WebhookTriggers(Token);
                CREATE INDEX idx_webhooks_profile ON WebhookTriggers(ProfileId);
                CREATE INDEX idx_webhooks_job ON WebhookTriggers(JobId);
            ";
            await connection.ExecuteAsync(sql);
            Log.Debug("Created WebhookTriggers table with JobId support");
        }
        else
        {
            // Table exists, check if it has JobId column
            var checkColumnSql = "PRAGMA table_info(WebhookTriggers);";
            var columns = await connection.QueryAsync<dynamic>(checkColumnSql);
            var hasJobId = columns.Any(c => ((string)c.name).Equals("JobId", StringComparison.OrdinalIgnoreCase));
            
            if (!hasJobId)
            {
                Log.Information("Migrating WebhookTriggers table to add JobId column");
                var migrationSql = @"
                    -- Create new table with updated schema (without CHECK constraint to avoid issues)
                    CREATE TABLE WebhookTriggers_new (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ProfileId INTEGER NULL,
                        JobId INTEGER NULL,
                        Token TEXT NOT NULL UNIQUE,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        CreatedAt TEXT NOT NULL,
                        LastTriggeredAt TEXT NULL,
                        TriggerCount INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                        FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
                    );
                    
                    -- Copy existing data
                    INSERT INTO WebhookTriggers_new (Id, ProfileId, Token, IsActive, CreatedAt, LastTriggeredAt, TriggerCount)
                    SELECT Id, ProfileId, Token, IsActive, CreatedAt, LastTriggeredAt, TriggerCount
                    FROM WebhookTriggers;
                    
                    -- Drop old table
                    DROP TABLE WebhookTriggers;
                    
                    -- Rename new table
                    ALTER TABLE WebhookTriggers_new RENAME TO WebhookTriggers;
                    
                    -- Recreate indexes
                    CREATE INDEX idx_webhooks_token ON WebhookTriggers(Token);
                    CREATE INDEX idx_webhooks_profile ON WebhookTriggers(ProfileId);
                    CREATE INDEX idx_webhooks_job ON WebhookTriggers(JobId);
                ";
                await connection.ExecuteAsync(migrationSql);
                Log.Information("WebhookTriggers table migration completed");
            }
        }
    }

    private async Task CreateUsersTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL DEFAULT 'User',
                IsActive INTEGER NOT NULL DEFAULT 1,
                Email TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NULL,
                LastLoginAt TEXT NULL,
                PasswordChangeRequired INTEGER NOT NULL DEFAULT 0,
                LastSeenAt TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_users_username ON Users(Username COLLATE NOCASE);
            CREATE INDEX IF NOT EXISTS idx_users_active ON Users(IsActive);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateAuditLogTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS AuditLog (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EntityType TEXT NOT NULL,
                EntityId INTEGER NOT NULL,
                Action TEXT NOT NULL,
                PerformedBy TEXT NOT NULL,
                Changes TEXT NULL,
                IpAddress TEXT NULL,
                Timestamp TEXT NOT NULL,
                UserAgent TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_auditlog_entity ON AuditLog(EntityType, EntityId);
            CREATE INDEX IF NOT EXISTS idx_auditlog_timestamp ON AuditLog(Timestamp DESC);
            CREATE INDEX IF NOT EXISTS idx_auditlog_user ON AuditLog(PerformedBy);
        ";
        await connection.ExecuteAsync(sql);
    }

    // ============================================
    // REEF TABLES
    // ============================================

    private async Task CreateDestinationsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS Destinations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL DEFAULT '',
                Type INTEGER NOT NULL,
                ConfigurationJson TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                Tags TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NULL,
                Hash TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_destinations_type ON Destinations(Type);
            CREATE INDEX IF NOT EXISTS idx_destinations_active ON Destinations(IsActive);
            CREATE INDEX IF NOT EXISTS idx_destinations_tags ON Destinations(Tags);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateQueryTemplatesTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS QueryTemplates (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL DEFAULT '',
                Type INTEGER NOT NULL,
                Template TEXT NOT NULL,
                OutputFormat TEXT NOT NULL DEFAULT 'XML',
                IsActive INTEGER NOT NULL DEFAULT 1,
                Tags TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NULL,
                Hash TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_templates_type ON QueryTemplates(Type);
            CREATE INDEX IF NOT EXISTS idx_templates_active ON QueryTemplates(IsActive);
            CREATE INDEX IF NOT EXISTS idx_templates_format ON QueryTemplates(OutputFormat);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateProfileTransformationsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS ProfileTransformations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                TemplateId INTEGER NULL,
                InlineTemplate TEXT NULL,
                TransformationType INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                TransformationOptions TEXT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (TemplateId) REFERENCES QueryTemplates(Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS idx_profile_transforms_profile ON ProfileTransformations(ProfileId);
            CREATE INDEX IF NOT EXISTS idx_profile_transforms_template ON ProfileTransformations(TemplateId);
            CREATE INDEX IF NOT EXISTS idx_profile_transforms_order ON ProfileTransformations(ProfileId, SortOrder);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateJobsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS Jobs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT NOT NULL DEFAULT '',
                Type INTEGER NOT NULL,
                ProfileId INTEGER NULL,
                DestinationId INTEGER NULL,
                CustomActionJson TEXT NULL,

                -- Scheduling
                ScheduleType INTEGER NOT NULL,
                CronExpression TEXT NULL,
                IntervalMinutes INTEGER NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                StartTime TEXT NULL,
                EndTime TEXT NULL,
                WeekDays TEXT NULL,
                MonthDay INTEGER NULL,

                -- Job Configuration
                MaxRetries INTEGER NOT NULL DEFAULT 3,
                TimeoutMinutes INTEGER NOT NULL DEFAULT 60,
                Priority INTEGER NOT NULL DEFAULT 5,
                AllowConcurrent INTEGER NOT NULL DEFAULT 0,
                DependsOnJobIds TEXT NULL,

                -- Status
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                Status INTEGER NOT NULL DEFAULT 0,
                NextRunTime TEXT NULL,
                LastRunTime TEXT NULL,
                LastSuccessTime TEXT NULL,
                LastFailureTime TEXT NULL,
                ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
                AutoPauseEnabled INTEGER NOT NULL DEFAULT 1,

                -- Metadata
                Tags TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NULL,
                CreatedBy TEXT NULL,
                Hash TEXT NOT NULL,

                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (DestinationId) REFERENCES Destinations(Id) ON DELETE SET NULL
            );

            CREATE INDEX IF NOT EXISTS idx_jobs_type ON Jobs(Type);
            CREATE INDEX IF NOT EXISTS idx_jobs_profile ON Jobs(ProfileId);
            CREATE INDEX IF NOT EXISTS idx_jobs_destination ON Jobs(DestinationId);
            CREATE INDEX IF NOT EXISTS idx_jobs_status ON Jobs(Status);
            CREATE INDEX IF NOT EXISTS idx_jobs_enabled ON Jobs(IsEnabled);
            CREATE INDEX IF NOT EXISTS idx_jobs_next_run ON Jobs(NextRunTime);
            CREATE INDEX IF NOT EXISTS idx_jobs_priority ON Jobs(Priority DESC);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateJobExecutionsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS JobExecutions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId INTEGER NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                Status INTEGER NOT NULL,
                AttemptNumber INTEGER NOT NULL DEFAULT 1,
                OutputData TEXT NULL,
                ErrorMessage TEXT NULL,
                StackTrace TEXT NULL,
                BytesProcessed INTEGER NULL,
                RowsProcessed INTEGER NULL,
                TriggeredBy TEXT NULL,
                ServerNode TEXT NULL,
                ExecutionContext TEXT NULL,
                
                FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_job_executions_job ON JobExecutions(JobId);
            CREATE INDEX IF NOT EXISTS idx_job_executions_started ON JobExecutions(StartedAt DESC);
            CREATE INDEX IF NOT EXISTS idx_job_executions_status ON JobExecutions(Status);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateJobDependenciesTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS JobDependencies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                JobId INTEGER NOT NULL,
                DependsOnJobId INTEGER NOT NULL,
                Type INTEGER NOT NULL DEFAULT 1,
                Condition TEXT NULL,
                
                FOREIGN KEY (JobId) REFERENCES Jobs(Id) ON DELETE CASCADE,
                FOREIGN KEY (DependsOnJobId) REFERENCES Jobs(Id) ON DELETE CASCADE,
                UNIQUE(JobId, DependsOnJobId)
            );

            CREATE INDEX IF NOT EXISTS idx_job_deps_job ON JobDependencies(JobId);
            CREATE INDEX IF NOT EXISTS idx_job_deps_parent ON JobDependencies(DependsOnJobId);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateDeltaSyncStateTableAsync(SqliteConnection connection)
    {
        // Check if table exists with wrong foreign key reference
        var tableExists = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DeltaSyncState'");
        
        if (tableExists > 0)
        {
            // Check if it has the wrong foreign key (to ProfileExecution instead of ProfileExecutions)
            var tableSql = await connection.ExecuteScalarAsync<string>(
                "SELECT sql FROM sqlite_master WHERE type='table' AND name='DeltaSyncState'");
            
            if (tableSql != null && tableSql.Contains("ProfileExecution(Id)") && !tableSql.Contains("ProfileExecutions(Id)"))
            {
                Log.Warning("DeltaSyncState table has incorrect foreign key reference, dropping and recreating...");
                await connection.ExecuteAsync("DROP TABLE IF EXISTS DeltaSyncState");
                tableExists = 0;
            }
        }
        
        if (tableExists == 0)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS DeltaSyncState (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProfileId INTEGER NOT NULL,
                    ReefId TEXT NOT NULL,
                    RowHash TEXT NOT NULL,
                    LastSeenExecutionId INTEGER NOT NULL,
                    FirstSeenAt TEXT NOT NULL DEFAULT (datetime('now')),
                    LastSeenAt TEXT NOT NULL DEFAULT (datetime('now')),
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    DeletedAt TEXT NULL,
                    
                    FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                    FOREIGN KEY (LastSeenExecutionId) REFERENCES ProfileExecutions(Id)
                );

                CREATE INDEX IF NOT EXISTS idx_deltasync_profile_reefid ON DeltaSyncState(ProfileId, ReefId);
                CREATE INDEX IF NOT EXISTS idx_deltasync_profile_hash ON DeltaSyncState(ProfileId, RowHash);
                CREATE INDEX IF NOT EXISTS idx_deltasync_execution ON DeltaSyncState(LastSeenExecutionId);
                CREATE INDEX IF NOT EXISTS idx_deltasync_deleted ON DeltaSyncState(ProfileId, IsDeleted);
            ";
            await connection.ExecuteAsync(sql);
            Log.Debug("✓ DeltaSyncState table created");
        }
    }

    private async Task CreateProfileExecutionSplitsTableAsync(SqliteConnection connection)
    {
        var tableExists = await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ProfileExecutionSplits'");

        if (tableExists == 0)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS ProfileExecutionSplits (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExecutionId INTEGER NOT NULL,
                    SplitKey TEXT NOT NULL,
                    RowCount INTEGER NOT NULL,
                    Status TEXT NOT NULL CHECK (Status IN ('Running', 'Success', 'Failed')),
                    OutputPath TEXT NULL,
                    FileSizeBytes INTEGER NULL,
                    ErrorMessage TEXT NULL,
                    StartedAt TEXT NOT NULL,
                    CompletedAt TEXT NULL,
                    FOREIGN KEY (ExecutionId) REFERENCES ProfileExecutions(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_execution_splits_execution
                    ON ProfileExecutionSplits(ExecutionId);

                CREATE INDEX IF NOT EXISTS idx_execution_splits_key
                    ON ProfileExecutionSplits(ExecutionId, SplitKey);

                CREATE INDEX IF NOT EXISTS idx_execution_splits_status
                    ON ProfileExecutionSplits(Status);
            ";
            await connection.ExecuteAsync(sql);
            Log.Debug("ProfileExecutionSplits table created");
        }
    }

    private async Task CreateNotificationSettingsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS NotificationSettings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                IsEnabled INTEGER NOT NULL DEFAULT 0,
                DestinationId INTEGER NOT NULL,
                DestinationName TEXT NULL,

                -- Trigger Flags
                NotifyOnJobFailure INTEGER NOT NULL DEFAULT 1,
                NotifyOnJobSuccess INTEGER NOT NULL DEFAULT 0,
                NotifyOnProfileFailure INTEGER NOT NULL DEFAULT 1,
                NotifyOnProfileSuccess INTEGER NOT NULL DEFAULT 0,
                NotifyOnDatabaseSizeThreshold INTEGER NOT NULL DEFAULT 1,
                DatabaseSizeThresholdBytes INTEGER NOT NULL DEFAULT 1073741824,
                NotifyOnNewUser INTEGER NOT NULL DEFAULT 0,
                NotifyOnNewApiKey INTEGER NOT NULL DEFAULT 1,
                NotifyOnNewWebhook INTEGER NOT NULL DEFAULT 0,

                -- Email Configuration
                RecipientEmails TEXT NULL,

                -- Metadata
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                Hash TEXT NOT NULL,

                FOREIGN KEY (DestinationId) REFERENCES Destinations(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_notificationsettings_enabled ON NotificationSettings(IsEnabled);
            CREATE INDEX IF NOT EXISTS idx_notificationsettings_destination ON NotificationSettings(DestinationId);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateNotificationEmailTemplateTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS NotificationEmailTemplate (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TemplateType TEXT NOT NULL UNIQUE,
                Subject TEXT NOT NULL,
                HtmlBody TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 1,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_notificationemailtemplate_type ON NotificationEmailTemplate(TemplateType);
            CREATE INDEX IF NOT EXISTS idx_notificationemailtemplate_default ON NotificationEmailTemplate(IsDefault);
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreatePendingEmailApprovalsTableAsync(SqliteConnection connection)
    {
        var sql = @"
            CREATE TABLE IF NOT EXISTS PendingEmailApprovals (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Guid TEXT NOT NULL UNIQUE,
                ProfileId INTEGER NOT NULL,
                ProfileExecutionId INTEGER NOT NULL,
                Recipients TEXT NOT NULL,
                CcAddresses TEXT NULL,
                Subject TEXT NOT NULL,
                HtmlBody TEXT NOT NULL,
                AttachmentConfig TEXT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                ApprovedByUserId INTEGER NULL,
                ApprovedAt TEXT NULL,
                ApprovalNotes TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ExpiresAt TEXT NULL,
                ErrorMessage TEXT NULL,
                SentAt TEXT NULL,
                Hash TEXT NOT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (ProfileExecutionId) REFERENCES ProfileExecutions(Id) ON DELETE CASCADE,
                FOREIGN KEY (ApprovedByUserId) REFERENCES Users(Id) ON DELETE SET NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_PendingEmailApprovals_Guid ON PendingEmailApprovals(Guid);
            CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_Status ON PendingEmailApprovals(Status);
            CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_ProfileId ON PendingEmailApprovals(ProfileId);
            CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_CreatedAt ON PendingEmailApprovals(CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_StatusCreatedAt ON PendingEmailApprovals(Status, CreatedAt DESC);
            CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_ExpiresAt ON PendingEmailApprovals(ExpiresAt) WHERE ExpiresAt IS NOT NULL;
            CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_ApprovedByUserId ON PendingEmailApprovals(ApprovedByUserId) WHERE ApprovedByUserId IS NOT NULL;
        ";
        await connection.ExecuteAsync(sql);
    }

    private async Task CreateApplicationStartupTableAsync(SqliteConnection connection)
    {
        // Check if table exists
        const string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='ApplicationStartup'";
        var tableExists = await connection.QueryFirstOrDefaultAsync(checkTableSql);

        if (tableExists == null)
        {
            // Create new table with StartupToken column
            var createTableSql = @"
                CREATE TABLE ApplicationStartup (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    StartupToken TEXT NOT NULL UNIQUE,
                    StartedAt TEXT NOT NULL,
                    MachineName TEXT NOT NULL,
                    Version TEXT NULL,
                    CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))
                );

                CREATE INDEX idx_appstartup_started ON ApplicationStartup(StartedAt DESC);
            ";
            await connection.ExecuteAsync(createTableSql);
        }
        else
        {
            // Table exists, check if StartupToken column exists
            const string checkColumnSql = "PRAGMA table_info(ApplicationStartup)";
            var columns = await connection.QueryAsync(checkColumnSql);
            var hasStartupToken = columns.Any(c => c.name == "StartupToken");

            if (!hasStartupToken)
            {
                // Add the column with a default value for existing rows
                await connection.ExecuteAsync("ALTER TABLE ApplicationStartup ADD COLUMN StartupToken TEXT");
                // Update existing rows with unique tokens
                await connection.ExecuteAsync(@"
                    UPDATE ApplicationStartup
                    SET StartupToken = lower(hex(randomblob(16)))
                    WHERE StartupToken IS NULL");
                // Now add the constraints
                await connection.ExecuteAsync("CREATE UNIQUE INDEX idx_startup_token ON ApplicationStartup(StartupToken)");
            }
        }
    }

    private async Task CreateDefaultUser(SqliteConnection connection)
    {
        const string checkUserSql = "SELECT COUNT(*) FROM Users";
        var userCount = await connection.ExecuteScalarAsync<int>(checkUserSql);

        if (userCount == 0)
        {
            // Create default admin user with BCrypt hashed password "admin123"
            // Force password change on first login for security
            const string insertUserSql = @"
                INSERT INTO Users (Username, PasswordHash, Role, IsActive, CreatedAt, PasswordChangeRequired)
                VALUES (@Username, @PasswordHash, @Role, @IsActive, @CreatedAt, @PasswordChangeRequired)
            ";

            var passwordHasher = new Reef.Core.Security.PasswordHasher();
            var passwordHash = passwordHasher.HashPassword("admin123");

            await connection.ExecuteAsync(insertUserSql, new
            {
                Username = "admin",
                PasswordHash = passwordHash,
                Role = "Admin",
                IsActive = 1,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                PasswordChangeRequired = 1
            });

            Log.Warning("! Created default admin user (username: admin, password: admin123)");
            Log.Warning("! Password change will be required on first login");
            Log.Information("");
        }
    }

    public async Task SeedSampleDataAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Insert sample local destination
        var destinationExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Destinations WHERE Name = 'Local Exports'");
        
        if (destinationExists == 0)
        {
            var destName = "Local Exports";
            var destType = "1";
            var destConfig = "{\"BasePath\":\"exports\",\"CreateDirectories\":true}";
            
            // Encrypt the configuration if encryption service is available
            var destConfigToStore = destConfig;
            if (_encryptionService != null)
            {
                destConfigToStore = _encryptionService.Encrypt(destConfig);
                Log.Debug("Encrypted destination configuration");
            }
            else
            {
                Log.Warning("EncryptionService not available during seeding - storing unencrypted configuration");
            }
            
            var destHash = Reef.Helpers.HashHelper.ComputeDestinationHash(destName, destType, destConfigToStore);
            
            await connection.ExecuteAsync(@"
                INSERT INTO Destinations (Name, Description, Type, ConfigurationJson, IsActive, Tags, CreatedAt, Hash)
                VALUES (@Name, @Description, @Type, @ConfigurationJson, @IsActive, @Tags, datetime('now'), @Hash)",
                new
                {
                    Name = destName,
                    Description = "Default local file system exports",
                    Type = 1,
                    ConfigurationJson = destConfigToStore,
                    IsActive = 1,
                    Tags = "default,local",
                    Hash = destHash
                });
        }

        // Define essential SQL Server query templates
        // IMPORTANT: Reef supports two transformation paths:
        // 1. SQL Server Native (Types 4-9): Uses QueryTemplateService with Options-based configuration
        //    - Template field stores example syntax for reference only
        //    - Actual configuration comes from Profile.TransformationOptionsJson at runtime
        // 2. Custom Templates (Types 1-3): Uses Scriban template engine
        //    - Template field contains actual Scriban syntax
        var templates = new[]
        {
            // ========== SQL SERVER NATIVE TEMPLATES ==========
            // These use QueryTemplateService.ApplyTransformationAsync with Options
            // The Template field is for reference/documentation only
            
            new
            {
                Name = "FOR XML PATH",
                Description = "SQL Server FOR XML PATH with ROOT - Most flexible XML generation. Configure via Profile TransformationOptionsJson.",
                Type = 6, // ForXmlPath
                Template = "FOR XML PATH, ROOT('data')",
                OutputFormat = "XML",
                Tags = "xml,sql-server,native,example"
            },
            new
            {
                Name = "FOR JSON PATH",
                Description = "SQL Server FOR JSON PATH - Flexible JSON with custom property names. Configure via Profile TransformationOptionsJson.",
                Type = 9, // ForJsonPath
                Template = "FOR JSON PATH, ROOT('data'), INCLUDE_NULL_VALUES",
                OutputFormat = "JSON",
                Tags = "json,sql-server,native,example"
            },
            new
            {
                Name = "FOR JSON AUTO",
                Description = "SQL Server FOR JSON AUTO - Automatic JSON structure based on query. Configure via Profile TransformationOptionsJson.",
                Type = 8, // ForJson
                Template = "FOR JSON AUTO, INCLUDE_NULL_VALUES",
                OutputFormat = "JSON",
                Tags = "json,sql-server,native,auto,example"
            },
            new
            {
                Name = "FOR XML AUTO",
                Description = "SQL Server FOR XML AUTO - Automatic XML structure based on query. Configure via Profile TransformationOptionsJson.",
                Type = 5, // ForXmlAuto
                Template = "FOR XML AUTO, ROOT('data'), ELEMENTS",
                OutputFormat = "XML",
                Tags = "xml,sql-server,native,auto,example"
            },
            
            // ========== CUSTOM SCRIBAN TEMPLATES ==========
            // These use ScribanTemplateEngine - Template field contains actual transformation logic
            
            new
            {
                Name = "CSV - Pipe Delimited",
                Description = "Pipe-delimited CSV format (|) - Common for legacy systems and mainframes",
                Type = 10, // ScribanTemplate (new type for Scriban templates)
                Template = @"ItemCode|Description|SalesPackagePrice
{{~ for row in data ~}}
{{ row.ItemCode | safe_string | csv_escape }}|{{ row.Description | safe_string | csv_escape }}|{{ row.SalesPackagePrice | safe_string | csv_escape }}
{{~ end ~}}",
                OutputFormat = "CSV",
                Tags = "csv,pipe-delimited,scriban,example"
            },
            new
            {
                Name = "CSV - Tab Delimited",
                Description = "Tab-delimited CSV format (TSV) - Excel and data science friendly",
                Type = 10, // ScribanTemplate
                Template = @"ItemCode	Description	SalesPackagePrice
{{~ for row in data ~}}
{{ row.ItemCode | safe_string | csv_escape }}	{{ row.Description | safe_string | csv_escape }}	{{ row.SalesPackagePrice | safe_string | csv_escape }}
{{~ end ~}}",
                OutputFormat = "CSV",
                Tags = "csv,tab-delimited,tsv,excel,scriban,example"
            },
            new
            {
                Name = "XML - Items with Attributes",
                Description = "Custom XML with item code as attribute and nested elements - Common B2B format",
                Type = 10, // ScribanTemplate
                Template = @"{{~ if data.size > 0 ~}}
<Items>
{{~ for row in data ~}}
{{~ if row.ItemCode ~}}
  <Item code=""{{ row.ItemCode | html.escape }}"">
    {{~ if row.Description ~}}<Description>{{ row.Description | html.escape }}</Description>{{~ end ~}}
    {{~ if row.SalesPackagePrice ~}}<Price>{{ row.SalesPackagePrice }}</Price>{{~ end ~}}
  </Item>
{{~ end ~}}
{{~ end ~}}
</Items>
{{~ else ~}}
<Items />
{{~ end ~}}",
                OutputFormat = "XML",
                Tags = "xml,b2b,catalog,scriban,example"
            },
            new
            {
                Name = "XML - SOAP Envelope",
                Description = "SOAP-style XML envelope - For legacy web services integration",
                Type = 10, // ScribanTemplate
                Template = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <DataExport>
      {{~ if data.size > 0 ~}}
      {{~ for row in data ~}}
      <Record>
        {{~ if row.ItemCode ~}}<ItemCode>{{ row.ItemCode | html.escape }}</ItemCode>{{~ end ~}}
        {{~ if row.Description ~}}<Description>{{ row.Description | html.escape }}</Description>{{~ end ~}}
        {{~ if row.SalesPackagePrice ~}}<SalesPackagePrice>{{ row.SalesPackagePrice | html.escape }}</SalesPackagePrice>{{~ end ~}}
      </Record>
      {{~ end ~}}
      {{~ end ~}}
    </DataExport>
  </soap:Body>
</soap:Envelope>",
                OutputFormat = "XML",
                Tags = "xml,soap,web-service,scriban,example"
            },
            new
            {
                Name = "JSON - Nested Catalog Structure",
                Description = "Nested JSON with catalog/items structure - Modern API format",
                Type = 10, // ScribanTemplate
                Template = @"{
  ""catalog"": {
    ""generated"": ""{{ date.now | date.to_string '%Y-%m-%d %H:%M:%S' }}"",
    ""count"": {{ data.size }},
    ""items"": [
      {{~ for row in data ~}}
      {
        ""sku"": ""{{ row.ItemCode | string.escape }}"",
        ""name"": ""{{ row.Description | string.escape }}"",
        ""pricing"": {
          ""retail"": {{ row.SalesPackagePrice ?? 0 }}
        }
      }{{~ if !for.last ~}},{{~ end ~}}
      {{~ end ~}}
    ]
  }
}",
                OutputFormat = "JSON",
                Tags = "json,nested,api,modern,scriban,example"
            },
            new
            {
                Name = "JSON - Simple Array",
                Description = "Simple flat JSON array - Most common REST API format",
                Type = 10, // ScribanTemplate
                Template = @"[
{{~ for row in data ~}}
  {
    ""ItemCode"": ""{{ row.ItemCode | string.escape }}"",
    ""Description"": ""{{ row.Description | string.escape }}"",
    ""SalesPackagePrice"": {{ row.SalesPackagePrice ?? null }}
  }{{~ if !for.last ~}},{{~ end ~}}
{{~ end ~}}
]",
                OutputFormat = "JSON",
                Tags = "json,rest,api,scriban,example"
            },
            new
            {
                Name = "Fixed-Width Text",
                Description = "Fixed-width text format - Mainframe and legacy system integration",
                Type = 10, // ScribanTemplate
                Template = @"{{~ for row in data ~}}
{{ row.ItemCode | string.pad_right 20 }}{{ row.Description | string.pad_right 50 }}{{ row.SalesPackagePrice ?? 0 | math.format ""F2"" | string.pad_left 10 }}
{{~ end ~}}",
                OutputFormat = "TXT",
                Tags = "txt,fixed-width,mainframe,scriban,example"
            },
            new
            {
                Name = "HTML - Data Table",
                Description = "HTML table format - Quick data viewing and reports",
                Type = 10, // ScribanTemplate
                Template = @"<!DOCTYPE html>
<html>
<head>
  <meta charset=""utf-8"">
  <title>Data Export</title>
  <style>
    table { border-collapse: collapse; width: 100%; }
    th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
    th { background-color: #4CAF50; color: white; }
    tr:nth-child(even) { background-color: #f2f2f2; }
  </style>
</head>
<body>
  <h1>Data Export ({{ data.size }} records)</h1>
  <table>
    <thead>
      <tr>
        <th>ItemCode</th>
        <th>Description</th>
        <th>SalesPackagePrice</th>
      </tr>
    </thead>
    <tbody>
      {{~ for row in data ~}}
      <tr>
        <td>{{ row.ItemCode | html.escape }}</td>
        <td>{{ row.Description | html.escape }}</td>
        <td>{{ row.SalesPackagePrice | html.escape }}</td>
      </tr>
      {{~ end ~}}
    </tbody>
  </table>
</body>
</html>",
                OutputFormat = "HTML",
                Tags = "html,report,table,viewing,scriban,example"
            }
        };

        // Insert each template if it doesn't exist
        foreach (var template in templates)
        {
            var exists = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM QueryTemplates WHERE Name = @Name",
                new { template.Name });
            
            if (exists == 0)
            {
                var hash = Reef.Helpers.HashHelper.ComputeDestinationHash(
                    template.Name, 
                    template.Type.ToString(), 
                    template.Template);
                
                await connection.ExecuteAsync(@"
                    INSERT INTO QueryTemplates (Name, Description, Type, Template, OutputFormat, IsActive, Tags, CreatedAt, Hash)
                    VALUES (@Name, @Description, @Type, @Template, @OutputFormat, @IsActive, @Tags, datetime('now'), @Hash)",
                    new
                    {
                        template.Name,
                        template.Description,
                        template.Type,
                        template.Template,
                        template.OutputFormat,
                        IsActive = 1,
                        template.Tags,
                        Hash = hash
                    });
            }
        }
    }

    /// <summary>
    /// Apply schema migrations for existing databases
    ///
    /// NOTE: Legacy column migrations have been consolidated into base table schemas.
    /// Since there are no production customers yet, all new installations start with a
    /// complete schema. The AddColumnIfNotExistsAsync helper is retained for future use.
    /// </summary>
    private async Task ApplyMigrationsAsync(SqliteConnection connection)
    {
        Log.Debug("Applying database schema migrations...");

        // Create ProfileExecutionSplits table (new table for split tracking)
        await CreateProfileExecutionSplitsTableAsync(connection);

        // Email Approval Workflow migrations
        await AddEmailApprovalColumnsAsync(connection);

        // Legacy migrations removed: All columns are now defined in base table schemas
        // since there are no production customers yet. All new installations start with the complete schema.
        //
        // If future migrations are needed for backward compatibility:
        // Example: await AddColumnIfNotExistsAsync(connection, "Profiles", "NewColumn", "TEXT NULL DEFAULT 'value'");

        Log.Debug("Database schema migrations completed");
    }

    /// <summary>
    /// Add email approval workflow columns and table
    /// </summary>
    private async Task AddEmailApprovalColumnsAsync(SqliteConnection connection)
    {
        Log.Debug("Applying email approval migrations...");

        // Add columns to Profiles table
        await AddColumnIfNotExistsAsync(connection, "Profiles", "EmailApprovalRequired", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(connection, "Profiles", "EmailApprovalRoles", "TEXT NULL");

        // Add columns to ProfileExecutions table
        await AddColumnIfNotExistsAsync(connection, "ProfileExecutions", "ApprovalStatus", "TEXT NULL");
        await AddColumnIfNotExistsAsync(connection, "ProfileExecutions", "PendingEmailApprovalId", "INTEGER NULL");
        await AddColumnIfNotExistsAsync(connection, "ProfileExecutions", "ApprovedAt", "TEXT NULL");

        // Create PendingEmailApprovals table if it doesn't exist
        var tableExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='PendingEmailApprovals'");

        if (tableExists == 0)
        {
            const string createTableSql = @"
                CREATE TABLE PendingEmailApprovals (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Guid TEXT NOT NULL UNIQUE,
                    ProfileId INTEGER NOT NULL,
                    ProfileExecutionId INTEGER NOT NULL,
                    Recipients TEXT NOT NULL,
                    CcAddresses TEXT NULL,
                    Subject TEXT NOT NULL,
                    HtmlBody TEXT NOT NULL,
                    AttachmentConfig TEXT NULL,
                    Status TEXT DEFAULT 'Pending',
                    ApprovedByUserId INTEGER NULL,
                    ApprovedAt TEXT NULL,
                    ApprovalNotes TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    ExpiresAt TEXT NULL,
                    ErrorMessage TEXT NULL,
                    SentAt TEXT NULL,
                    Hash TEXT NOT NULL,
                    FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ProfileExecutionId) REFERENCES ProfileExecutions(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ApprovedByUserId) REFERENCES Users(Id) ON DELETE SET NULL
                )
            ";

            await connection.ExecuteAsync(createTableSql);
            Log.Debug("✓ Created PendingEmailApprovals table");
        }

        // Add indexes for performance (except Guid index which requires the column to exist first)
        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS IX_Profiles_EmailApprovalRequired ON Profiles(EmailApprovalRequired) WHERE EmailApprovalRequired = 1",
            "CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_ApprovalStatus ON ProfileExecutions(ApprovalStatus) WHERE ApprovalStatus IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS IX_ProfileExecutions_PendingEmailApprovalId ON ProfileExecutions(PendingEmailApprovalId) WHERE PendingEmailApprovalId IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_Status ON PendingEmailApprovals(Status)",
            "CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_ProfileId ON PendingEmailApprovals(ProfileId)",
            "CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_CreatedAt ON PendingEmailApprovals(CreatedAt DESC)",
            "CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_StatusCreatedAt ON PendingEmailApprovals(Status, CreatedAt DESC)",
            "CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_ExpiresAt ON PendingEmailApprovals(ExpiresAt) WHERE ExpiresAt IS NOT NULL",
            "CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_ApprovedByUserId ON PendingEmailApprovals(ApprovedByUserId) WHERE ApprovedByUserId IS NOT NULL"
        };

        foreach (var indexSql in indexes)
        {
            try
            {
                await connection.ExecuteAsync(indexSql);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create index, continuing");
            }
        }

        // Migration: Add Guid column to existing PendingEmailApprovals tables (for databases created before Guid was added)
        var columns = await connection.QueryAsync<dynamic>("PRAGMA table_info(PendingEmailApprovals)");
        var guidColumnExists = columns.Any(c => ((string)c.name).Equals("Guid", StringComparison.OrdinalIgnoreCase));
        
        if (!guidColumnExists)
        {
            // Column doesn't exist, add it and populate GUIDs
            await AddColumnIfNotExistsAsync(connection, "PendingEmailApprovals", "Guid", "TEXT NULL");
            
            // Populate GUIDs for existing records that don't have one
            const string populateGuids = @"
                UPDATE PendingEmailApprovals 
                SET Guid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab',abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)))
                WHERE Guid IS NULL OR Guid = ''
            ";
            await connection.ExecuteAsync(populateGuids);
            Log.Debug("✓ Migrated existing records to include GUIDs");
        }
        
        // Create the Guid index (for both new and migrated tables)
        try
        {
            await connection.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_PendingEmailApprovals_Guid ON PendingEmailApprovals(Guid)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create Guid index, continuing");
        }

        Log.Debug("✓ Email approval migrations completed");
    }

    /// <summary>
    /// Helper method to add a column to a table if it doesn't already exist
    /// </summary>
    private async Task AddColumnIfNotExistsAsync(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        try
        {
            var columns = await connection.QueryAsync<dynamic>($"PRAGMA table_info({tableName})");
            var columnExists = columns.Any(c => ((string)c.name).Equals(columnName, StringComparison.OrdinalIgnoreCase));

            if (!columnExists)
            {
                await connection.ExecuteAsync($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
                Log.Information("Added {Column} column to {Table} table", columnName, tableName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error adding {Column} column to {Table} table", columnName, tableName);
        }
    }
}