# Reef Import Architecture: Index & Quick Reference

## Overview

This is the main index document for the comprehensive Import Architecture design for Reef. It provides navigation, quick reference information, and high-level guidance for understanding and implementing the Import mechanism.

---

## 📚 Documentation Structure

### 1. **Architecture Overview** (`01-IMPORT-ARCHITECTURE-OVERVIEW.md`)
**Purpose**: High-level system design and principles

**Key sections**:
- Design philosophy (symmetry between Export and Import)
- High-level architecture diagram
- Core concepts (Profile, DataSourceExecutor, WriterService, Delta Sync)
- Unified execution pipeline (9 stages)
- Security & auditability approach
- UI/API changes (minimal)
- Extension points for future features
- Real-world scenario examples

**When to read**: Start here for overall understanding
**Audience**: Architects, tech leads, team members getting oriented

---

### 2. **Module & Class Design** (`02-MODULE-CLASS-DESIGN.md`)
**Purpose**: Concrete class definitions, interfaces, and design patterns

**Key sections**:
- Core abstractions (base classes, interfaces)
- Data source executors (REST, S3, FTP, Database, File)
- Writer implementations (Database, File, S3, FTP)
- Import execution service (9-stage pipeline)
- Service registration (dependency injection)
- Complete class inventory
- Design patterns applied

**When to read**: When implementing classes, understanding structure
**Audience**: Backend engineers, developers

---

### 3. **Data Schema & Storage** (`03-DATA-SCHEMA-STORAGE.md`)
**Purpose**: Database schema design, migrations, and storage strategy

**Key sections**:
- ImportProfiles table schema (complete DDL)
- ImportJobs table schema
- ImportExecutions table schema (detailed metrics)
- Extended DeltaSyncState for imports
- DataSourceConfigs table (optional, reusable configs)
- Migration strategy
- Encryption at rest
- Backup & recovery procedures
- Data retention & cleanup policies
- Performance indexing
- Schema diagram and relationships

**When to read**: When setting up database, configuring storage
**Audience**: Database engineers, DevOps, backend engineers

---

### 4. **Execution Flow & Examples** (`04-EXECUTION-FLOW-EXAMPLES.md`)
**Purpose**: Detailed execution pipeline with real-world examples

**Key sections**:
- High-level execution flow diagram (9 stages with timing)
- Detailed code execution flow (step-by-step)
- Real-world example: REST API → SQL Database
- Execution timeline with metrics
- Error handling examples (network timeout, constraint violation, transformation error)
- Delta sync in detail (first run, subsequent runs, hash calculation)
- Retry logic with exponential backoff

**When to read**: Understanding execution, debugging issues, testing
**Audience**: Backend engineers, QA, troubleshooting

---

### 5. **Implementation Roadmap** (`05-IMPLEMENTATION-ROADMAP.md`)
**Purpose**: Phased implementation plan with timeline and effort estimates

**Key sections**:
- Phase 0: Preparation & Foundation (Week 1, 40-50 hours)
  - Database schema
  - Code structure
  - CI/CD integration
- Phase 1: MVP - REST API → Database (Weeks 2-4, 120-150 hours)
  - Core models and repository layer
  - REST data source executor
  - Database writer
  - Execution service
  - API endpoints
- Phase 2: Delta Sync & Connectors (Weeks 5-7, 160-200 hours)
  - Delta sync implementation
  - S3, FTP, Database executors
  - Row transformation (Scriban)
  - Scheduled job execution
  - Error handling & quarantine
- Phase 3: UI & Features (Weeks 8-10, 120-160 hours)
  - Import profiles UI
  - Execution monitoring dashboard
  - Delta sync state viewer
  - Additional writers
  - Validation & schema mapping
- Phase 4: Optimization & Release (Weeks 11-12, 80-120 hours)
  - Performance optimization
  - Load testing
  - Security audit
  - Production readiness
- Team structure, risks, success metrics

**When to read**: Project planning, sprint planning, tracking progress
**Audience**: Project managers, engineering leads, team members

---

## 🎯 Quick Reference Guide

### Architecture at a Glance

```
┌─────────────────────────────────────────────────────┐
│          Import Profile Definition                  │
│   (Source + Destination + Schedule + Transforms)   │
└──────────────────┬──────────────────────────────────┘
                   │
                   ↓
    ┌──────────────────────────────┐
    │ JobScheduler (Reused)        │
    │ ├─ Cron scheduling           │
    │ ├─ Interval-based            │
    │ └─ Webhook triggers          │
    └──────────────┬───────────────┘
                   │
                   ↓
    ┌──────────────────────────────────┐
    │ ExecutionService (Direction-Agnostic)  │
    │ ├─ 1. Validation              │
    │ ├─ 2. Pre-fetch transform     │
    │ ├─ 3. Source read             │
    │ │   └─ DataSourceExecutor     │
    │ ├─ 4. Delta sync              │
    │ ├─ 5. Row transform           │
    │ ├─ 6. Schema validate         │
    │ ├─ 7. Write to destination    │
    │ │   └─ DataWriter             │
    │ ├─ 8. Post-write transform    │
    │ └─ 9. Commit & audit          │
    └──────────────┬───────────────┘
                   │
                   ↓
    ┌──────────────────────────────┐
    │ ImportExecutions Logged       │
    │ (Audit trail, metrics)        │
    └──────────────────────────────┘
```

### Key Classes & Responsibilities

| Class | Purpose | Implements |
|-------|---------|-----------|
| `ImportProfile` | Configuration for an import (source, dest, schedule) | Inherits from `DataProfile` |
| `ImportJob` | Scheduled task for an import profile | Job scheduling metadata |
| `RestDataSourceExecutor` | Fetches data from REST APIs | `IDataSourceExecutor` |
| `S3DataSourceExecutor` | Fetches objects from S3 buckets | `IDataSourceExecutor` |
| `FtpDataSourceExecutor` | Fetches files from FTP/SFTP | `IDataSourceExecutor` |
| `DatabaseDataSourceExecutor` | Queries data from databases | `IDataSourceExecutor` |
| `DatabaseWriter` | Writes rows to database (INSERT/UPSERT) | `IDataWriter` |
| `FileWriter` | Writes rows to CSV/JSON files | `IDataWriter` |
| `ImportExecutionService` | Orchestrates 9-stage pipeline | `IImportExecutionService` |
| `DeltaSyncService` | Detects new/changed/unchanged rows | Reused from Export |
| `ScribanTemplateEngine` | Transforms data with templates | Reused from Export |

### Core Data Models

**ImportProfile** fields (essential):
```csharp
- Name, Description
- SourceType (RestApi, S3, Ftp, Database, File)
- SourceUri (API URL, S3 path, table name)
- SourceConfiguration (JSON: pagination, auth, filters)
- DestinationType (Database, File, S3, Ftp)
- DestinationUri (table name, file path, S3 path)
- DestinationConfiguration (JSON: format, upsert logic)
- DeltaSyncMode (Hash, Timestamp, Key, None)
- FieldMappings (source → destination column mapping)
- ValidationRules (schema validation)
- ErrorStrategy (Skip, Fail, Retry, Quarantine)
- CronExpression or IntervalSeconds (scheduling)
```

### API Endpoints (Minimal)

```
POST   /api/import-profiles              Create profile
GET    /api/import-profiles              List profiles
GET    /api/import-profiles/{id}         Get details
PUT    /api/import-profiles/{id}         Update profile
DELETE /api/import-profiles/{id}         Delete profile
POST   /api/import-profiles/{id}/run     Trigger import
POST   /api/import-profiles/{id}/test    Test run (preview)

GET    /api/import-jobs                  List scheduled jobs
POST   /api/import-jobs                  Create job
PUT    /api/import-jobs/{id}             Update job
DELETE /api/import-jobs/{id}             Delete job

GET    /api/import-executions            List execution history
GET    /api/import-executions/{id}       Get execution details
```

### Database Tables (New)

| Table | Purpose | Key Columns |
|-------|---------|------------|
| `ImportProfiles` | Import profile definitions | Id, Name, SourceType, DestinationType |
| `ImportJobs` | Scheduled import tasks | Id, ImportProfileId, CronExpression, IntervalSeconds |
| `ImportExecutions` | Execution history | Id, ImportProfileId, Status, StartedAt, RowsWritten |
| `DataSourceConfigs` | Reusable source configurations | Id, Name, DataSourceType |
| `DeltaSyncState` | Extended for imports | ProfileId, DataDirection ('Export' or 'Import') |

### Execution Pipeline (9 Stages)

1. **Validation** (200-500ms) - Profile & connectivity check
2. **Pre-fetch Transform** (50-200ms) - Scriban template execution
3. **Source Read** (1-30s) - Fetch data from source
4. **Delta Sync** (100ms-5s) - Classify rows (new/changed/unchanged)
5. **Row Transform** (500ms-10s) - Apply field mapping & templates
6. **Schema Validation** (100-500ms) - Type & value validation
7. **Write** (1-30s) - UPSERT to destination
8. **Post-process** (50-500ms) - Scriban template execution
9. **Commit & Audit** (200-500ms) - Update delta state, log execution

---

## 🔄 Symmetry with Export Architecture

**Key insight**: Imports are exports in reverse.

| Aspect | Export | Import |
|--------|--------|--------|
| **Source** | Database (QueryExecutor) | Multiple sources (DataSourceExecutor) |
| **Destination** | Files, S3, FTP, etc. | Database, Files, S3, FTP, etc. |
| **Orchestration** | ExecutionService | ExecutionService (same) |
| **Delta Tracking** | DeltaSyncService | DeltaSyncService (same) |
| **Templating** | ScribanTemplateEngine | ScribanTemplateEngine (same) |
| **Scheduling** | JobScheduler | JobScheduler (same) |
| **Logging** | ExecutionLogger | ExecutionLogger (same) |
| **Credentials** | ConnectionService | ConnectionService (same) |

**Code reuse**: ~65% of import logic reuses existing export components.

---

## 🚀 Getting Started

### For Architects/Tech Leads
1. Read `01-IMPORT-ARCHITECTURE-OVERVIEW.md` (10 min)
2. Review `02-MODULE-CLASS-DESIGN.md` - class hierarchy section (10 min)
3. Skim `05-IMPLEMENTATION-ROADMAP.md` - timeline & phases (5 min)
4. **Time**: ~25 minutes

### For Backend Engineers
1. Read `01-IMPORT-ARCHITECTURE-OVERVIEW.md` - Core Concepts section (10 min)
2. Study `02-MODULE-CLASS-DESIGN.md` - complete (30 min)
3. Read `03-DATA-SCHEMA-STORAGE.md` - relevant tables (20 min)
4. Reference `04-EXECUTION-FLOW-EXAMPLES.md` during implementation (as needed)
5. **Time**: ~60 minutes

### For Database/DevOps Engineers
1. Read `01-IMPORT-ARCHITECTURE-OVERVIEW.md` - Architecture section (5 min)
2. Study `03-DATA-SCHEMA-STORAGE.md` - complete (40 min)
3. Review `05-IMPLEMENTATION-ROADMAP.md` - Phase 0 & Phase 4 sections (20 min)
4. **Time**: ~65 minutes

### For QA Engineers
1. Read `01-IMPORT-ARCHITECTURE-OVERVIEW.md` (10 min)
2. Study `04-EXECUTION-FLOW-EXAMPLES.md` - complete (30 min)
3. Review `05-IMPLEMENTATION-ROADMAP.md` - testing sections (15 min)
4. **Time**: ~55 minutes

### For Product/UX
1. Read `01-IMPORT-ARCHITECTURE-OVERVIEW.md` - UI/API Changes section (5 min)
2. Skim `05-IMPLEMENTATION-ROADMAP.md` - Phase 3 (UI & Features) (10 min)
3. Reference scenario examples in `04-EXECUTION-FLOW-EXAMPLES.md` (10 min)
4. **Time**: ~25 minutes

---

## 💡 Key Design Decisions

### 1. **Direction-Agnostic Pipeline**
✅ **Decision**: Reuse ExecutionService for both exports and imports
✅ **Rationale**: Minimize code duplication, ensure consistent behavior, easier maintenance
✅ **Trade-off**: Slightly more abstract code, but pays off with 65% code reuse

### 2. **Delta Sync via Hashing**
✅ **Decision**: Use SHA256 hash-based delta detection
✅ **Rationale**: 100% accurate (no false positives), works across all sources/destinations
✅ **Trade-off**: Slight memory overhead on first run (hash calculation)

### 3. **Pluggable Data Sources & Writers**
✅ **Decision**: Strategy pattern with factory
✅ **Rationale**: Easy to add new connectors (REST, S3, Kafka, etc.)
✅ **Trade-off**: More classes to maintain, but scalable design

### 4. **Scriban for Transformations**
✅ **Decision**: Reuse ScribanTemplateEngine
✅ **Rationale**: Already proven, consistent with exports, flexible
✅ **Trade-off**: Learning curve for complex templates

### 5. **Configurable Error Handling**
✅ **Decision**: Four strategies (Skip, Fail, Quarantine, Retry)
✅ **Rationale**: Flexibility for different use cases, debuggability
✅ **Trade-off**: More complexity, must choose per profile

---

## ⚠️ Critical Implementation Notes

### 1. **Delta Sync State is Sacred**
- Only update after **successful** write (not before)
- Prevents false positives in next run
- See `04-EXECUTION-FLOW-EXAMPLES.md` for detailed examples

### 2. **Transaction Boundaries**
- DatabaseWriter must use transactions for atomicity
- On error, rollback entire batch
- For file destinations, write to temp file first, then rename

### 3. **Retry Idempotency**
- UPSERT must be idempotent (same data, same result)
- Avoid row_number() or timestamp-based inserts
- Use natural/composite keys for UPSERT

### 4. **Memory Management**
- Stream large datasets (avoid loading all into memory)
- For 1M+ rows, process in chunks
- Clear data structures after writing

### 5. **Credential Security**
- All API keys/passwords must be encrypted (use existing encryption service)
- Never log credentials (mask sensitive fields)
- Rotate tokens periodically

---

## 🧪 Testing Strategy

### Unit Tests
- Model validation
- Executor logic (mocked APIs)
- Writer logic (mocked databases)
- Transformation logic (Scriban templates)
- Delta sync classification

### Integration Tests
- Full pipeline with real data sources
- All source/destination combinations
- Error scenarios
- Performance benchmarks

### E2E Tests
- API endpoint testing
- UI workflow testing
- Full import from source to destination

### Load Tests
- 10K, 100K, 1M row scenarios
- Concurrent imports
- Memory usage under load

---

## 📊 Success Criteria (Phase 1 MVP)

✅ **Functionality**
- Import data from REST API to SQL database
- Manual trigger via API
- Execution history captured

✅ **Quality**
- 85%+ code coverage
- All unit tests passing
- Integration tests for MVP scenario

✅ **Performance**
- Import 10K rows in <2 minutes
- Memory usage <200MB

✅ **Operations**
- Deployment via Docker
- Health checks working
- Logs accessible

---

## 🔗 Document Navigation

```
00-INDEX-AND-QUICK-REFERENCE.md (you are here)
├─ 01-IMPORT-ARCHITECTURE-OVERVIEW.md
│  └─ Read first for big picture
├─ 02-MODULE-CLASS-DESIGN.md
│  └─ Reference during implementation
├─ 03-DATA-SCHEMA-STORAGE.md
│  └─ Use for database setup
├─ 04-EXECUTION-FLOW-EXAMPLES.md
│  └─ Reference during testing & debugging
└─ 05-IMPLEMENTATION-ROADMAP.md
   └─ Use for project planning
```

---

## 📞 Support & Questions

### Architecture Questions
→ Review `01-IMPORT-ARCHITECTURE-OVERVIEW.md` section on that topic, then discuss with tech lead

### Implementation Questions
→ Check `02-MODULE-CLASS-DESIGN.md` class definitions, then ask backend lead

### Database Questions
→ Consult `03-DATA-SCHEMA-STORAGE.md`, then ask database engineer

### Execution/Debugging
→ Trace through `04-EXECUTION-FLOW-EXAMPLES.md` corresponding stage

### Timeline/Planning
→ Reference `05-IMPLEMENTATION-ROADMAP.md` phase details

---

## 📝 Document Maintenance

| Document | Last Updated | Maintainer | Review Cycle |
|----------|--------------|-----------|-------------|
| 00-INDEX | 2025-11-09 | Architecture | Monthly |
| 01-OVERVIEW | 2025-11-09 | Architecture | Quarterly |
| 02-DESIGN | 2025-11-09 | Engineering Lead | Quarterly |
| 03-SCHEMA | 2025-11-09 | Database Engineer | As-needed |
| 04-EXECUTION | 2025-11-09 | Engineering Lead | As-needed |
| 05-ROADMAP | 2025-11-09 | Product Manager | Weekly (sprints) |

---

## 🎓 Learning Resources

### Relevant Reef Concepts
- Export architecture (`/Source/Reef/Core/Services/ExecutionService.cs`)
- Delta sync implementation (`/Source/Reef/Core/Services/DeltaSyncService.cs`)
- Scriban templating (`/Source/Reef/Core/TemplateEngines/ScribanTemplateEngine.cs`)
- Job scheduler (`/Source/Reef/Core/Services/JobSchedulerService.cs`)

### External References
- [Scriban Template Syntax](https://github.com/scriban/scriban/wiki)
- [Dapper ORM Documentation](https://github.com/DapperLib/Dapper)
- [ASP.NET Core Dependency Injection](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
- [SQL Server MERGE Statement](https://learn.microsoft.com/en-us/sql/t-sql/statements/merge-transact-sql)

---

## ✅ Checklist for Getting Started

- [ ] Read `01-IMPORT-ARCHITECTURE-OVERVIEW.md`
- [ ] Understand the 9-stage execution pipeline
- [ ] Review the key classes in `02-MODULE-CLASS-DESIGN.md`
- [ ] Examine the database schema in `03-DATA-SCHEMA-STORAGE.md`
- [ ] Trace through a real-world example in `04-EXECUTION-FLOW-EXAMPLES.md`
- [ ] Review the implementation phases in `05-IMPLEMENTATION-ROADMAP.md`
- [ ] Discuss questions with team
- [ ] Set up development environment
- [ ] Begin Phase 0 (Foundation)

---

**Version**: 1.0
**Status**: Ready for Implementation
**Last Updated**: 2025-11-09
**Next Review**: Week 1 (Phase 0 completion)

---

## Quick Links

- **Architecture Overview**: [01-IMPORT-ARCHITECTURE-OVERVIEW.md](01-IMPORT-ARCHITECTURE-OVERVIEW.md)
- **Module Design**: [02-MODULE-CLASS-DESIGN.md](02-MODULE-CLASS-DESIGN.md)
- **Data Schema**: [03-DATA-SCHEMA-STORAGE.md](03-DATA-SCHEMA-STORAGE.md)
- **Execution Examples**: [04-EXECUTION-FLOW-EXAMPLES.md](04-EXECUTION-FLOW-EXAMPLES.md)
- **Implementation Plan**: [05-IMPLEMENTATION-ROADMAP.md](05-IMPLEMENTATION-ROADMAP.md)
