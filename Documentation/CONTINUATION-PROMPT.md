# Reef Import Architecture: Continuation Prompt

**Use this prompt in new conversations to continue implementation work.**

---

## 🎯 Context & Background

We are implementing a **fully symmetrical Import mechanism for Reef**, a .NET-based data orchestration platform. The Import system mirrors the existing Export architecture, enabling inbound data ingestion from multiple sources (REST APIs, S3, FTP, databases) to multiple destinations with delta sync, transformations, scheduling, and full auditability.

### Documentation Available
All architectural documentation is available in `/mnt/d/Repository/reef/Documentation/`:

1. **00-INDEX-AND-QUICK-REFERENCE.md** - Navigation guide and quick reference
2. **01-IMPORT-ARCHITECTURE-OVERVIEW.md** - System design principles and overview
3. **02-MODULE-CLASS-DESIGN.md** - Concrete class definitions and interfaces
4. **03-DATA-SCHEMA-STORAGE.md** - Database schema, DDL, migrations
5. **04-EXECUTION-FLOW-EXAMPLES.md** - Detailed execution pipeline with examples
6. **05-IMPLEMENTATION-ROADMAP.md** - Phased implementation plan (4 phases, 8-12 weeks)

### Key Principle
**Symmetry**: Imports are exports in reverse. We reuse ~65% of existing components (Scheduler, DeltaSyncService, ScribanTemplateEngine, ConnectionService, ExecutionLogger) to minimize code duplication and maintain consistency.

---

## 📋 Implementation Phases

### Phase 0: Preparation & Foundation ✅ (Completed)
**Status**: Design documentation generated
**Deliverables**:
- Comprehensive architecture design (6 documents)
- Database schema DDL
- Class/interface definitions
- Implementation roadmap with effort estimates

**What "Completed" means**: Team has documented architecture and is ready to begin Phase 1 development.

---

### Phase 1: MVP - REST API → SQL Database 🚀 (Ready to Start)
**Duration**: Weeks 2-4 (120-150 hours, 2-3 engineers)
**Goal**: Deliver minimal but complete import system; prove architectural design with end-to-end functionality.

**Deliverables**:
1. **Data Models & Repository Layer** (24 hours)
   - Implement: `ImportProfile`, `ImportJob`, `ImportExecution`, `FieldMapping`, `ValidationRule`
   - Implement: `IImportProfileRepository`, `IImportJobRepository`, `IImportExecutionRepository`
   - Add to: `/Source/Reef/Core/Models/` and `/Source/Reef/Core/Repositories/`
   - Reference: `02-MODULE-CLASS-DESIGN.md` (section 1.1)

2. **REST Data Source Executor** (36 hours)
   - Implement: `RestDataSourceExecutor : IDataSourceExecutor`
   - Features: HTTP GET, custom headers, Bearer auth, pagination (cursor/offset/page), retry logic
   - Location: `/Source/Reef/Core/Services/Import/DataSourceExecutors/RestDataSourceExecutor.cs`
   - Reference: `02-MODULE-CLASS-DESIGN.md` (section 2.2)
   - Test: Fetch 10K+ rows, handle timeouts, test pagination

3. **Database Writer** (32 hours)
   - Implement: `DatabaseWriter : IDataWriter`
   - Features: INSERT mode, UPSERT mode (SQL Server MERGE), batch optimization, transactions
   - Location: `/Source/Reef/Core/Services/Import/Writers/DatabaseWriter.cs`
   - Reference: `02-MODULE-CLASS-DESIGN.md` (section 3.1)
   - Test: Insert/UPSERT 10K rows in <2 minutes, verify transaction rollback

4. **Import Execution Service** (48 hours)
   - Implement: `ImportExecutionService : IImportExecutionService`
   - Features: 9-stage pipeline (validation → source read → transform → validate → write → commit)
   - Location: `/Source/Reef/Core/Services/Import/ImportExecutionService.cs`
   - Reference: `02-MODULE-CLASS-DESIGN.md` (section 4), `04-EXECUTION-FLOW-EXAMPLES.md` (section 2)
   - Test: End-to-end pipeline with mock data

5. **API Endpoints (MVP)** (20 hours)
   - Implement: Minimal REST API (6 endpoints for profiles, 3 for executions)
   - Location: `/Source/Reef/Api/ImportProfileEndpoints.cs`
   - Reference: `01-IMPORT-ARCHITECTURE-OVERVIEW.md` (section 7)
   - Test: All endpoints tested with valid/invalid inputs

6. **Database Schema & Migrations** (16 hours)
   - Create: `ImportProfiles`, `ImportJobs`, `ImportExecutions` tables
   - Location: `/Source/Reef/Helpers/DatabaseInitializer.cs` (add method `InitializeImportTablesAsync()`)
   - Reference: `03-DATA-SCHEMA-STORAGE.md` (sections 1-3)
   - Test: Migration works on fresh and existing databases

7. **Dependency Injection Setup** (8 hours)
   - Register: All services, executors, writers, repositories
   - Location: `/Source/Reef/Program.cs` (add `AddImportServices()`)
   - Reference: `02-MODULE-CLASS-DESIGN.md` (section 5)
   - Test: DI container resolves all dependencies correctly

**Phase 1 Success Criteria**:
- ✅ REST API → SQL database import works end-to-end
- ✅ Manual trigger via API works
- ✅ Executes 10K rows in <2 minutes
- ✅ Execution history captured in database
- ✅ ≥85% code coverage on core logic
- ✅ All unit & integration tests passing
- ✅ No compiler warnings
- ✅ Code reviewed and approved

**What "Completed" means**: You can manually import data from a REST API into a SQL Server database. You have a working foundation to build Phase 2 on top of.

---

### Phase 2: Delta Sync & Multiple Connectors 📦 (Follows Phase 1)
**Duration**: Weeks 5-7 (160-200 hours)
**Goal**: Enable delta sync (new/changed/unchanged detection), support multiple sources (S3, FTP, Database), add Scriban transformations.

**Deliverables**:
1. Delta Sync implementation (hash-based detection)
2. S3DataSourceExecutor (list objects, download files, streaming)
3. FtpDataSourceExecutor (FTP/SFTP protocol support)
4. DatabaseDataSourceExecutor (direct SQL queries)
5. Row-level transformation (Scriban templates)
6. Scheduled job execution (integrate with JobScheduler)
7. Error handling & quarantine (Skip/Fail/Quarantine/Retry strategies)

**Reference**: `05-IMPLEMENTATION-ROADMAP.md` (section 3)

**What "Completed" means**: You have delta sync detecting changes accurately, multiple source types supported, and scheduled imports working. You can import from REST, S3, FTP, or another database, with intelligent change detection.

---

### Phase 3: UI & Advanced Features 🎨 (Follows Phase 2)
**Duration**: Weeks 8-10 (120-160 hours)
**Goal**: User-friendly UI for managing imports, advanced features, comprehensive monitoring.

**Deliverables**:
1. Import Profiles UI (visual profile builder)
2. Execution History & Monitoring dashboard
3. Delta Sync State viewer
4. Additional writers (File, S3)
5. Validation & schema mapping UI
6. Documentation & help

**Reference**: `05-IMPLEMENTATION-ROADMAP.md` (section 4)

**What "Completed" means**: Non-technical users can create, schedule, and monitor imports via a web interface. Full feature parity with exports from user perspective.

---

### Phase 4: Optimization & Production Release 🚀 (Follows Phase 3)
**Duration**: Weeks 11-12 (80-120 hours)
**Goal**: Performance optimization, security audit, production readiness.

**Deliverables**:
1. Performance optimization (batch sizes, connection pooling, caching)
2. Load testing (10K, 100K, 1M row scenarios)
3. Security audit (credential encryption, SQL injection prevention, audit logging)
4. Integration testing (all source/destination combinations)
5. Production readiness (monitoring, alerting, runbooks, disaster recovery)
6. Documentation & release notes

**Reference**: `05-IMPLEMENTATION-ROADMAP.md` (section 5)

**What "Completed" means**: Import feature is production-ready, thoroughly tested, secure, and performant. Ready for official v1.0 release.

---

## 🔄 How to Use This Prompt

### Starting a New Conversation for Phase X:

```
I'm continuing Reef Import architecture implementation.

Current Status: Phase [X-1] completed
Next Phase: Phase [X] - [Phase Name]

Documentation available:
- /mnt/d/Repository/reef/Documentation/00-INDEX-AND-QUICK-REFERENCE.md
- /mnt/d/Repository/reef/Documentation/01-IMPORT-ARCHITECTURE-OVERVIEW.md
- /mnt/d/Repository/reef/Documentation/02-MODULE-CLASS-DESIGN.md
- /mnt/d/Repository/reef/Documentation/03-DATA-SCHEMA-STORAGE.md
- /mnt/d/Repository/reef/Documentation/04-EXECUTION-FLOW-EXAMPLES.md
- /mnt/d/Repository/reef/Documentation/05-IMPLEMENTATION-ROADMAP.md

Please help me with Phase [X]:
1. [Specific deliverable from phase]
2. [Specific deliverable from phase]
3. [Specific deliverable from phase]

I need:
- [ ] Code implementation for [component]
- [ ] Unit tests for [component]
- [ ] Integration tests for [component]
- [ ] Database schema validation
- [ ] Code review checklist
```

### Example for Phase 1:

```
I'm continuing Reef Import architecture implementation.

Current Status: Phase 0 completed (architecture designed)
Next Phase: Phase 1 - MVP (REST API → SQL Database)

Documentation available:
- /mnt/d/Repository/reef/Documentation/00-INDEX-AND-QUICK-REFERENCE.md
- [All 6 docs as above]

Please help me implement Phase 1 deliverables:
1. Data Models & Repository Layer
2. REST Data Source Executor
3. Database Writer (with UPSERT)

I need:
- [ ] Code templates for models and repositories
- [ ] Implementation guidance for RestDataSourceExecutor
- [ ] Database schema migration script
- [ ] Unit test templates
- [ ] DI setup checklist
```

---

## 📚 Quick Navigation by Phase

### Phase 1: MVP Development
- **Overview**: `01-IMPORT-ARCHITECTURE-OVERVIEW.md` (read sections 3-5)
- **Classes to implement**: `02-MODULE-CLASS-DESIGN.md` (sections 1-5)
- **Database setup**: `03-DATA-SCHEMA-STORAGE.md` (sections 1-3)
- **Testing guide**: `04-EXECUTION-FLOW-EXAMPLES.md` (sections 1-2)
- **Timeline**: `05-IMPLEMENTATION-ROADMAP.md` (section 2)

### Phase 2: Delta Sync & Connectors
- **Classes to implement**: `02-MODULE-CLASS-DESIGN.md` (sections 2-3)
- **Delta sync logic**: `04-EXECUTION-FLOW-EXAMPLES.md` (section 5)
- **Timeline**: `05-IMPLEMENTATION-ROADMAP.md` (section 3)

### Phase 3: UI & Features
- **UI requirements**: `01-IMPORT-ARCHITECTURE-OVERVIEW.md` (section 7)
- **Timeline**: `05-IMPLEMENTATION-ROADMAP.md` (section 4)

### Phase 4: Optimization & Release
- **Performance targets**: `05-IMPLEMENTATION-ROADMAP.md` (section 5)
- **Success metrics**: `05-IMPLEMENTATION-ROADMAP.md` (section 9)

---

## 🛠️ Implementation Checklist Template

Use this checklist for each phase:

```markdown
# Phase [X] Implementation Checklist

## Deliverable 1: [Name]
- [ ] Code implemented
- [ ] Unit tests written (≥85% coverage)
- [ ] Integration tests written
- [ ] Code reviewed
- [ ] Documentation updated
- [ ] Merged to main branch

## Deliverable 2: [Name]
- [ ] Code implemented
- [ ] Unit tests written (≥85% coverage)
- [ ] Integration tests written
- [ ] Code reviewed
- [ ] Documentation updated
- [ ] Merged to main branch

[... continue for all deliverables ...]

## Phase Success Criteria
- [ ] All acceptance criteria met
- [ ] All tests passing
- [ ] Code coverage ≥85%
- [ ] No compiler warnings
- [ ] Code review approved
- [ ] Ready for next phase
```

---

## 📞 Key Questions to Ask When Starting Each Phase

1. **What is the current codebase state?**
   - Have migrations from Phase [X-1] been applied?
   - Are there any uncommitted changes?
   - What branch are we working on?

2. **What are the specific blockers or questions?**
   - Any unclear architectural decisions?
   - Dependencies on external systems?
   - Integration challenges with existing code?

3. **What's the testing strategy?**
   - Mock vs. real API/database for testing?
   - Performance benchmarks to meet?
   - Load testing environment available?

4. **What's the timeline?**
   - Actual duration vs. estimated 120-150 hours for Phase 1?
   - Team availability?
   - Dependencies on other teams?

---

## 🎓 Key Design Principles (Reminder)

**Remember these when implementing**:

1. **Symmetry**: Reuse export components (Scheduler, DeltaSyncService, TemplateEngine)
2. **No breaking changes**: All changes additive only
3. **Delta sync integrity**: Only commit state after successful write
4. **Error handling**: Configurable strategies (Skip, Fail, Quarantine, Retry)
5. **Security**: All credentials encrypted, never logged
6. **Auditability**: Every execution logged with metrics and audit trail
7. **Extensibility**: Pluggable sources and writers for future connectors

---

## ✅ Phase Completion Definition

**A phase is "Completed" when**:

1. ✅ All deliverables implemented
2. ✅ ≥85% code coverage on new code
3. ✅ All unit tests passing
4. ✅ All integration tests passing
5. ✅ Code reviewed and approved
6. ✅ Changes merged to main branch
7. ✅ Documentation updated
8. ✅ Success criteria met
9. ✅ Ready to start next phase

**When marking phase complete, include**:
- Summary of deliverables completed
- Test coverage percentage
- Any known issues or blockers
- Recommendations for next phase

---

## 🚀 Continuing to Next Phase

When Phase 1 is complete, use this template to start Phase 2:

```
I'm continuing Reef Import architecture implementation.

Current Status: Phase 1 COMPLETED ✅
- REST API → SQL database import working end-to-end
- Manual trigger via API tested
- 10K rows imported in [X] seconds
- Code coverage: [X]%
- All tests passing
- Code merged to main

Next Phase: Phase 2 - Delta Sync & Multiple Connectors

Deliverables for Phase 2:
1. Delta sync implementation (hash-based detection)
2. S3 data source executor
3. FTP data source executor
4. Database data source executor
5. Row-level transformation (Scriban)
6. Scheduled job execution
7. Error handling & quarantine

Please help me with Phase 2. Start by:
1. [Specific first task]
2. [Specific second task]
3. [Specific third task]
```

---

## 📖 Reading Guide for New Conversations

**If continuing Phase 1**:
- Read `02-MODULE-CLASS-DESIGN.md` (the specific class you're implementing)
- Reference `04-EXECUTION-FLOW-EXAMPLES.md` for testing guidance
- Check `03-DATA-SCHEMA-STORAGE.md` for database setup

**If continuing Phase 2+**:
- Start with `05-IMPLEMENTATION-ROADMAP.md` (section for that phase)
- Reference architecture docs as needed
- Check execution flow examples for timing expectations

---

## 🔗 File Locations

```
Reef Codebase Root: /mnt/d/Repository/reef/

Documentation:
├── /Documentation/00-INDEX-AND-QUICK-REFERENCE.md
├── /Documentation/01-IMPORT-ARCHITECTURE-OVERVIEW.md
├── /Documentation/02-MODULE-CLASS-DESIGN.md
├── /Documentation/03-DATA-SCHEMA-STORAGE.md
├── /Documentation/04-EXECUTION-FLOW-EXAMPLES.md
├── /Documentation/05-IMPLEMENTATION-ROADMAP.md
└── /Documentation/CONTINUATION-PROMPT.md (this file)

Source Code (to be created):
├── /Source/Reef/Core/Models/ImportProfile.cs
├── /Source/Reef/Core/Models/ImportJob.cs
├── /Source/Reef/Core/Models/ImportExecution.cs
├── /Source/Reef/Core/Abstractions/IDataSourceExecutor.cs
├── /Source/Reef/Core/Abstractions/IDataWriter.cs
├── /Source/Reef/Core/Abstractions/IImportExecutionService.cs
├── /Source/Reef/Core/Services/Import/DataSourceExecutors/
│   ├── RestDataSourceExecutor.cs
│   ├── S3DataSourceExecutor.cs
│   ├── FtpDataSourceExecutor.cs
│   └── DatabaseDataSourceExecutor.cs
├── /Source/Reef/Core/Services/Import/Writers/
│   ├── DatabaseWriter.cs
│   ├── FileWriter.cs
│   └── S3Writer.cs
├── /Source/Reef/Core/Services/Import/ImportExecutionService.cs
├── /Source/Reef/Api/ImportProfileEndpoints.cs
└── /Source/Reef/Helpers/DatabaseInitializer.cs (extend with import tables)
```

---

## 💬 Template Prompt (Copy & Paste Ready)

```
I'm continuing Reef Import architecture implementation.

Current Status: Phase [PHASE_NUMBER] [STATUS: In Progress / Completed]

Next Action: [Implement / Debug / Refactor] [Component Name]

Documentation Available:
- Architecture: /mnt/d/Repository/reef/Documentation/
- Current plan: /mnt/d/Repository/reef/Documentation/05-IMPLEMENTATION-ROADMAP.md

Specific Task:
[Describe what you want to implement or fix]

I need:
- [ ] Code implementation
- [ ] Unit tests
- [ ] Integration tests
- [ ] Documentation
- [ ] Code review checklist

Reference Documentation:
- [Relevant doc section from the 6 documents]
- [Specific class/interface to implement]
- [Expected behavior/output]

Current Blockers:
[List any issues or unclear points]

Please help me:
1. [First specific request]
2. [Second specific request]
3. [Third specific request]
```

---

## 📊 Progress Tracking

Use this to track phase completion:

| Phase | Status | Start Date | End Date | Coverage | Notes |
|-------|--------|-----------|----------|----------|-------|
| Phase 0 | ✅ Complete | 2025-11-09 | 2025-11-09 | 100% | Architecture designed |
| Phase 1 | 🚀 Ready | TBD | TBD | TBD | MVP development |
| Phase 2 | ⏳ Pending | TBD | TBD | TBD | Delta sync & connectors |
| Phase 3 | ⏳ Pending | TBD | TBD | TBD | UI & features |
| Phase 4 | ⏳ Pending | TBD | TBD | TBD | Optimization & release |

---

## 🎯 Final Note

**This documentation is self-contained and comprehensive.** Each phase builds on the previous one. When you mark a phase "Complete" and move to the next conversation:

1. ✅ Copy the phase completion summary
2. ✅ Reference the next phase section from this prompt
3. ✅ Include specific deliverables from `05-IMPLEMENTATION-ROADMAP.md`
4. ✅ Ask questions about blockers or clarifications
5. ✅ Proceed systematically through deliverables

**The next conversation should start with**: "I'm continuing Reef Import architecture implementation. Phase [X-1] completed. Starting Phase [X]..."

---

**Version**: 1.0
**Created**: 2025-11-09
**For use**: Multi-conversation continuation of Reef Import architecture
**Reviewed by**: Architecture team
