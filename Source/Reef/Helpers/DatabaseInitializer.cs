
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

        // Seed notification templates AFTER migrations to ensure all columns exist
        await SeedNotificationEmailTemplatesAsync(connection);

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
                DisplayName TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ModifiedAt TEXT NULL,
                LastLoginAt TEXT NULL,
                PasswordChangeRequired INTEGER NOT NULL DEFAULT 0,
                LastSeenAt TEXT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DeletedAt TEXT NULL,
                DeletedBy TEXT NULL
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
                NotifyOnNewEmailApproval INTEGER NOT NULL DEFAULT 0,
                NewEmailApprovalCooldownHours INTEGER NOT NULL DEFAULT 24,

                -- Instance Exposure Configuration
                EnableCTA INTEGER NOT NULL DEFAULT 0,
                CTAUrl TEXT NULL,

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
                CTAButtonText TEXT NULL,
                CTAUrlOverride TEXT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                UpdatedAt TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE INDEX IF NOT EXISTS idx_notificationemailtemplate_type ON NotificationEmailTemplate(TemplateType);
            CREATE INDEX IF NOT EXISTS idx_notificationemailtemplate_default ON NotificationEmailTemplate(IsDefault);
        ";
        await connection.ExecuteAsync(sql);
    }

    /// <summary>
    /// Seed default notification email templates if table is empty
    /// </summary>
    private async Task SeedNotificationEmailTemplatesAsync(SqliteConnection connection)
    {
        try
        {
            // Check if table is empty
            const string checkSql = "SELECT COUNT(*) FROM NotificationEmailTemplate";
            var count = await connection.ExecuteScalarAsync<int>(checkSql);

            if (count == 0)
            {
                Log.Debug("Seeding default notification email templates...");

                const string insertSql = @"
                    INSERT INTO NotificationEmailTemplate (TemplateType, Subject, HtmlBody, IsDefault, CTAButtonText, CreatedAt, UpdatedAt)
                    VALUES (@TemplateType, @Subject, @HtmlBody, @IsDefault, @CTAButtonText, @CreatedAt, @UpdatedAt)";

                var templates = new[]
                {
                    new { TemplateType = "ProfileSuccess", Subject = "[Reef] Profile '{{ ProfileName }}' executed successfully", CTAButtonText = "View Execution" },
                    new { TemplateType = "ProfileFailure", Subject = "[Reef] Profile '{{ ProfileName }}' execution failed", CTAButtonText = "View Execution" },
                    new { TemplateType = "JobSuccess", Subject = "[Reef] Job '{{ JobName }}' completed successfully", CTAButtonText = "View Job" },
                    new { TemplateType = "JobFailure", Subject = "[Reef] Job '{{ JobName }}' failed", CTAButtonText = "View Job" },
                    new { TemplateType = "NewUser", Subject = "[Reef] New user created: {{ Username }}", CTAButtonText = "View Users" },
                    new { TemplateType = "NewApiKey", Subject = "[Reef] New API key created: {{ KeyName }}", CTAButtonText = "Manage API Keys" },
                    new { TemplateType = "NewWebhook", Subject = "[Reef] New webhook created: {{ WebhookName }}", CTAButtonText = "Manage Webhooks" },
                    new { TemplateType = "NewEmailApproval", Subject = "[Reef] {{ PendingCount }} email{{ Plural }} pending approval", CTAButtonText = "Review Approvals" },
                    new { TemplateType = "DatabaseSizeThreshold", Subject = "[Reef] Database size critical", CTAButtonText = "View System Status" }
                };

                foreach (var template in templates)
                {
                    var htmlBody = template.TemplateType switch
                    {
                        "ProfileSuccess" => BuildProfileSuccessEmailBody(),
                        "ProfileFailure" => BuildProfileFailureEmailBody(),
                        "JobSuccess" => BuildJobSuccessEmailBody(),
                        "JobFailure" => BuildJobFailureEmailBody(),
                        "NewUser" => BuildNewUserEmailBody(),
                        "NewApiKey" => BuildNewApiKeyEmailBody(),
                        "NewWebhook" => BuildNewWebhookEmailBody(),
                        "NewEmailApproval" => BuildDefaultNewEmailApprovalEmailBody(),
                        "DatabaseSizeThreshold" => BuildDatabaseSizeThresholdEmailBody(),
                        _ => BuildSimpleNotificationTemplate(template.TemplateType)
                    };

                    await connection.ExecuteAsync(insertSql, new
                    {
                        template.TemplateType,
                        template.Subject,
                        HtmlBody = htmlBody,
                        IsDefault = 1,
                        template.CTAButtonText,
                        CreatedAt = DateTime.UtcNow.ToString("o"),
                        UpdatedAt = DateTime.UtcNow.ToString("o")
                    });
                }

                Log.Debug("Seeded {Count} default notification email templates", templates.Length);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error seeding notification email templates");
        }
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
                ReefId TEXT NULL,
                DeltaSyncHash TEXT NULL,
                DeltaSyncRowType TEXT NULL,
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

        // Only seed templates if the table is completely empty (first-time initialization)
        // This ensures user-deleted templates are not re-created on subsequent app startups
        var existingCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM QueryTemplates");

        if (existingCount == 0)
        {
            Log.Debug("QueryTemplates table is empty. Seeding {Count} default templates", templates.Length);

            foreach (var template in templates)
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

            Log.Debug("Successfully seeded {Count} default query templates", templates.Length);
        }
        else
        {
            Log.Debug("QueryTemplates table already contains {Count} templates. Skipping seed.", existingCount);
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

        // Add ReefId and DeltaSyncHash columns to PendingEmailApprovals for delta sync support
        await AddColumnIfNotExistsAsync(connection, "PendingEmailApprovals", "ReefId", "TEXT NULL");
        await AddColumnIfNotExistsAsync(connection, "PendingEmailApprovals", "DeltaSyncHash", "TEXT NULL");
        await AddColumnIfNotExistsAsync(connection, "PendingEmailApprovals", "DeltaSyncRowType", "TEXT NULL");

        // Add Email Approval Notification columns to NotificationSettings
        await AddColumnIfNotExistsAsync(connection, "NotificationSettings", "NotifyOnNewEmailApproval", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(connection, "NotificationSettings", "NewEmailApprovalCooldownHours", "INTEGER NOT NULL DEFAULT 24");

        // Username change tracking and User ID foreign keys
        await CreateUsernameHistoryTableAsync(connection);
        await MigrateCreatedByToUserIdsAsync(connection);
        await AddColumnIfNotExistsAsync(connection, "Users", "DisplayName", "TEXT NULL");

        // Add soft delete columns to Users table
        await AddColumnIfNotExistsAsync(connection, "Users", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(connection, "Users", "DeletedAt", "TEXT NULL");
        await AddColumnIfNotExistsAsync(connection, "Users", "DeletedBy", "TEXT NULL");

        // Add instance exposure columns to NotificationSettings
        await AddColumnIfNotExistsAsync(connection, "NotificationSettings", "EnableCTA", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(connection, "NotificationSettings", "CTAUrl", "TEXT NULL");

        // Add CTA columns to NotificationEmailTemplate for per-template configuration
        await AddColumnIfNotExistsAsync(connection, "NotificationEmailTemplate", "CTAButtonText", "TEXT NULL");
        await AddColumnIfNotExistsAsync(connection, "NotificationEmailTemplate", "CTAUrlOverride", "TEXT NULL");

        // Legacy migrations removed: All columns are now defined in base table schemas
        // since there are no production customers yet. All new installations start with the complete schema.
        //
        // If future migrations are needed for backward compatibility:
        // Example: await AddColumnIfNotExistsAsync(connection, "Profiles", "NewColumn", "TEXT NULL DEFAULT 'value'");

        Log.Debug("Database schema migrations completed");
    }

    /// <summary>
    /// Create UsernameHistory table for tracking username changes
    /// </summary>
    private async Task CreateUsernameHistoryTableAsync(SqliteConnection connection)
    {
        var tableExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='UsernameHistory'");

        if (tableExists == 0)
        {
            Log.Debug("Creating UsernameHistory table...");

            const string createTableSql = @"
                CREATE TABLE UsernameHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    OldUsername TEXT NOT NULL,
                    NewUsername TEXT NOT NULL,
                    ChangedAt TEXT NOT NULL,
                    ChangedBy TEXT NOT NULL,
                    ChangedByUserId INTEGER NULL,
                    IpAddress TEXT NULL,
                    Reason TEXT NULL,
                    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ChangedByUserId) REFERENCES Users(Id) ON DELETE SET NULL
                )
            ";

            await connection.ExecuteAsync(createTableSql);

            // Create indexes for performance
            await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_UsernameHistory_UserId ON UsernameHistory(UserId)");
            await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_UsernameHistory_ChangedAt ON UsernameHistory(ChangedAt DESC)");
            await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_UsernameHistory_OldUsername ON UsernameHistory(OldUsername)");
            await connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_UsernameHistory_NewUsername ON UsernameHistory(NewUsername)");

            Log.Debug("✓ Created UsernameHistory table");
        }
    }

    /// <summary>
    /// Migrate CreatedBy columns from username strings to User IDs
    /// This converts existing string values to integer IDs in-place
    /// Note: AuditLog.PerformedBy remains a string (username) as it's a historical record
    /// </summary>
    private async Task MigrateCreatedByToUserIdsAsync(SqliteConnection connection)
    {
        Log.Debug("Migrating CreatedBy columns from usernames to User IDs...");

        // Check if CreatedBy in Connections contains text data (needs migration)
        var connectionsNeedsMigration = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM Connections
            WHERE CreatedBy IS NOT NULL
            AND typeof(CreatedBy) = 'text'
            AND CAST(CreatedBy AS INTEGER) = 0
        ");

        if (connectionsNeedsMigration > 0)
        {
            Log.Information("Migrating {Count} Connections.CreatedBy records from usernames to User IDs", connectionsNeedsMigration);

            // Convert username strings to User IDs for Connections
            await connection.ExecuteAsync(@"
                UPDATE Connections
                SET CreatedBy = (SELECT Id FROM Users WHERE LOWER(Users.Username) = LOWER(Connections.CreatedBy))
                WHERE CreatedBy IS NOT NULL
                AND typeof(CreatedBy) = 'text'
                AND (SELECT Id FROM Users WHERE LOWER(Users.Username) = LOWER(Connections.CreatedBy)) IS NOT NULL
            ");

            // Clear CreatedBy for records where user no longer exists
            await connection.ExecuteAsync(@"
                UPDATE Connections
                SET CreatedBy = NULL
                WHERE CreatedBy IS NOT NULL
                AND typeof(CreatedBy) = 'text'
            ");

            Log.Debug("✓ Migrated Connections.CreatedBy to User IDs");
        }

        // Check if CreatedBy in Profiles contains text data (needs migration)
        var profilesNeedsMigration = await connection.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM Profiles
            WHERE CreatedBy IS NOT NULL
            AND typeof(CreatedBy) = 'text'
            AND CAST(CreatedBy AS INTEGER) = 0
        ");

        if (profilesNeedsMigration > 0)
        {
            Log.Information("Migrating {Count} Profiles.CreatedBy records from usernames to User IDs", profilesNeedsMigration);

            // Convert username strings to User IDs for Profiles
            await connection.ExecuteAsync(@"
                UPDATE Profiles
                SET CreatedBy = (SELECT Id FROM Users WHERE LOWER(Users.Username) = LOWER(Profiles.CreatedBy))
                WHERE CreatedBy IS NOT NULL
                AND typeof(CreatedBy) = 'text'
                AND (SELECT Id FROM Users WHERE LOWER(Users.Username) = LOWER(Profiles.CreatedBy)) IS NOT NULL
            ");

            // Clear CreatedBy for records where user no longer exists
            await connection.ExecuteAsync(@"
                UPDATE Profiles
                SET CreatedBy = NULL
                WHERE CreatedBy IS NOT NULL
                AND typeof(CreatedBy) = 'text'
            ");

            Log.Debug("✓ Migrated Profiles.CreatedBy to User IDs");
        }

        // Note: AuditLog.PerformedBy remains as string (username) intentionally
        // Audit logs are historical records and should preserve the username used at the time

        Log.Debug("✓ Migrated CreatedBy columns to User IDs");
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
    /// Add NewEmailApproval notification template if it doesn't exist
    /// </summary>
    private async Task AddEmailApprovalNotificationTemplateAsync(SqliteConnection connection)
    {
        try
        {
            // Check if template already exists
            const string checkSql = "SELECT COUNT(*) FROM NotificationEmailTemplate WHERE TemplateType = 'NewEmailApproval'";
            var count = await connection.ExecuteScalarAsync<int>(checkSql);

            if (count == 0)
            {
                const string insertSql = @"
                    INSERT INTO NotificationEmailTemplate (TemplateType, Subject, HtmlBody, IsDefault, CreatedAt, UpdatedAt)
                    VALUES (@TemplateType, @Subject, @HtmlBody, @IsDefault, @CreatedAt, @UpdatedAt)";

                await connection.ExecuteAsync(insertSql, new
                {
                    TemplateType = "NewEmailApproval",
                    Subject = "[Reef] {PendingCount} email{Plural} pending approval",
                    HtmlBody = BuildDefaultNewEmailApprovalEmailBody(),
                    IsDefault = 1,
                    CreatedAt = DateTime.UtcNow.ToString("o"),
                    UpdatedAt = DateTime.UtcNow.ToString("o")
                });

                Log.Information("Added NewEmailApproval notification template");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error adding NewEmailApproval notification template");
        }
    }

    /// <summary>
    /// Build a simple notification template that will be replaced by in-code templates
    /// This is just a placeholder to prevent 404 errors - actual emails use in-code templates
    /// </summary>
    private static string BuildSimpleNotificationTemplate(string templateType)
    {
        return @"
<!doctype html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Notification</title>
</head>
<body style=""font-family: Arial, sans-serif; padding: 20px;"">
    <p>This is a placeholder template for " + templateType + @".</p>
    <p>The actual email content is generated programmatically.</p>
    <p>You can customize this template in the Email Templates tab.</p>

    {{~ if EnableCTA ~}}
    <p style=""margin-top: 20px;"">
        <a href=""{{ CTAUrl }}"" style=""display: inline-block; padding: 10px 20px; background-color: #111827; color: #ffffff; text-decoration: none; border-radius: 5px;"">
            {{ CTAButtonText }}
        </a>
    </p>
    {{~ end ~}}
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for NewEmailApproval notification with Scriban conditional CTA support
    /// </summary>
    private static string BuildDefaultNewEmailApprovalEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Pending Approval</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    {{ PendingCount }} pending approval in Reef.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  Email{{ Plural }} pending approval
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                There {{ PluralVerb }} <strong style=""color:#111827;"">{{ PendingCount }}</strong> email{{ Plural }} waiting for approval.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Pending items
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ PendingCount }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Notification time
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ NotificationTime | date.to_string '%Y-%m-%d %H:%M:%S' }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fff7ed; border:1px solid #ffedd5; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7c2d12;"">
                    Please review and approve or reject {{ PluralThem }} in the application dashboard.
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for ProfileSuccess notification
    /// </summary>
    private static string BuildProfileSuccessEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Profile Execution Success</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Profile {{ ProfileName }} executed successfully.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  Profile executed successfully
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Profile <strong style=""color:#111827;"">{{ ProfileName }}</strong> has completed successfully.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Execution ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ExecutionId }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Execution Time
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ExecutionTime }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Row Count
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ RowCount }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    File Size
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ FileSize }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for ProfileFailure notification
    /// </summary>
    private static string BuildProfileFailureEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Profile Execution Failed</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Profile {{ ProfileName }} execution failed.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px; color:#dc2626;"">
                  Profile execution failed
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Profile <strong style=""color:#111827;"">{{ ProfileName }}</strong> encountered an error during execution.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fef2f2; border:1px solid #fecaca; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7f1d1d;"">
                    <strong>Error:</strong> {{ ErrorMessage }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Execution ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ExecutionId }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for JobSuccess notification
    /// </summary>
    private static string BuildJobSuccessEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Job Completed Successfully</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Job {{ JobName }} completed successfully.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  Job completed successfully
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Job <strong style=""color:#111827;"">{{ JobName }}</strong> has completed successfully.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Job ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ JobId }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for JobFailure notification
    /// </summary>
    private static string BuildJobFailureEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Job Failed</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Job {{ JobName }} failed.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px; color:#dc2626;"">
                  Job failed
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                Job <strong style=""color:#111827;"">{{ JobName }}</strong> encountered an error.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fef2f2; border:1px solid #fecaca; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7f1d1d;"">
                    <strong>Error:</strong> {{ ErrorMessage }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Job ID
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ JobId }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for NewUser notification
    /// </summary>
    private static string BuildNewUserEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>New User Created</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    New user {{ Username }} created.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  New user created
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                A new user account has been created in the system.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Username
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ Username }}
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Email
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ Email }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for NewApiKey notification
    /// </summary>
    private static string BuildNewApiKeyEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>New API Key Created</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    New API key {{ KeyName }} created.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  New API key created
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                A new API key has been created in the system.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Key Name
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ KeyName }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#eff6ff; border:1px solid #dbeafe; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#1e3a8a;"">
                    Please ensure this API key is stored securely and rotated regularly according to your security policy.
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for NewWebhook notification
    /// </summary>
    private static string BuildNewWebhookEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>New Webhook Created</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    New webhook {{ WebhookName }} created.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px;"">
                  New webhook created
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                A new webhook has been created in the system.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Webhook Name
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ WebhookName }}
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Build default email body for DatabaseSizeThreshold notification
    /// </summary>
    private static string BuildDatabaseSizeThresholdEmailBody()
    {
        return @"
<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <meta name=""x-apple-disable-message-reformatting"">
  <title>Database Size Alert</title>
  <style>
    @media (max-width: 620px) {
      .container { width: 100% !important; }
      .px { padding-left: 16px !important; padding-right: 16px !important; }
      .stack { display: block !important; width: 100% !important; }
      .right { text-align: left !important; }
    }
  </style>
</head>

<body style=""margin:0; padding:0; background:#f6f7fb;"">
  <div style=""display:none; font-size:1px; line-height:1px; max-height:0; max-width:0; opacity:0; overflow:hidden;"">
    Database size threshold exceeded.
  </div>

  <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%"" style=""background:#f6f7fb;"">
    <tr>
      <td align=""center"" style=""padding:24px 12px;"">
        <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""600"" class=""container""
               style=""width:600px; max-width:600px; background:#ffffff; border:1px solid #e9ecf3; border-radius:14px; overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:22px 24px 10px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#111827;"">
                <div style=""font-size:12px; letter-spacing:0.08em; text-transform:uppercase; color:#6b7280;"">
                  System Notification By <span style=""font-weight:600;"">Reef</span>
                </div>
                <div style=""font-size:20px; line-height:1.25; font-weight:650; margin-top:6px; color:#dc2626;"">
                  Database size critical
                </div>
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; color:#374151; font-size:14px; line-height:1.6;"">
                The database has exceeded the configured size threshold and requires attention.
              </div>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""border:1px solid #eef1f7; border-radius:12px;"">
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Current Size
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ CurrentMB }} MB
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Threshold
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#111827; font-weight:600;"">
                    {{ ThresholdMB }} MB
                  </td>
                </tr>
                <tr>
                  <td class=""stack"" style=""padding:14px 16px; border-top:1px solid #eef1f7; width:40%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#6b7280;"">
                    Excess
                  </td>
                  <td class=""stack right"" align=""right""
                      style=""padding:14px 16px; border-top:1px solid #eef1f7; width:60%; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; color:#dc2626; font-weight:600;"">
                    +{{ ExcessMB }} MB
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 18px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""
                     style=""background:#fef2f2; border:1px solid #fecaca; border-radius:12px;"">
                <tr>
                  <td style=""padding:14px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:13px; line-height:1.6; color:#7f1d1d;"">
                    <strong>Action Required:</strong> Consider archiving old data, increasing storage capacity, or adjusting the threshold limit.
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          {{~ if EnableCTA ~}}
          <tr>
            <td class=""px"" style=""padding:6px 24px 22px 24px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""left"">
                <tr>
                  <td bgcolor=""#111827"" style=""border-radius:10px;"">
                    <a href=""{{ CTAUrl }}""
                       target=""_blank""
                       style=""display:inline-block; padding:12px 16px; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:14px; font-weight:650; color:#ffffff; text-decoration:none; border-radius:10px;"">
                      {{ CTAButtonText }}
                    </a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <tr>
            <td class=""px"" style=""padding:0 24px 22px 24px;"">
              <div style=""font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif; font-size:12px; line-height:1.6; color:#6b7280;"">
                If the button doesn't work, use this link:
                <a href=""{{ CTAUrl }}"" target=""_blank"" style=""color:#111827; text-decoration:underline;"">
                  {{ CTAUrl }}
                </a>
              </div>
            </td>
          </tr>
          {{~ end ~}}
        </table>

        <div style=""height:14px; line-height:14px; font-size:14px;"">&nbsp;</div>
      </td>
    </tr>
  </table>
</body>
</html>";
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