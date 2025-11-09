# Reef Import: Execution Flow & Examples

## Overview

This document details the complete execution flow for import profiles, with real-world examples, timing metrics, and edge case handling.

---

## 1. High-Level Execution Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                   Import Trigger                                │
│  Manual | Scheduled (Cron/Interval) | Webhook                  │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 1. VALIDATION (200-500ms)                                       │
│   ├─ Load profile from database                                 │
│   ├─ Validate profile configuration                            │
│   ├─ Test source connectivity (quick check)                    │
│   └─ Test destination connectivity (quick check)               │
│   Status: If validation fails → ABORT, log error              │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 2. PRE-FETCH TRANSFORMATION (50-200ms)                          │
│   └─ Apply Scriban pre-process template (if defined)           │
│   Status: Prepare request parameters, filters, auth            │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 3. SOURCE DATA READ (1-30s depending on data size)              │
│   ├─ Instantiate DataSourceExecutor                            │
│   │  ├─ RestDataSourceExecutor                                │
│   │  ├─ S3DataSourceExecutor                                  │
│   │  ├─ FtpDataSourceExecutor                                 │
│   │  ├─ DatabaseDataSourceExecutor                            │
│   │  └─ FileDataSourceExecutor                                │
│   ├─ Fetch data with retry logic (exponential backoff)        │
│   ├─ Stream results to avoid memory explosion                 │
│   └─ Return: List<Dictionary<string, object>>                 │
│   Status: If source read fails → retry or fail based on config│
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 4. DELTA SYNC (100ms - 5s depending on row count)               │
│   ├─ Load previous DeltaSyncState from database               │
│   ├─ For each row:                                             │
│   │  ├─ Calculate SHA256 hash (or compare timestamp/key)     │
│   │  ├─ Lookup in DeltaSyncState                             │
│   │  └─ Classify: NEW | CHANGED | UNCHANGED                  │
│   ├─ Filter: Keep only NEW + CHANGED rows                     │
│   └─ Return classification results                            │
│   Status: Skip rows not matching delta criteria               │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 5. ROW-LEVEL TRANSFORMATION (500ms - 10s)                       │
│   ├─ For each (NEW + CHANGED) row:                             │
│   │  ├─ Apply field mappings (source col → dest col)         │
│   │  ├─ Execute row transformation template (Scriban)        │
│   │  ├─ Type conversion (string → int/datetime/decimal)      │
│   │  ├─ Apply validation rules                               │
│   │  └─ Add default values if missing                        │
│   │─ On error:                                                │
│   │  ├─ If ErrorStrategy=Fail → abort entire profile        │
│   │  ├─ If ErrorStrategy=Skip → log row, continue           │
│   │  └─ If ErrorStrategy=Quarantine → write to quarantine   │
│   └─ Return: transformed rows                                 │
│   Status: Log transformation metrics                          │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 6. SCHEMA VALIDATION (100-500ms)                                │
│   ├─ For each ValidationRule:                                  │
│   │  ├─ Type conformance                                     │
│   │  ├─ Required field check                                 │
│   │  ├─ Min/max values                                       │
│   │  ├─ Regex pattern matching                               │
│   │  └─ Enum/allowed values                                  │
│   │─ On validation error:                                     │
│   │  ├─ If ErrorStrategy=Fail → abort entire profile        │
│   │  └─ If ErrorStrategy=Skip → quarantine row               │
│   └─ Return: validated rows, error list                       │
│   Status: Validation error count                              │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 7. WRITE TO DESTINATION (1-30s depending on row count)          │
│   ├─ Instantiate appropriate Writer                            │
│   │  ├─ DatabaseWriter (INSERT or UPSERT)                    │
│   │  ├─ FileWriter (CSV, JSON)                               │
│   │  ├─ S3Writer (multipart upload)                          │
│   │  ├─ FtpWriter (SFTP upload)                              │
│   │  └─ AzureWriter, GCSWriter, etc.                         │
│   ├─ Open destination connection (with retry)                │
│   ├─ Begin transaction (if ACID-compliant destination)       │
│   ├─ For each row:                                            │
│   │  ├─ Perform UPSERT (INSERT or UPDATE)                   │
│   │  ├─ Handle destination-specific errors                  │
│   │  └─ Retry transient errors (network timeout, locks)    │
│   ├─ Commit transaction                                       │
│   └─ Return: write_count, error_count                         │
│   Status: If write fails → rollback transaction               │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 8. POST-WRITE TRANSFORMATION (50-500ms)                         │
│   ├─ Apply Scriban post-process template (if defined)         │
│   ├─ Example use cases:                                        │
│   │  ├─ Trigger downstream exports                          │
│   │  ├─ Send notifications (email, Slack, webhook)          │
│   │  ├─ Update parent/child records                         │
│   │  └─ Archive source file                                 │
│   └─ Status: Log any post-processing errors (non-blocking)   │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
┌─────────────────────────────────────────────────────────────────┐
│ 9. COMMIT & AUDIT (200-500ms)                                   │
│   ├─ Update DeltaSyncState with row hashes (ONLY on success) │
│   ├─ Insert ImportExecution record                            │
│   │  ├─ Status: SUCCESS or FAILED                           │
│   │  ├─ Metrics: duration, row counts, errors              │
│   │  ├─ Audit trail: step-by-step events                  │
│   │  └─ Detailed errors (if configured)                    │
│   ├─ Update ImportJob.LastExecutionAt, NextExecutionAt      │
│   ├─ Update circuit breaker state                           │
│   └─ Return: ImportExecutionResult                           │
│   Status: Always commit execution record (for auditability)  │
└──────────────────────┬──────────────────────────────────────────┘
                       │
                       ↓
           ┌───────────┴───────────┐
           ↓                       ↓
    ┌─────────────┐         ┌────────────┐
    │   SUCCESS   │         │   FAILED   │
    ├─────────────┤         ├────────────┤
    │ Log success │         │ Log error  │
    │ metrics     │         │ details    │
    │ Trigger OK  │         │ Retry?     │
    │ webhook     │         │ Increment  │
    │ (optional)  │         │ failure cnt│
    └─────────────┘         └────────────┘
```

---

## 2. Detailed Code Execution Flow

### Step 1: Profile Loading & Validation

```csharp
public async Task<ImportExecutionResult> ExecuteAsync(
    ImportProfile profile,
    ExecutionContext context,
    CancellationToken cancellationToken = default)
{
    var executionId = Guid.NewGuid();
    var sw = Stopwatch.StartNew();
    var result = new ImportExecutionResult { ProfileId = profile.Id };

    try
    {
        // STAGE 1: VALIDATION (200-500ms)
        await LogStageAsync(executionId, "VALIDATION", "Starting validation...");

        // Validate profile configuration
        profile.Validate();  // Throws ValidationException if invalid

        // Quick connectivity test (HEAD request for REST, List for S3, etc.)
        var sourceConnected = await TestSourceConnectionAsync(profile);
        if (!sourceConnected)
            throw new ConnectionException("Source connection failed");

        var destConnected = await TestDestinationConnectionAsync(profile);
        if (!destConnected)
            throw new ConnectionException("Destination connection failed");

        await LogStageAsync(executionId, "VALIDATION", "Validation passed ✓");

        // ... (continue to next stages)
    }
    catch (Exception ex)
    {
        // Error handling
        result.Status = ExecutionStatus.Failed;
        result.ErrorMessage = ex.Message;
        await LogAsync(executionId, "ERROR", ex.Message, isError: true);
        return result;
    }
}
```

### Step 2-3: Pre-Fetch Transformation & Source Data Read

```csharp
// STAGE 2: PRE-FETCH TRANSFORMATION (50-200ms)
if (!string.IsNullOrEmpty(profile.PreProcessTemplate))
{
    await LogStageAsync(executionId, "PRE_PROCESS", "Applying template...");

    var templateContext = new Dictionary<string, object>
    {
        { "execution_context", context },
        { "last_sync_at", profile.LastSyncedAt },
        { "profile", profile }
    };

    // Example: parse API pagination, filter by timestamp
    var preProcessResult = await _templateEngine.RenderAsync(
        profile.PreProcessTemplate,
        templateContext,
        cancellationToken);

    // Use result to modify SourceUri or SourceConfiguration
    // E.g., add ?modified_since=<last_sync_at> to REST API URL
}

// STAGE 3: SOURCE DATA READ (1-30s)
await LogStageAsync(executionId, "SOURCE_READ",
    $"Fetching from {profile.SourceType} ({profile.SourceUri})...");

var sourceExecutor = _sourceFactory.CreateExecutor(profile.SourceType);
var sourceResult = await sourceExecutor.FetchAsync(profile, cancellationToken);

await LogStageAsync(executionId, "SOURCE_READ",
    $"Fetched {sourceResult.Rows.Count} rows from source");

result.SourceReadTimeMs = (int)sw.ElapsedMilliseconds;
```

### Step 4: Delta Sync Classification

```csharp
// STAGE 4: DELTA SYNC (100ms - 5s)
if (profile.TrackChanges && profile.DeltaSyncMode != DeltaSyncMode.None)
{
    await LogStageAsync(executionId, "DELTA_SYNC", "Detecting changes...");
    var deltaSw = Stopwatch.StartNew();

    var (newRows, changedRows, unchangedCount) =
        await _deltaSyncService.ClassifyRowsAsync(
            profile.Id,
            sourceResult.Rows,
            profile.DeltaSyncMode,
            profile.DeltaSyncKeyColumns,
            cancellationToken);

    // Update result with delta sync metrics
    result.NewRowsDetected = newRows.Count;
    result.ChangedRowsDetected = changedRows.Count;
    result.UnchangedRowsDetected = unchangedCount;

    // Filter source result to include only new/changed rows
    sourceResult.Rows = newRows.Concat(changedRows).ToList();

    await LogStageAsync(executionId, "DELTA_SYNC",
        $"Detected: {newRows.Count} new, {changedRows.Count} changed, " +
        $"{unchangedCount} unchanged rows");

    result.DeltaSyncTimeMs = (int)deltaSw.ElapsedMilliseconds;
}
else
{
    await LogStageAsync(executionId, "DELTA_SYNC", "Skipped (not configured)");
}
```

### Step 5: Row-Level Transformation

```csharp
// STAGE 5: ROW-LEVEL TRANSFORMATION (500ms - 10s)
await LogStageAsync(executionId, "TRANSFORM", "Transforming rows...");
var transformSw = Stopwatch.StartNew();

var transformedRows = new List<Dictionary<string, object>>();
var transformationErrors = new List<(int RowIndex, Exception Error)>();

foreach (var (sourceRow, rowIndex) in sourceResult.Rows.WithIndex())
{
    try
    {
        var transformed = await TransformRowAsync(
            profile,
            sourceRow,
            context,
            cancellationToken);

        transformedRows.Add(transformed);
    }
    catch (Exception ex)
    {
        transformationErrors.Add((rowIndex, ex));

        if (profile.ErrorStrategy == ErrorStrategy.Fail)
        {
            await LogStageAsync(executionId, "TRANSFORM",
                $"Row {rowIndex}: {ex.Message}", isError: true);
            throw;  // Abort entire import
        }
        else if (profile.ErrorStrategy == ErrorStrategy.Skip)
        {
            result.RowsSkipped++;
            await LogAsync(executionId, "TRANSFORM_SKIP",
                $"Row {rowIndex}: {ex.Message}");
        }
        else if (profile.ErrorStrategy == ErrorStrategy.Quarantine)
        {
            result.QuarantinedRowsCount++;
            await QuarantineRowAsync(executionId, rowIndex, sourceRow, ex);
        }
    }
}

await LogStageAsync(executionId, "TRANSFORM",
    $"Transformed {transformedRows.Count} rows, " +
    $"{transformationErrors.Count} errors");

result.TransformTimeMs = (int)transformSw.ElapsedMilliseconds;
```

### Step 5a: Transform Row Helper

```csharp
private async Task<Dictionary<string, object>> TransformRowAsync(
    ImportProfile profile,
    Dictionary<string, object> sourceRow,
    ExecutionContext context,
    CancellationToken cancellationToken)
{
    var transformed = new Dictionary<string, object>();

    foreach (var mapping in profile.FieldMappings ?? new List<FieldMapping>())
    {
        var value = sourceRow.ContainsKey(mapping.SourceColumn)
            ? sourceRow[mapping.SourceColumn]
            : null;

        // Apply transformation template if defined
        if (!string.IsNullOrEmpty(mapping.TransformationTemplate))
        {
            var templateContext = new Dictionary<string, object>
            {
                { "value", value },
                { "row", sourceRow },
                { "execution_context", context }
            };

            var renderedValue = await _templateEngine.RenderAsync(
                mapping.TransformationTemplate,
                templateContext,
                cancellationToken);

            value = renderedValue;
        }

        // Type conversion
        try
        {
            value = ConvertType(value, mapping.DataType);
        }
        catch (Exception ex)
        {
            if (mapping.Required)
                throw new TransformationException(
                    $"Cannot convert {mapping.SourceColumn} to {mapping.DataType}: {ex.Message}");
            value = mapping.DefaultValue;
        }

        // Apply default if null
        if (value == null && mapping.DefaultValue != null)
            value = mapping.DefaultValue;

        // Validate required
        if (mapping.Required && (value == null || string.IsNullOrEmpty(value.ToString())))
            throw new ValidationException(
                $"Required field {mapping.DestinationColumn} is null");

        transformed[mapping.DestinationColumn] = value;
    }

    return transformed;
}
```

### Step 6: Schema Validation

```csharp
// STAGE 6: SCHEMA VALIDATION (100-500ms)
await LogStageAsync(executionId, "VALIDATION", "Validating schema...");
var validationSw = Stopwatch.StartNew();

var schemaErrors = new List<(int RowIndex, string ErrorMessage)>();

foreach (var rule in profile.ValidationRules ?? new List<ValidationRule>())
{
    foreach (var (row, rowIndex) in transformedRows.WithIndex())
    {
        try
        {
            ValidateRule(rule, row, rowIndex);
        }
        catch (Exception ex)
        {
            schemaErrors.Add((rowIndex, ex.Message));

            if (profile.ErrorStrategy == ErrorStrategy.Fail)
            {
                throw;
            }
            else
            {
                result.ValidationErrorCount++;
            }
        }
    }
}

await LogStageAsync(executionId, "VALIDATION",
    $"Validation complete: {schemaErrors.Count} errors");

result.ValidationErrorCount = schemaErrors.Count;
result.ValidateTimeMs = (int)validationSw.ElapsedMilliseconds;
```

### Step 7: Write to Destination

```csharp
// STAGE 7: WRITE TO DESTINATION (1-30s)
await LogStageAsync(executionId, "WRITE",
    $"Writing to {profile.DestinationType} ({profile.DestinationUri})...");

var writeSw = Stopwatch.StartNew();
var writer = _writerFactory.CreateWriter(profile.DestinationType);

DataWriteResult writeResult;
try
{
    writeResult = await writer.WriteAsync(
        profile,
        transformedRows,
        cancellationToken);
}
catch (Exception ex)
{
    await LogStageAsync(executionId, "WRITE",
        $"Write failed: {ex.Message}", isError: true);
    throw;
}

result.RowsWritten = writeResult.RowsWritten;
result.RowsFailed += writeResult.RowsFailed;
result.WriteErrors = writeResult.Errors;

await LogStageAsync(executionId, "WRITE",
    $"Wrote {writeResult.RowsWritten} rows, {writeResult.RowsFailed} failures");

result.WriteTimeMs = (int)writeSw.ElapsedMilliseconds;
```

### Step 8-9: Post-Processing & Commit

```csharp
// STAGE 8: POST-WRITE TRANSFORMATION (50-500ms)
if (!string.IsNullOrEmpty(profile.PostProcessTemplate))
{
    await LogStageAsync(executionId, "POST_PROCESS",
        "Executing post-write template...");

    var postContext = new Dictionary<string, object>
    {
        { "execution_context", context },
        { "rows_written", result.RowsWritten },
        { "rows_failed", result.RowsFailed },
        { "execution_result", result }
    };

    var postResult = await _templateEngine.RenderAsync(
        profile.PostProcessTemplate,
        postContext,
        cancellationToken);

    // Post-processing errors are non-blocking
    await LogStageAsync(executionId, "POST_PROCESS", "Completed");
}

// STAGE 9: COMMIT & AUDIT (200-500ms)
await LogStageAsync(executionId, "COMMIT", "Committing changes...");

// CRITICAL: Only update delta sync state after successful write
if (profile.TrackChanges && profile.DeltaSyncMode != DeltaSyncMode.None)
{
    await _deltaSyncService.CommitAsync(
        profile.Id,
        sourceResult.Rows,  // All fetched rows, not just transformed
        cancellationToken);

    await LogAsync(executionId, "COMMIT", "Delta sync state updated");
}

// Log execution record (ALWAYS, even on partial failure)
result.Status = ExecutionStatus.Success;
result.Duration = sw.Elapsed;

var execution = new ImportExecution
{
    ExecutionId = executionId,
    ImportProfileId = profile.Id,
    Status = result.Status,
    StartedAt = DateTime.UtcNow - result.Duration,
    CompletedAt = DateTime.UtcNow,
    DurationSeconds = (int)result.Duration.TotalSeconds,
    RowsFetched = sourceResult.Rows.Count,
    RowsWritten = result.RowsWritten,
    RowsFailed = result.RowsFailed,
    NewRowsDetected = result.NewRowsDetected,
    ChangedRowsDetected = result.ChangedRowsDetected,
    AuditTrail = GenerateAuditTrail(executionId),
    DetailedErrors = result.WriteErrors.Any()
        ? JsonSerializer.Serialize(result.WriteErrors)
        : null
};

await _executionLogger.LogExecutionAsync(executionId, execution);

await LogStageAsync(executionId, "COMMIT", "Execution record logged");
return result;
```

---

## 3. Real-World Example: REST API → SQL Database

### Scenario
Import user data from a paginated REST API into a SQL Server database with delta tracking.

### Profile Configuration

```json
{
  "id": 1,
  "name": "Import Users from API",
  "sourceType": "RestApi",
  "sourceUri": "https://api.example.com/v2/users",
  "sourceConfiguration": {
    "paginationType": "cursor",
    "pageSize": 100,
    "maxPages": 1000,
    "authenticationToken": "Bearer <encrypted_token>",
    "customHeaders": {
      "X-API-Version": "2024-01-01"
    },
    "dataPath": "data.users",
    "nextCursorPath": "data.pagination.next_cursor"
  },
  "destinationType": "Database",
  "destinationUri": "Users",
  "destinationConfiguration": {
    "tableName": "Users",
    "upsertMode": "Upsert",
    "keyColumns": "id"
  },
  "deltaSync": {
    "enabled": true,
    "mode": "Hash"
  },
  "fieldMappings": [
    {
      "sourceColumn": "id",
      "destinationColumn": "user_id",
      "dataType": "int",
      "required": true
    },
    {
      "sourceColumn": "email",
      "destinationColumn": "email_address",
      "dataType": "string",
      "required": true
    },
    {
      "sourceColumn": "created_at",
      "destinationColumn": "created_date",
      "dataType": "datetime",
      "transformationTemplate": "{{ value | date: 'yyyy-MM-dd' }}"
    },
    {
      "sourceColumn": "status",
      "destinationColumn": "user_status",
      "dataType": "string",
      "required": true
    }
  ],
  "validationRules": [
    {
      "columnName": "email_address",
      "validationType": "Regex",
      "pattern": "^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}$"
    },
    {
      "columnName": "user_status",
      "validationType": "Enum",
      "allowedValues": ["active", "inactive", "suspended"]
    }
  ],
  "errorStrategy": "Skip",
  "schedule": {
    "cronExpression": "0 */4 * * *"  // Every 4 hours
  }
}
```

### Execution Timeline (Example)

```
TIME     STAGE                    DURATION    DETAILS
────────────────────────────────────────────────────────────
00:00    START                    -           ExecutionId: 550e8400-e29b-41d4-a716-446655440000
00:00    VALIDATION               250ms       ✓ Profile valid
00:01    VALIDATION               100ms       ✓ Source connectivity OK
00:02    VALIDATION               80ms        ✓ Destination connectivity OK
00:05    PRE_PROCESS              0ms         (no template defined)
00:10    SOURCE_READ              2150ms      ✓ Fetched 2,534 rows from API
                                             (25 pages × 100 rows + remainder)
02:16    DELTA_SYNC               450ms       ✓ 1,200 new, 400 changed, 934 unchanged
02:67    TRANSFORM                1200ms      ✓ Transformed 1,600 rows
                                             8 rows failed validation (skipped)
03:87    VALIDATION               300ms       ✓ Schema validation passed
04:17    WRITE                    3500ms      ✓ Wrote 1,600 rows (UPSERT)
                                             INSERT: 1,200 rows
                                             UPDATE: 400 rows
07:77    POST_PROCESS             0ms         (no template defined)
07:99    COMMIT                   150ms       ✓ Delta sync state updated
                                             ✓ Execution logged
────────────────────────────────────────────────────────────
TOTAL:   8.0s                                 SUCCESS

Metrics:
  - Rows imported: 1,600
  - Rows skipped: 8
  - Rows unchanged: 934
  - Transformation errors: 8
  - Write throughput: ~457 rows/sec
  - Total data fetched: ~245 KB
```

### Detailed Log Output

```
[550e8400] VALIDATION: Starting validation...
[550e8400] VALIDATION: Loading profile from database...
[550e8400] VALIDATION: Profile "Import Users from API" loaded
[550e8400] VALIDATION: Checking source configuration...
[550e8400] VALIDATION: Source URI: https://api.example.com/v2/users ✓
[550e8400] VALIDATION: Testing source connectivity...
[550e8400] VALIDATION: Source connectivity test passed ✓
[550e8400] VALIDATION: Testing destination connectivity...
[550e8400] VALIDATION: Destination connectivity test passed ✓
[550e8400] VALIDATION: Profile validation complete ✓

[550e8400] SOURCE_READ: Fetching from RestApi (https://api.example.com/v2/users)...
[550e8400] SOURCE_READ: Requesting page 1 (100 rows)...
[550e8400] SOURCE_READ: Received 100 rows, next_cursor: cursor_abc123
[550e8400] SOURCE_READ: Requesting page 2 (100 rows)...
[550e8400] SOURCE_READ: Received 100 rows, next_cursor: cursor_def456
...
[550e8400] SOURCE_READ: Requesting page 25 (100 rows)...
[550e8400] SOURCE_READ: Received 100 rows, next_cursor: cursor_xyz789
[550e8400] SOURCE_READ: Requesting page 26 (34 rows)...
[550e8400] SOURCE_READ: Received 34 rows, next_cursor: null
[550e8400] SOURCE_READ: Fetched 2,534 rows from source ✓

[550e8400] DELTA_SYNC: Detecting changes...
[550e8400] DELTA_SYNC: Loaded 2,100 previous delta states from database
[550e8400] DELTA_SYNC: Classifying 2,534 fetched rows...
[550e8400] DELTA_SYNC: Row 1: NEW (hash not in state)
[550e8400] DELTA_SYNC: Row 5: CHANGED (hash differs from state)
[550e8400] DELTA_SYNC: Row 12: UNCHANGED (hash matches state)
[550e8400] DELTA_SYNC: Classification complete
[550e8400] DELTA_SYNC: Detected: 1,200 new, 400 changed, 934 unchanged rows ✓

[550e8400] TRANSFORM: Transforming rows...
[550e8400] TRANSFORM: Row 1: id=12345 → user_id=12345 ✓
[550e8400] TRANSFORM: Row 2: email=john@example.com → email_address=john@example.com ✓
[550e8400] TRANSFORM: Row 3: created_at=2025-01-09T14:32:00Z → created_date=2025-01-09 ✓
[550e8400] TRANSFORM: Row 8: status=invalid_status ✗ SKIP (validation failed)
[550e8400] TRANSFORM: Transformed 1,600 rows ✓

[550e8400] VALIDATION: Validating schema...
[550e8400] VALIDATION: Row 1: email_address=john@example.com ✓ Valid regex
[550e8400] VALIDATION: Row 2: user_status=active ✓ Valid enum
[550e8400] VALIDATION: Row 8: email_address=invalid ✗ Invalid regex pattern
[550e8400] VALIDATION: Validation complete ✓

[550e8400] WRITE: Writing to Database (Users)...
[550e8400] WRITE: Opening connection to [SQL_SERVER_CONNECTION]...
[550e8400] WRITE: Connection opened ✓
[550e8400] WRITE: Beginning transaction...
[550e8400] WRITE: Batch 1/2: Writing 800 rows (INSERT/UPSERT)...
[550e8400] WRITE: Batch 1 complete: 800 rows written ✓
[550e8400] WRITE: Batch 2/2: Writing 800 rows (INSERT/UPSERT)...
[550e8400] WRITE: Batch 2 complete: 800 rows written ✓
[550e8400] WRITE: Committing transaction...
[550e8400] WRITE: Transaction committed ✓
[550e8400] WRITE: Wrote 1,600 rows ✓

[550e8400] COMMIT: Updating delta sync state...
[550e8400] COMMIT: Upserting 2,534 row hashes into DeltaSyncState...
[550e8400] COMMIT: Delta sync state updated ✓
[550e8400] COMMIT: Logging execution record...
[550e8400] COMMIT: Execution record stored ✓
[550e8400] EXECUTION_COMPLETED: Status=SUCCESS, Duration=8.02s ✓
```

---

## 4. Error Handling Examples

### Example 1: Network Timeout During Source Read

```csharp
// Source read fails with timeout
[550e8401] SOURCE_READ: Fetching from RestApi...
[550e8401] SOURCE_READ: Request timeout after 30s
[550e8401] SOURCE_READ: Retrying (attempt 1/3) in 2s...
[550e8401] SOURCE_READ: Retrying (attempt 2/3) in 4s...
[550e8401] SOURCE_READ: Retrying (attempt 3/3) in 6s...
[550e8401] ERROR: Max retries exceeded: HttpRequestException
[550e8401] EXECUTION_FAILED: Status=FAILED, Duration=47.5s

Action taken:
- Execution marked as FAILED
- No rows written
- Job failure count incremented
- Circuit breaker checked (if 10+ consecutive failures, job paused)
- ImportJob.LastFailureReason = "Max retries exceeded: HttpRequestException"
```

### Example 2: Constraint Violation During Write

```csharp
[550e8402] WRITE: Writing to Database...
[550e8402] WRITE: Batch 1: Writing 800 rows...
[550e8402] WRITE: Row 450: CONSTRAINT_VIOLATION (Unique key violation on email)
[550e8402] WRITE: Error strategy = Skip
[550e8402] WRITE: Row 450 skipped, continuing...
[550e8402] WRITE: Batch 1 complete: 799 rows written, 1 failed ✓
[550e8402] WRITE: Batch 2: Writing 800 rows...
[550e8402] WRITE: Batch 2 complete: 800 rows written ✓
[550e8402] WRITE: Wrote 1,599 rows (1 duplicate) ✓

Action taken:
- Failed row quarantined (if configured)
- Error logged: "Unique key violation on email"
- Execution marked as SUCCESS (partial)
- Metrics updated: RowsFailed=1
```

### Example 3: Invalid Field Transformation

```csharp
[550e8403] TRANSFORM: Row 25: created_at=2025-13-45T25:75:00Z
[550e8403] TRANSFORM: ERROR: Cannot convert to datetime
[550e8403] TRANSFORM: Error strategy = Quarantine
[550e8403] TRANSFORM: Row 25 quarantined to /quarantine/import_1/row_25.json
[550e8403] TRANSFORM: Continuing...
[550e8403] TRANSFORM: Transformed 1,599 rows (1 quarantined) ✓

Quarantine file:
{
  "rowIndex": 25,
  "sourceData": {
    "id": "99999",
    "email": "user@example.com",
    "created_at": "2025-13-45T25:75:00Z"
  },
  "errorMessage": "String was not recognized as a valid DateTime.",
  "errorType": "FormatException",
  "timestamp": "2025-11-09T14:32:25Z"
}
```

---

## 5. Delta Sync in Detail

### Scenario: Hash-Based Delta Sync

**First run** (no previous state):

```
Rows fetched: [
  { id: 1, email: "john@example.com", status: "active" },
  { id: 2, email: "jane@example.com", status: "active" }
]

Delta sync classification:
- Row 1: Hash = SHA256("1john@example.comactive") = abc123... → NEW
- Row 2: Hash = SHA256("2jane@example.comactive") = def456... → NEW

Action: Insert both rows into database
Delta state committed: {row_hash: abc123, row_hash: def456}
```

**Second run** (2 hours later, 1 new, 1 changed, 1 unchanged):

```
Rows fetched: [
  { id: 1, email: "john@example.com", status: "inactive" },  // Changed!
  { id: 2, email: "jane@example.com", status: "active" },    // Unchanged
  { id: 3, email: "bob@example.com", status: "active" }      // New!
]

Delta sync classification:
- Row 1: Hash = SHA256("1john@example.cominactive") = xyz789...
         Previous hash: abc123... → CHANGED ✓
- Row 2: Hash = SHA256("2jane@example.comactive") = def456...
         Previous hash: def456... → UNCHANGED ✓
- Row 3: Hash = SHA256("3bob@example.comactive") = ghi012...
         Previous hash: not found → NEW ✓

Action:
- Filter to only CHANGED + NEW rows: [Row1, Row3]
- Update Row 1 (status: active → inactive)
- Insert Row 3 (new user)
- Skip Row 2 (no changes)

Delta state committed: {
  row_hash: xyz789 (updated),
  row_hash: def456 (unchanged, kept),
  row_hash: ghi012 (new)
}
```

**DeltaSyncState table after second run**:

```sql
ProfileId | DataDirection | RowHash | RowKey | LastSyncedAt
----------|----------------|---------|--------|---------------------
1         | Import         | abc123  | NULL   | 2025-11-09 14:28:00  → DELETED (old hash)
1         | Import         | xyz789  | NULL   | 2025-11-09 14:32:00  → NEW (updated)
1         | Import         | def456  | NULL   | 2025-11-09 14:28:00  → UNCHANGED
1         | Import         | ghi012  | NULL   | 2025-11-09 14:32:00  → NEW
```

---

## 6. Retry Logic with Exponential Backoff

```csharp
public async Task<T> ExecuteWithRetryAsync<T>(
    Func<Task<T>> operation,
    int maxRetries,
    int initialDelayMs)
{
    int delayMs = initialDelayMs;

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            _logger.LogInformation($"Attempt {attempt}/{maxRetries}...");
            return await operation();
        }
        catch (Exception ex) when (attempt < maxRetries && IsTransient(ex))
        {
            _logger.LogWarning(
                $"Transient error on attempt {attempt}: {ex.Message}. " +
                $"Retrying in {delayMs}ms...");

            await Task.Delay(delayMs);
            delayMs = (int)(delayMs * 1.5);  // Exponential backoff
        }
    }

    throw new MaxRetriesExceededException($"Failed after {maxRetries} attempts");
}
```

**Example execution with timeout**:

```
Attempt 1/3: HttpRequestException (timeout)
Delay: 2000ms
Attempt 2/3: HttpRequestException (timeout)
Delay: 3000ms
Attempt 3/3: HttpRequestException (timeout)
FAILED: Max retries exceeded
```

---

## Summary

- **Total execution pipeline**: 9 distinct stages
- **Typical execution time**: 8-15 seconds (for 2000 rows)
- **Error handling**: Configurable strategies (Skip, Fail, Quarantine, Retry)
- **Delta sync**: 100% accurate classification via SHA256 hashing
- **Auditability**: Every stage logged with timestamp and metrics
- **Resilience**: Exponential backoff retry, circuit breaker, transaction rollback

---

**Document Version**: 1.0
**Status**: Design Phase
**Next Step**: Create Implementation Roadmap
