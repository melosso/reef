# Reef Import Architecture Overview

## Executive Summary

This document outlines a **symmetrical Import mechanism** for Reef that mirrors the existing Export architecture. The Import system enables inbound data ingestion from various sources (REST APIs, S3, FTP, databases) with the same level of reliability, transformation, scheduling, and auditability as the current Export platform.

**Key principle**: Imports are exports in reverse—reusing the same abstractions, execution engine, delta sync, templating, and scheduler.

---

## 1. Design Philosophy: Symmetry

### Current Export Flow (Unidirectional)
```
┌─────────────────────────────────────────────────────────┐
│ Reef Export Architecture                                │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Profile Definition (Query + Destination)              │
│         ↓                                                │
│  Read from Database (QueryExecutor)                    │
│         ↓                                                │
│  Delta Sync (Track Changes via Hash)                   │
│         ↓                                                │
│  Transformation (Scriban Templates)                    │
│         ↓                                                │
│  Format (JSON, CSV, XML, YAML)                         │
│         ↓                                                │
│  Split (Multi-output rules)                            │
│         ↓                                                │
│  Write to Destination (File, FTP, S3, Cloud)          │
│         ↓                                                │
│  Audit Log & Execution History                         │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

### Proposed Import Flow (Symmetrical)
```
┌─────────────────────────────────────────────────────────┐
│ Reef Import Architecture (Proposed)                     │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Profile Definition (Source + Destination)             │
│         ↓                                                │
│  Read from Source (DataSourceExecutor)                 │
│         ↓                                                │
│  Delta Sync (Track Changes via Hash)                   │
│         ↓                                                │
│  Transformation (Scriban Templates)                    │
│         ↓                                                │
│  Format (Normalize to tabular)                         │
│         ↓                                                │
│  Validation (Schema conformance)                       │
│         ↓                                                │
│  Write to Destination (Database, File)                 │
│         ↓                                                │
│  Audit Log & Execution History                         │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

### Key Symmetries

| Component | Export | Import | Reuse Strategy |
|-----------|--------|--------|-----------------|
| **Profile** | `ExportProfile` | `ImportProfile` | Extend base `DataProfile` |
| **Scheduling** | `JobScheduler` | `JobScheduler` | Unified job type system |
| **Execution** | `ExecutionService` | `ExecutionService` | Direction-agnostic pipeline |
| **Delta Sync** | `DeltaSyncService` | `DeltaSyncService` | Same row-hash mechanism |
| **Templating** | `ScribanTemplateEngine` | `ScribanTemplateEngine` | Pre/post-processing transforms |
| **Logging** | `ExecutionLogger` | `ExecutionLogger` | Shared audit trail |
| **Destination Handler** | `DestinationService` | `WriterService` | Mirror implementation |
| **Connections** | `ConnectionService` | `ConnectionService` | Bidirectional credentials |

---

## 2. High-Level Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                          Reef UI / API                               │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ Unified Profiles Tab                                        │    │
│  │ ├─ Export Profiles (existing)                              │    │
│  │ └─ Import Profiles (new)                                   │    │
│  │    ├─ Create/Edit/Delete                                  │    │
│  │    ├─ Test Run                                            │    │
│  │    ├─ Execution History                                   │    │
│  │    └─ Delta Sync State Viewer                             │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                    Job Scheduler & Executor                          │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ JobScheduler (unified)                                      │    │
│  │ ├─ Producer: Poll due jobs (10s interval)                 │    │
│  │ ├─ Queue: Priority-based (imports/exports mixed)          │    │
│  │ └─ Consumers: Worker threads (configurable, default 10)   │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                  Execution Engine (Unified)                          │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ ExecutionService (direction-agnostic)                       │    │
│  │ ├─ Validation (profile config, connectivity test)         │    │
│  │ ├─ Pre-processing (Scriban templates)                     │    │
│  │ ├─ Data source read (DataSourceExecutor)                  │    │
│  │ │  └─ REST, S3, FTP, Database sources                    │    │
│  │ ├─ Delta Sync (DeltaSyncService - unchanged)             │    │
│  │ ├─ Transformation (Scriban templates)                     │    │
│  │ ├─ Schema Validation                                       │    │
│  │ ├─ Data write (WriterService)                             │    │
│  │ │  └─ Database, File, Cloud destinations                 │    │
│  │ ├─ Post-processing (Scriban templates)                    │    │
│  │ └─ Commit & Audit                                          │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│               Core Services (Largely Reused)                         │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐   │
│  │ ConnectionService│  │ DeltaSyncService │  │ScribanTemplateEng│   │
│  │ (Bidirectional)  │  │ (Unchanged)      │  │(Pre/Post process)│   │
│  └──────────────────┘  └──────────────────┘  └──────────────────┘   │
│                                                                      │
├──────────────────────────────────────────────────────────────────────┤
│                       Data Layer                                     │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │ SQLite Database                                            │    │
│  │ ├─ ImportProfiles (new table)                             │    │
│  │ ├─ ImportJobs (new table)                                 │    │
│  │ ├─ ImportExecutions (new table)                           │    │
│  │ ├─ DataSourceConfigs (new table)                          │    │
│  │ ├─ Shared: Connections, DeltaSyncState, Templates         │    │
│  └────────────────────────────────────────────────────────────┘    │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 3. Core Concepts

### 3.1 Import Profile
An **Import Profile** defines:
- **Source**: Where data comes from (REST API, S3, FTP, database)
- **Destination**: Where data is written (database, file system)
- **Transformations**: Pre/post-processing logic using Scriban
- **Scheduling**: Cron, intervals, or webhooks
- **Delta sync**: Track changes by hash, timestamp, or key
- **Schema mapping**: Field transformations and validations
- **Error handling**: Retry, skip, or fail strategies

### 3.2 Data Source Executor
Analogous to `QueryExecutor` for exports. Handles:
- **REST API**: HTTP GET/POST with pagination, authentication, rate limiting
- **S3**: Object retrieval with streaming
- **FTP**: Remote file downloads
- **Database**: Direct table read (via existing connections)
- **SFTP**: Secure file transfer
- **Message Queues** (future): Kafka, Azure Service Bus, etc.

### 3.3 Writer Service
Analogous to `DestinationService` for exports. Handles:
- **Database**: INSERT/UPSERT into tables
- **File System**: CSV, JSON, Parquet output
- **S3**: Object uploads
- **FTP**: Remote file uploads
- **Cloud Storage**: Azure Blob, Google Cloud Storage, etc.

### 3.4 Delta Sync for Imports
Reuses the existing `DeltaSyncService`:
- **Hash-based detection**: Track row changes via SHA256
- **Timestamp-based detection**: Rely on source timestamps
- **Key-based detection**: Row keys (natural or synthetic)
- **First-run behavior**: Ingest all rows, mark as baseline

### 3.5 Transformation Pipeline
Reuses `ScribanTemplateEngine`:
- **Pre-fetch transformation**: Shape data from source before delta sync (e.g., API response parsing)
- **Row-level transformation**: Transform each row before writing (e.g., unit conversion)
- **Post-write transformation**: Cleanup or downstream triggers

---

## 4. Execution Pipeline (Unified)

```
Import Execution Pipeline:

1. VALIDATION
   ├─ Profile exists and enabled?
   ├─ Source credentials valid?
   ├─ Destination credentials valid?
   └─ Schema mapping valid?

2. PRE-FETCH TRANSFORMATION
   ├─ Execute pre-fetch Scriban template (if defined)
   └─ Example: Parse API pagination headers, filter results

3. DATA SOURCE READ
   ├─ Instantiate appropriate DataSourceExecutor
   │  ├─ RestDataSourceExecutor (pagination, auth)
   │  ├─ S3DataSourceExecutor (boto-style)
   │  ├─ FtpDataSourceExecutor (FTP/SFTP)
   │  ├─ DatabaseDataSourceExecutor (SQL query)
   │  └─ FileDataSourceExecutor (local/remote files)
   ├─ Fetch data with retry logic (exponential backoff)
   ├─ Stream results to avoid memory explosion
   └─ Return: List<Dictionary<string, object>>

4. DELTA SYNC
   ├─ Calculate row hash (SHA256 over normalized columns)
   ├─ Compare with previous state in DeltaSyncState table
   ├─ Classify rows: NEW | CHANGED | UNCHANGED
   └─ Filter: Pass only NEW + CHANGED to next stage

5. ROW-LEVEL TRANSFORMATION
   ├─ For each row:
   │  ├─ Execute row transformation Scriban template
   │  ├─ Field mapping: source_col → destination_col
   │  ├─ Type conversion: string → datetime, etc.
   │  ├─ Validation: Required fields, formats, ranges
   │  └─ Enrich: Lookup values, calculate fields
   └─ Collect transformed rows

6. SCHEMA VALIDATION
   ├─ Verify all required columns present
   ├─ Type conformance
   ├─ Foreign key constraints (if applicable)
   └─ Business rule validation

7. WRITE TO DESTINATION
   ├─ Instantiate appropriate Writer
   │  ├─ DatabaseWriter (UPSERT logic)
   │  ├─ FileWriter (CSV, JSON, Parquet)
   │  ├─ S3Writer (multipart upload)
   │  ├─ FtpWriter (SFTP upload)
   │  └─ CloudStorageWriter (Azure, GCS)
   ├─ Write with batch optimization
   ├─ Retry on transient failures
   └─ Return: write_count, error_count

8. POST-WRITE TRANSFORMATION
   ├─ Execute post-write Scriban template (if defined)
   └─ Example: Trigger downstream exports, send notifications

9. COMMIT & AUDIT
   ├─ Update DeltaSyncState with new hashes
   ├─ Mark profile execution as SUCCESS/FAILURE
   ├─ Log metrics: rows_imported, duration, errors
   ├─ Encrypt sensitive data in logs
   └─ Trigger notifications (Slack, email, webhooks)
```

---

## 5. No Breaking Changes

The Import mechanism is **entirely additive**:

1. **New tables**: `ImportProfiles`, `ImportJobs`, `ImportExecutions`, `DataSourceConfigs`
2. **New endpoints**: `/api/import-profiles/*`, `/api/import-jobs/*`
3. **New service classes**: `DataSourceExecutor`, `WriterService`, `ImportProfileService`
4. **Existing code unchanged**: All export services remain identical
5. **Unified scheduler**: `JobScheduler` updated to handle both `ExportJob` and `ImportJob` (polymorphic)
6. **Shared infrastructure**: Connections, Delta Sync, Templates, Logging, Auth remain reused

---

## 6. Security & Auditability

### Encryption
- **Source credentials**: Same RSA 2048 + AES 256 as existing connections
- **Destination credentials**: Encrypted in database
- **API keys/tokens**: Encrypted at rest, masked in logs

### Audit Trail
- **Execution tracking**: Every import logged with timestamp, user, status, row count
- **Error logging**: Detailed error messages encrypted in database
- **Data lineage**: Source → Destination mapping per row (if enabled)
- **Compliance**: GDPR-compliant data deletion, retention policies

### Access Control
- **Role-based**: Admin, Operator, Viewer roles on profiles
- **Connection isolation**: Users can only access profiles using connections they own
- **Webhook secrets**: HMAC-SHA256 signature validation

---

## 7. UI/API Changes (Minimal)

### New API Endpoints

```
POST   /api/import-profiles              - Create profile
GET    /api/import-profiles              - List profiles
GET    /api/import-profiles/{id}         - Get profile details
PUT    /api/import-profiles/{id}         - Update profile
DELETE /api/import-profiles/{id}         - Delete profile
POST   /api/import-profiles/{id}/test    - Test run (no persist)
POST   /api/import-profiles/{id}/run     - Trigger immediate execution

GET    /api/import-jobs                  - List scheduled imports
POST   /api/import-jobs                  - Create scheduled import
PUT    /api/import-jobs/{id}             - Update job schedule
DELETE /api/import-jobs/{id}             - Delete job

GET    /api/import-executions            - List execution history
GET    /api/import-executions/{id}       - Get execution details

GET    /api/data-sources                 - List available source types
GET    /api/data-sources/{type}/schema   - Get schema for source type

GET    /api/import-profiles/{id}/delta-state - View delta sync state
POST   /api/import-profiles/{id}/reset-delta - Reset delta sync tracking
```

### UI Additions

1. **Import Profiles Tab** (mirror of Export Profiles)
   - Create/Edit/Delete profiles with source and destination config
   - Visual profile builder
   - Execution history table with filtering

2. **Test Run Modal**
   - Run profile without persisting (preview results)
   - Show row count, transformation applied, estimated write time
   - Display any validation errors

3. **Delta Sync Viewer**
   - Show number of NEW/CHANGED/UNCHANGED rows
   - Last sync timestamp
   - Reset button with confirmation

4. **Unified Job Dashboard**
   - Combined view of import + export jobs
   - Filter by type, status, schedule
   - Quick actions: Run, Pause, Edit, Delete

---

## 8. Extension Points for Future

### 8.1 Bidirectional Sync
```csharp
// Future: profiles that both import and export
public class SyncProfile
{
    public ImportProfile Import { get; set; }
    public ExportProfile Export { get; set; }
    public SyncDirection Direction { get; set; } // OneWay, TwoWay, ConflictStrategy
}
```

### 8.2 Chained Pipelines
```csharp
// Future: import → transform → export workflows
public class PipelineProfile
{
    public List<PipelineStage> Stages { get; set; }
    // Stage 1: Import from API
    // Stage 2: Transform via Scriban
    // Stage 3: Export to database
}
```

### 8.3 Event-Driven Architecture
```csharp
// Future: imports triggered by webhooks, events
public class EventTrigger
{
    public string EventType { get; set; } // "file_uploaded", "api_data_available"
    public string SourceUri { get; set; }
    public Dictionary<string, object> EventData { get; set; }
}
```

### 8.4 New Connectors (Pluggable)
- Kafka topics (streaming imports)
- Database replication (CDC)
- Webhook payloads
- Snowflake, BigQuery, Datadog

---

## 9. Example Scenarios

### Scenario 1: REST API → SQL Database
```
Profile:
├─ Source: REST API (https://api.example.com/users)
│  ├─ Auth: Bearer token (encrypted)
│  ├─ Pagination: Cursor-based (cursor_id param)
│  └─ Pre-transform: Extract "data" array from response
├─ Destination: SQL Server (existing connection)
│  └─ Table: Users
├─ Delta Sync: Hash-based (detect changed users)
├─ Row Transform:
│  ├─ api_id → user_id
│  ├─ email → email_address
│  └─ created_at → created_date (parse ISO8601)
└─ Schedule: Every 6 hours via cron

Execution:
1. Fetch paginated data from REST API
2. Detect new/changed users via hash
3. Transform field names and types
4. UPSERT into Users table
5. Log: 1,234 new users, 156 updated, 0 errors
```

### Scenario 2: S3 CSV → Local Files + Database
```
Profile:
├─ Source: S3 (s3://data-bucket/exports/)
│  ├─ Credentials: AWS access key (encrypted)
│  ├─ Prefix: data/daily/
│  └─ Pattern: *.csv (filter)
├─ Destination: Database (SQLite)
│  └─ Table: ImportedData
├─ Transformation:
│  ├─ Pre-fetch: List S3 objects, filter by timestamp
│  ├─ Row-level: Convert amounts to decimal, validate dates
│  └─ Post-write: Create summary report (Scriban template)
├─ Error Handling: Skip rows with validation errors, log separately
└─ Schedule: Daily at 2 AM

Execution:
1. List S3 objects matching pattern
2. Download CSV files (stream for large files)
3. Parse CSV → rows
4. Validate and transform
5. Insert into database
6. Generate summary (total rows, errors, duration)
7. Store summary in artifacts directory
```

### Scenario 3: FTP Files with Delta Detection
```
Profile:
├─ Source: FTP (ftp://vendor.example.com/feed/)
│  ├─ Credentials: SFTP (encrypted)
│  └─ Pattern: *.json
├─ Delta Sync: Timestamp-based (file modification time)
├─ Transformation: Parse JSON → normalize fields
├─ Destination: File system (/data/vendor_feed/)
├─ Error Handling: Quarantine files with errors
└─ Schedule: Every 4 hours

Execution:
1. List FTP files (with timestamps)
2. Compare mod times to last sync
3. Download only new/modified files
4. Parse JSON → tabular format
5. Write to local directory
6. Create .processed marker file
```

---

## 10. Comparison: Export vs. Import Architecture

| Aspect | Export | Import |
|--------|--------|--------|
| **Source** | Database (QueryExecutor) | Multiple (DataSourceExecutor) |
| **Destination** | Multiple (DestinationService) | Database/Files (WriterService) |
| **Data Flow** | Pull from DB → Transform → Push | Pull from source → Transform → Push |
| **Delta Logic** | Hash-based row tracking | Hash, Timestamp, or Key-based |
| **Executor** | `QueryExecutor` | `DataSourceExecutor` (new) |
| **Handler** | `DestinationService` | `WriterService` (new) |
| **Scheduling** | `JobScheduler` | `JobScheduler` (reused) |
| **Transformations** | `ScribanTemplateEngine` | `ScribanTemplateEngine` (reused) |
| **Error Recovery** | Retry destination write | Retry source read + skip rows |
| **Audit Trail** | `ExecutionLogger` | `ExecutionLogger` (reused) |

---

## 11. Benefits of Symmetric Design

✅ **Code Reuse**: 60-70% of export logic reusable (scheduler, delta sync, templating, logging)
✅ **Familiar UX**: Import UI mirrors export UI (faster adoption)
✅ **Operational Consistency**: Same monitoring, alerting, and audit trails
✅ **Team Efficiency**: Engineers familiar with exports understand imports immediately
✅ **Future-Proof**: Easy to extend to bidirectional sync or chained pipelines
✅ **Security**: Credential encryption, audit trails, access control already solved
✅ **Scalability**: Unified job queue prevents resource contention
✅ **Testing**: Shared test infrastructure, similar test patterns

---

## 12. Success Metrics

- **Time to implement**: 8-12 weeks (phased)
- **Code reuse ratio**: 65%+
- **Test coverage**: >85%
- **Performance**: Import 100K rows in <2 minutes
- **Delta sync accuracy**: 100% (zero false positives/negatives)
- **Error recovery**: 95%+ retry success rate
- **User adoption**: 80%+ of export users adopt imports within 3 months

---

## 13. Next Steps

See the following documents for detailed implementation:

1. **Module & Class Design** - Exact classes, interfaces, and responsibilities
2. **Data Schema** - New tables, migration strategy
3. **Execution Flow** - Step-by-step code flow with diagrams
4. **Implementation Roadmap** - Phased approach with milestones
5. **Risk & Mitigation** - Potential issues and solutions

---

**Document Version**: 1.0
**Date**: 2025-11-09
**Status**: Architecture Design
**Next Review**: Upon roadmap approval
