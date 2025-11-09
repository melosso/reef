# Reef Import: Data Schema & Storage

## Overview

This document defines the database schema for the Import mechanism. The design follows Reef's existing patterns:
- **SQLite** as the persistent store
- **Proper indexing** for query performance
- **Foreign key constraints** for referential integrity
- **Encryption** for sensitive data at rest
- **Temporal columns** for audit trails

All new tables are **additive**—no modifications to existing export tables.

---

## 1. Import Profile Table

Stores import profile definitions (reuses base from DataProfile abstraction).

```sql
CREATE TABLE ImportProfiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name NVARCHAR(255) NOT NULL,
    Description NVARCHAR(1000),

    -- Lifecycle
    Enabled BOOLEAN NOT NULL DEFAULT 1,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CreatedByUserId INTEGER NOT NULL,
    UpdatedByUserId INTEGER NOT NULL,
    DeletedAt DATETIME,  -- Soft delete support

    -- Source Configuration
    SourceType NVARCHAR(50) NOT NULL,  -- RestApi, S3, Ftp, Sftp, Database, File
    SourceConnectionId INTEGER,  -- Foreign key to Connections (nullable for REST APIs)
    SourceUri NVARCHAR(2048) NOT NULL,  -- API URL, S3 path, FTP path, database table
    SourceConfiguration NVARCHAR(MAX) NOT NULL,  -- JSON: pagination, auth, filters, etc.

    -- Destination Configuration
    DestinationConnectionId INTEGER NOT NULL,  -- Foreign key to Connections
    DestinationType NVARCHAR(50) NOT NULL,  -- Database, File, S3, Ftp, Sftp
    DestinationUri NVARCHAR(2048) NOT NULL,  -- Table name, file path, S3 path
    DestinationConfiguration NVARCHAR(MAX) NOT NULL,  -- JSON: format, UPSERT logic, etc.

    -- Scheduling
    CronExpression NVARCHAR(100),  -- "0 2 * * *" for 2 AM daily
    IntervalSeconds INTEGER,  -- Interval in seconds (e.g., 3600 for hourly)
    UseWebhook BOOLEAN NOT NULL DEFAULT 0,
    WebhookSecret NVARCHAR(255),  -- HMAC secret for webhook signature

    -- Delta Sync Configuration
    TrackChanges BOOLEAN NOT NULL DEFAULT 1,
    DeltaSyncMode NVARCHAR(50) NOT NULL DEFAULT 'Hash',  -- Hash, Timestamp, Key, None, Incremental
    DeltaSyncKeyColumns NVARCHAR(500),  -- Comma-separated key columns for key-based sync
    DeltaSyncTimestampColumn NVARCHAR(255),  -- Column name for timestamp-based sync

    -- Transformation
    PreProcessTemplate NVARCHAR(MAX),  -- Scriban template for pre-fetch transformation
    PostProcessTemplate NVARCHAR(MAX),  -- Scriban template for post-write transformation

    -- Data Mapping & Validation (JSON arrays)
    FieldMappings NVARCHAR(MAX),  -- JSON array of FieldMapping objects
    ValidationRules NVARCHAR(MAX),  -- JSON array of ValidationRule objects

    -- Error Handling
    ErrorStrategy NVARCHAR(50) NOT NULL DEFAULT 'Skip',  -- Skip, Fail, Retry, Quarantine
    MaxRetries INTEGER NOT NULL DEFAULT 3,
    RetryDelaySeconds INTEGER NOT NULL DEFAULT 30,

    -- Logging & Audit
    LogDetailedErrors BOOLEAN NOT NULL DEFAULT 0,
    ExecutionHistoryRetentionDays INTEGER NOT NULL DEFAULT 90,

    -- Metadata
    Tags NVARCHAR(500),  -- Comma-separated tags for organization
    OwnerUserId INTEGER,  -- User responsible for this profile

    CONSTRAINT FK_ImportProfiles_SourceConnection FOREIGN KEY (SourceConnectionId)
        REFERENCES Connections(Id) ON DELETE SET NULL,
    CONSTRAINT FK_ImportProfiles_DestinationConnection FOREIGN KEY (DestinationConnectionId)
        REFERENCES Connections(Id) ON DELETE RESTRICT,
    CONSTRAINT FK_ImportProfiles_CreatedBy FOREIGN KEY (CreatedByUserId)
        REFERENCES Users(Id),
    CONSTRAINT FK_ImportProfiles_UpdatedBy FOREIGN KEY (UpdatedByUserId)
        REFERENCES Users(Id),
    CONSTRAINT FK_ImportProfiles_Owner FOREIGN KEY (OwnerUserId)
        REFERENCES Users(Id) ON DELETE SET NULL
);

-- Indexes for common queries
CREATE INDEX IDX_ImportProfiles_Enabled ON ImportProfiles(Enabled, DeletedAt);
CREATE INDEX IDX_ImportProfiles_SourceType ON ImportProfiles(SourceType);
CREATE INDEX IDX_ImportProfiles_DestinationType ON ImportProfiles(DestinationType);
CREATE INDEX IDX_ImportProfiles_Owner ON ImportProfiles(OwnerUserId);
CREATE INDEX IDX_ImportProfiles_CreatedAt ON ImportProfiles(CreatedAt DESC);
CREATE UNIQUE INDEX IDX_ImportProfiles_Name ON ImportProfiles(Name)
    WHERE DeletedAt IS NULL;  -- Name must be unique for active profiles
```

### Sample SourceConfiguration (JSON)

```json
{
  "restApi": {
    "paginationType": "cursor",
    "nextCursorPath": "data.pagination.next_cursor",
    "pageSize": 100,
    "maxPages": 1000,
    "authenticationToken": "encrypted:aes_key_id_123",
    "customHeaders": {
      "X-API-Version": "2024-01-01",
      "User-Agent": "Reef/1.0"
    },
    "timeoutSeconds": 30,
    "dataPath": "data.items"
  },
  "s3": {
    "bucketName": "data-bucket",
    "objectKeyPrefix": "exports/",
    "filePattern": "*.csv",
    "awsAccessKeyId": "encrypted:aes_key_id_456",
    "awsSecretAccessKey": "encrypted:aes_key_id_789",
    "region": "us-east-1",
    "maxObjectsPerFetch": 100
  },
  "database": {
    "query": "SELECT * FROM source_table WHERE updated_at > @last_sync",
    "timeoutSeconds": 300
  }
}
```

### Sample DestinationConfiguration (JSON)

```json
{
  "database": {
    "tableName": "imported_data",
    "upsertMode": "Upsert",
    "keyColumns": "id,source_id",
    "createTableIfNotExists": false
  },
  "file": {
    "format": "csv",
    "append": false,
    "createDirectoryIfNotExists": true,
    "encoding": "UTF-8"
  }
}
```

### Sample FieldMappings (JSON)

```json
[
  {
    "sourceColumn": "api_id",
    "destinationColumn": "user_id",
    "dataType": "int",
    "required": true,
    "defaultValue": null,
    "transformationTemplate": null
  },
  {
    "sourceColumn": "email",
    "destinationColumn": "email_address",
    "dataType": "string",
    "required": true,
    "defaultValue": null,
    "transformationTemplate": null
  },
  {
    "sourceColumn": "created_at",
    "destinationColumn": "created_date",
    "dataType": "datetime",
    "required": true,
    "defaultValue": null,
    "transformationTemplate": "{{ value | date: 'yyyy-MM-dd' }}"
  },
  {
    "sourceColumn": null,
    "destinationColumn": "import_hash",
    "dataType": "string",
    "required": false,
    "defaultValue": null,
    "transformationTemplate": "{{ row | crypto.sha256 }}"
  }
]
```

---

## 2. Import Jobs Table

Scheduled execution definitions (mirrors ExportJobs pattern).

```sql
CREATE TABLE ImportJobs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ImportProfileId INTEGER NOT NULL,

    -- Schedule Configuration
    CronExpression NVARCHAR(100),  -- NULL if using interval
    IntervalSeconds INTEGER,  -- NULL if using cron

    -- Webhook Configuration
    UseWebhook BOOLEAN NOT NULL DEFAULT 0,
    WebhookEndpoint NVARCHAR(2048),  -- POST endpoint for webhook triggers
    WebhookSignatureSecret NVARCHAR(255),  -- HMAC-SHA256 secret

    -- Execution Control
    Enabled BOOLEAN NOT NULL DEFAULT 1,
    IsPaused BOOLEAN NOT NULL DEFAULT 0,
    LastExecutionAt DATETIME,
    NextExecutionAt DATETIME,
    ExecutionCount INTEGER NOT NULL DEFAULT 0,
    FailureCount INTEGER NOT NULL DEFAULT 0,
    LastSuccessAt DATETIME,
    LastFailureAt DATETIME,
    LastFailureReason NVARCHAR(1000),

    -- Circuit Breaker (auto-pause on repeated failures)
    ConsecutiveFailures INTEGER NOT NULL DEFAULT 0,
    CircuitBreakerThreshold INTEGER NOT NULL DEFAULT 10,

    -- Metadata
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CreatedByUserId INTEGER NOT NULL,

    CONSTRAINT FK_ImportJobs_Profile FOREIGN KEY (ImportProfileId)
        REFERENCES ImportProfiles(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ImportJobs_CreatedBy FOREIGN KEY (CreatedByUserId)
        REFERENCES Users(Id)
);

CREATE INDEX IDX_ImportJobs_ProfileId ON ImportJobs(ImportProfileId);
CREATE INDEX IDX_ImportJobs_Enabled ON ImportJobs(Enabled);
CREATE INDEX IDX_ImportJobs_NextExecution ON ImportJobs(NextExecutionAt)
    WHERE Enabled = 1 AND IsPaused = 0;
```

---

## 3. Import Executions Table

Execution history and detailed logs (mirrors ProfileExecutions pattern).

```sql
CREATE TABLE ImportExecutions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ExecutionId UNIQUEIDENTIFIER NOT NULL,  -- UUID for tracking across logs
    ImportProfileId INTEGER NOT NULL,
    ImportJobId INTEGER,  -- NULL if manually triggered

    -- Execution Status
    Status NVARCHAR(50) NOT NULL,  -- Pending, Running, Success, Failed, Cancelled
    StartedAt DATETIME NOT NULL,
    CompletedAt DATETIME,
    DurationSeconds INTEGER,

    -- Trigger Information
    TriggerType NVARCHAR(50) NOT NULL,  -- Manual, Scheduled, Webhook
    TriggeredByUserId INTEGER,
    TriggeredByIpAddress NVARCHAR(45),

    -- Data Statistics
    RowsFetched INTEGER NOT NULL DEFAULT 0,
    RowsWritten INTEGER NOT NULL DEFAULT 0,
    RowsFailed INTEGER NOT NULL DEFAULT 0,
    RowsSkipped INTEGER NOT NULL DEFAULT 0,

    -- Delta Sync Statistics
    NewRowsDetected INTEGER NOT NULL DEFAULT 0,
    ChangedRowsDetected INTEGER NOT NULL DEFAULT 0,
    UnchangedRowsDetected INTEGER NOT NULL DEFAULT 0,

    -- Error Details
    ErrorMessage NVARCHAR(MAX),  -- Encrypted if LogDetailedErrors is true
    StackTrace NVARCHAR(MAX),  -- Only if LogDetailedErrors is true
    DetailedErrors NVARCHAR(MAX),  -- JSON array of individual row errors

    -- Performance Metrics
    SourceReadTimeMs INTEGER,
    DeltaSyncTimeMs INTEGER,
    TransformTimeMs INTEGER,
    WriteTimeMs INTEGER,
    TotalTimeMs INTEGER,

    -- Validation & Output
    ValidationErrorCount INTEGER NOT NULL DEFAULT 0,
    QuarantinedRowsCount INTEGER NOT NULL DEFAULT 0,
    QuarantineLocation NVARCHAR(2048),  -- Path to quarantine file

    -- Retry Information
    RetryCount INTEGER NOT NULL DEFAULT 0,
    ParentExecutionId UNIQUEIDENTIFIER,  -- For retry tracking

    -- Output Metadata
    OutputLocation NVARCHAR(2048),  -- Where data was written
    OutputFileSize BIGINT,  -- In bytes
    OutputChecksum NVARCHAR(64),  -- SHA256 of output

    -- Audit & Compliance
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    AuditTrail NVARCHAR(MAX),  -- JSON array of audit events
    ComplianceFlags NVARCHAR(500),  -- E.g., "GDPR_DATA_PROCESSED", "SENSITIVE_DATA"

    CONSTRAINT FK_ImportExecutions_Profile FOREIGN KEY (ImportProfileId)
        REFERENCES ImportProfiles(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ImportExecutions_Job FOREIGN KEY (ImportJobId)
        REFERENCES ImportJobs(Id) ON DELETE SET NULL,
    CONSTRAINT FK_ImportExecutions_TriggeredBy FOREIGN KEY (TriggeredByUserId)
        REFERENCES Users(Id) ON DELETE SET NULL,
    CONSTRAINT FK_ImportExecutions_Parent FOREIGN KEY (ParentExecutionId)
        REFERENCES ImportExecutions(ExecutionId) ON DELETE SET NULL
);

CREATE INDEX IDX_ImportExecutions_ProfileId ON ImportExecutions(ImportProfileId, StartedAt DESC);
CREATE INDEX IDX_ImportExecutions_Status ON ImportExecutions(Status);
CREATE INDEX IDX_ImportExecutions_ExecutionId ON ImportExecutions(ExecutionId);
CREATE INDEX IDX_ImportExecutions_TriggeredBy ON ImportExecutions(TriggeredByUserId);
CREATE INDEX IDX_ImportExecutions_CreatedAt ON ImportExecutions(CreatedAt DESC);
CREATE INDEX IDX_ImportExecutions_TimeRange ON ImportExecutions(StartedAt DESC, CompletedAt DESC);
```

### Sample DetailedErrors (JSON)

```json
[
  {
    "rowIndex": 5,
    "rowData": {
      "api_id": "12345",
      "email": "invalid-email"
    },
    "errorMessage": "Invalid email format",
    "errorType": "ValidationError",
    "timestamp": "2025-11-09T14:32:15Z"
  },
  {
    "rowIndex": 12,
    "rowData": {
      "api_id": "67890",
      "phone": "+1-555-0100"
    },
    "errorMessage": "Phone format not supported",
    "errorType": "TransformationError",
    "timestamp": "2025-11-09T14:32:16Z"
  }
]
```

### Sample AuditTrail (JSON)

```json
[
  {
    "timestamp": "2025-11-09T14:32:00Z",
    "event": "EXECUTION_STARTED",
    "details": "Import triggered by manual run"
  },
  {
    "timestamp": "2025-11-09T14:32:05Z",
    "event": "SOURCE_CONNECTED",
    "details": "Connected to REST API https://api.example.com/users"
  },
  {
    "timestamp": "2025-11-09T14:32:15Z",
    "event": "DATA_FETCHED",
    "details": "1000 rows fetched from source"
  },
  {
    "timestamp": "2025-11-09T14:32:20Z",
    "event": "DELTA_SYNC_COMPLETED",
    "details": "750 new rows, 150 changed rows, 100 unchanged"
  },
  {
    "timestamp": "2025-11-09T14:32:30Z",
    "event": "WRITE_COMPLETED",
    "details": "900 rows written to database successfully"
  },
  {
    "timestamp": "2025-11-09T14:32:35Z",
    "event": "EXECUTION_COMPLETED",
    "details": "Success"
  }
]
```

---

## 4. Delta Sync State Table (Extended)

Reuses existing `DeltaSyncState` table with import-specific records.

```sql
-- Existing table in Reef (NO CHANGES)
CREATE TABLE DeltaSyncState (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProfileId INTEGER NOT NULL,  -- Foreign key (flexible: can reference Export OR Import)
    DataDirection NVARCHAR(50) NOT NULL,  -- 'Export' or 'Import'
    RowHash NVARCHAR(64) NOT NULL,  -- SHA256 hash of row
    RowKey NVARCHAR(500),  -- Composite key for key-based sync
    LastSyncedAt DATETIME NOT NULL,
    SourceChecksum NVARCHAR(64),  -- CRC or hash from source
    DestinationChecksum NVARCHAR(64),  -- CRC or hash from destination
    Metadata NVARCHAR(MAX),  -- JSON: additional context

    CONSTRAINT PK_DeltaSyncState PRIMARY KEY (ProfileId, DataDirection, RowHash),
    INDEX IDX_DeltaSyncState_ProfileDirection ON (ProfileId, DataDirection, LastSyncedAt)
);

-- When an import profile is deleted, clean up delta sync state:
-- DELETE FROM DeltaSyncState WHERE ProfileId = @profileId AND DataDirection = 'Import'
```

---

## 5. Data Source Configuration Table (Optional)

For reusable source configs (e.g., shared API credentials, S3 paths).

```sql
CREATE TABLE DataSourceConfigs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name NVARCHAR(255) NOT NULL UNIQUE,
    Description NVARCHAR(1000),

    DataSourceType NVARCHAR(50) NOT NULL,  -- RestApi, S3, Ftp, Sftp, Database, File
    Configuration NVARCHAR(MAX) NOT NULL,  -- JSON: auth, endpoints, paths, etc. (encrypted)

    IsShared BOOLEAN NOT NULL DEFAULT 0,  -- Can be reused by multiple profiles
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CreatedByUserId INTEGER NOT NULL,

    CONSTRAINT FK_DataSourceConfigs_CreatedBy FOREIGN KEY (CreatedByUserId)
        REFERENCES Users(Id)
);

CREATE INDEX IDX_DataSourceConfigs_Type ON DataSourceConfigs(DataSourceType);
CREATE INDEX IDX_DataSourceConfigs_Shared ON DataSourceConfigs(IsShared);
```

---

## 6. Import Alerts & Notifications (Optional)

For future notification system.

```sql
CREATE TABLE ImportNotifications (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ImportProfileId INTEGER NOT NULL,

    NotificationType NVARCHAR(50) NOT NULL,  -- OnSuccess, OnFailure, OnSlowExecution, OnHighErrorRate
    NotificationChannel NVARCHAR(50) NOT NULL,  -- Email, Slack, Webhook, PagerDuty
    NotificationTarget NVARCHAR(255) NOT NULL,  -- Email address, Slack channel, webhook URL

    IsEnabled BOOLEAN NOT NULL DEFAULT 1,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT FK_ImportNotifications_Profile FOREIGN KEY (ImportProfileId)
        REFERENCES ImportProfiles(Id) ON DELETE CASCADE
);
```

---

## 7. Migration Strategy

### Phase 1: Initial Schema Creation

```csharp
// DatabaseInitializer.cs - New method
public async Task InitializeImportTablesAsync()
{
    using var connection = new SqliteConnection(_connectionString);
    await connection.OpenAsync();

    var commands = new[]
    {
        CreateImportProfilesTable(),
        CreateImportJobsTable(),
        CreateImportExecutionsTable(),
        CreateDataSourceConfigsTable(),
        CreateImportNotificationsTable()
    };

    foreach (var command in commands)
    {
        using var cmd = new SqliteCommand(command, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    // Create indexes
    await CreateIndexesAsync(connection);
}

private string CreateImportProfilesTable()
{
    return @"
        CREATE TABLE IF NOT EXISTS ImportProfiles (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ...
        );
    ";
}

// Call in Startup:
await _databaseInitializer.InitializeImportTablesAsync();
```

### Phase 2: Data Migration (if upgrading existing Reef)

None required—import schema is completely additive.

### Phase 3: Encryption at Rest

All sensitive fields (API keys, passwords, etc.) encrypted using existing encryption service:

```csharp
// In DatabaseInitializer or migration service
public async Task EncryptSensitiveFieldsAsync()
{
    var profiles = await _db.ImportProfiles.ToListAsync();
    foreach (var profile in profiles)
    {
        var sourceConfig = JsonDocument.Parse(profile.SourceConfiguration);
        // Encrypt auth tokens, API keys, passwords in JSON
        var encrypted = EncryptJson(sourceConfig);
        profile.SourceConfiguration = encrypted;
        await _db.SaveChangesAsync();
    }
}
```

---

## 8. Backup & Disaster Recovery

### Backup Strategy

```sql
-- Full backup of import tables
BACKUP DATABASE Reef
  TO DISK = 'backup/reef_imports_full_20250109.bak';

-- Incremental backup (daily)
BACKUP DATABASE Reef
  TO DISK = 'backup/reef_imports_incr_20250109.bak'
  WITH INCREMENTAL;

-- Backup Delta Sync State separately (critical)
SELECT * FROM DeltaSyncState
WHERE DataDirection = 'Import'
INTO OUTFILE 'backup/delta_sync_state_export.json'
FORMAT JSON;
```

### Recovery Procedure

```
1. Restore from full backup
2. Restore incremental backups in order
3. Verify delta sync state integrity
4. Test import execution
5. Validate data consistency
```

---

## 9. Data Retention & Cleanup

### Automatic Cleanup Policies

```sql
-- Delete old execution records (based on ExecutionHistoryRetentionDays)
DELETE FROM ImportExecutions
WHERE ImportProfileId IN (
    SELECT Id FROM ImportProfiles
    WHERE LogDetailedErrors = 0
)
AND CompletedAt < DATEADD(DAY, -90, GETDATE());

-- Delete orphaned delta sync state (no matching profile)
DELETE FROM DeltaSyncState
WHERE DataDirection = 'Import'
AND ProfileId NOT IN (SELECT Id FROM ImportProfiles WHERE DeletedAt IS NULL);

-- Clean up quarantine files older than 30 days
-- (Application-level cleanup in service)
```

### Compliance & Audit

All deletions are **logged** in an audit table:

```sql
CREATE TABLE AuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Operation NVARCHAR(50),  -- CREATE, UPDATE, DELETE, READ
    TableName NVARCHAR(100),
    RecordId INTEGER,
    UserId INTEGER,
    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    OldValue NVARCHAR(MAX),
    NewValue NVARCHAR(MAX),
    IpAddress NVARCHAR(45),
    Details NVARCHAR(MAX)
);
```

---

## 10. Performance Considerations

### Query Optimization

**High-frequency queries**:
1. Get enabled profiles for scheduling
2. Get execution history for a profile
3. Get delta sync state for a profile

**Indexes created**:
```sql
-- Profile lookup
CREATE INDEX IDX_ImportProfiles_Enabled_Deleted
ON ImportProfiles(Enabled, DeletedAt);

-- Execution history
CREATE INDEX IDX_ImportExecutions_ProfileId_StartedAt
ON ImportExecutions(ImportProfileId, StartedAt DESC);

-- Delta sync state
CREATE INDEX IDX_DeltaSyncState_ProfileDirection_LastSynced
ON DeltaSyncState(ProfileId, DataDirection, LastSyncedAt);

-- Job scheduling
CREATE INDEX IDX_ImportJobs_NextExecution
ON ImportJobs(NextExecutionAt)
WHERE Enabled = 1 AND IsPaused = 0;
```

### Partitioning Strategy (for large deployments)

```sql
-- Partition ImportExecutions by date for faster queries
ALTER TABLE ImportExecutions
ADD CONSTRAINT PartitionDate CHECK (CompletedAt >= '2024-01-01');

-- Create monthly partitions
CREATE TABLE ImportExecutions_2025_01 LIKE ImportExecutions;
CREATE TABLE ImportExecutions_2025_02 LIKE ImportExecutions;
-- ... and so on
```

---

## 11. Schema Validation & Testing

### Unit Test Example

```csharp
[Test]
public async Task ImportProfiles_Creation_ValidatesConstraints()
{
    var profile = new ImportProfile
    {
        Name = "Test Profile",
        SourceType = DataSourceType.RestApi,
        SourceUri = "https://api.example.com/data",
        DestinationType = DestinationType.Database,
        DestinationUri = "imported_data",
        DestinationConnectionId = 1
    };

    var result = await _profileRepository.CreateAsync(profile);

    Assert.That(result.Id, Is.GreaterThan(0));
    Assert.That(result.CreatedAt, Is.Not.EqualTo(DateTime.MinValue));
}

[Test]
public async Task ImportExecutions_DeltaSyncState_Updates()
{
    var execution = new ImportExecution
    {
        ImportProfileId = 1,
        Status = ExecutionStatus.Success,
        RowsWritten = 100
    };

    await _db.ImportExecutions.AddAsync(execution);
    await _db.SaveChangesAsync();

    var deltaSyncStates = await _db.DeltaSyncState
        .Where(d => d.ProfileId == 1 && d.DataDirection == "Import")
        .ToListAsync();

    Assert.That(deltaSyncStates, Is.Not.Empty);
}
```

---

## 12. Schema Diagram (Text Format)

```
Connections (existing)
    ↑
    ├─→ ImportProfiles (new)
    │       ├─→ ImportJobs (new)
    │       │    └─→ ImportExecutions (new)
    │       └─→ DeltaSyncState (extended, shared)
    │           └─→ Stores row hashes for both Import & Export
    │
    └─→ ExportProfiles (existing)

Users (existing)
    ↑
    ├─→ ImportProfiles.CreatedByUserId
    ├─→ ImportJobs.CreatedByUserId
    ├─→ ImportExecutions.TriggeredByUserId
    └─→ ExportProfiles.CreatedByUserId
```

---

## 13. Storage Requirements

### Expected Storage Size

**For 10,000 import profiles with daily executions over 1 year**:

| Table | Rows | Approx. Size |
|-------|------|--------------|
| ImportProfiles | 10,000 | 50 MB |
| ImportJobs | 15,000 | 30 MB |
| ImportExecutions | 3,650,000 | 10 GB |
| DeltaSyncState | 50,000,000 | 20 GB |
| **Total** | **3.7M** | **30 GB** |

**Retention recommendations**:
- ImportExecutions: 90 days (configurable per profile)
- DeltaSyncState: Keep indefinitely (compress after 1 year)
- ImportProfiles/Jobs: Soft delete, archive after 1 year

---

## Summary

✅ **Additive**: No changes to existing schema
✅ **Secure**: Encryption at rest, audit trails
✅ **Performant**: Proper indexing, query optimization
✅ **Extensible**: Future columns for new features
✅ **Compliant**: GDPR-friendly (soft deletes, retention policies)
✅ **Observable**: Comprehensive execution history and audit logs

**Document Version**: 1.0
**Status**: Design Phase
**Next Step**: Implement database initializer and migration scripts
