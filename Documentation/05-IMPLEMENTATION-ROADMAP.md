# Reef Import: Implementation Roadmap

## Executive Summary

This document outlines a **phased implementation strategy** for integrating the Import mechanism into Reef. The approach prioritizes early delivery of MVP functionality while building toward comprehensive feature parity with exports.

**Timeline**: 8-12 weeks across 4 phases
**Team Size**: 2-3 engineers + 1 QA
**Effort**: ~480-720 person-hours

---

## 1. Phase 0: Preparation & Foundation (Weeks 1-1, 40-50 hours)

### Objectives
- Set up development environment
- Create database schema
- Establish code structure and CI/CD pipeline

### Deliverables

#### 1.1 Database Schema & Migration Scripts
- Create new tables: `ImportProfiles`, `ImportJobs`, `ImportExecutions`, `DataSourceConfigs`
- Extend `DeltaSyncState` for import support
- Create indexes and foreign key constraints
- **Estimated effort**: 16 hours
- **Acceptance criteria**:
  - Schema created via DatabaseInitializer
  - Migration script tested on fresh and existing databases
  - No breaking changes to export tables
  - All constraints validated

#### 1.2 Code Structure & DI Setup
- Create folder structure:
  ```
  Source/Reef/
    ├─ Core/
    │  ├─ Abstractions/
    │  │  ├─ IDataSourceExecutor.cs
    │  │  ├─ IDataWriter.cs
    │  │  ├─ IImportExecutionService.cs
    │  │  ├─ IImportProfileService.cs
    │  │  └─ IImportJobService.cs
    │  ├─ Services/
    │  │  ├─ Import/
    │  │  │  ├─ DataSourceExecutors/ (RestDataSourceExecutor, S3, FTP, etc.)
    │  │  │  ├─ Writers/ (DatabaseWriter, FileWriter, S3Writer, etc.)
    │  │  │  ├─ ImportExecutionService.cs
    │  │  │  ├─ ImportProfileService.cs
    │  │  │  └─ ImportJobService.cs
    │  │  └─ Factories/
    │  │     ├─ DataSourceExecutorFactory.cs
    │  │     └─ DataWriterFactory.cs
    │  └─ Models/
    │     ├─ ImportProfile.cs
    │     ├─ ImportJob.cs
    │     ├─ ImportExecution.cs
    │     ├─ FieldMapping.cs
    │     └─ ValidationRule.cs
    └─ Api/
       └─ ImportProfileEndpoints.cs
  ```
- **Estimated effort**: 12 hours
- **Acceptance criteria**:
  - Code compiles without errors
  - Dependency injection configured
  - Project structure mirrors export pattern
  - Unit test framework ready

#### 1.3 CI/CD Integration
- Add import-related tests to pipeline
- Configure Docker build for import services
- Set up test database containers
- **Estimated effort**: 8 hours
- **Acceptance criteria**:
  - GitHub Actions pipeline passes
  - Docker image builds successfully
  - Test coverage tracking enabled

#### 1.4 Documentation & Team Onboarding
- Create this roadmap + technical architecture docs
- Team onboarding session (1 hour)
- Set up design review process
- **Estimated effort**: 12 hours
- **Acceptance criteria**:
  - All team members understand architecture
  - Code review checklist created
  - Technical debt tracking system ready

### Phase 0 Metrics
- **Code coverage**: >80%
- **Build time**: <5 minutes
- **Database migration time**: <10 seconds
- **All tests passing**: 100%

---

## 2. Phase 1: MVP - REST API → Database (Weeks 2-4, 120-150 hours)

### Objectives
- Deliver minimal but complete import system
- Prove architectural design with end-to-end functionality
- Establish patterns for future connectors

### Deliverables

#### 2.1 Data Models & Repository Layer
**Core models**:
- `ImportProfile` (with validation)
- `ImportJob` (scheduling metadata)
- `ImportExecution` (execution history)
- `FieldMapping` & `ValidationRule`

**Repository interfaces**:
- `IImportProfileRepository`
- `IImportJobRepository`
- `IImportExecutionRepository`

**Effort**: 24 hours
**Tests**: Unit tests for model validation, repository CRUD operations
**Definition of done**:
- All models serialize/deserialize correctly
- Database persistence works end-to-end
- Soft delete support tested

#### 2.2 REST Data Source Executor
**Implement**: `RestDataSourceExecutor`
- HTTP GET support with custom headers
- Bearer token authentication
- Pagination support:
  - Cursor-based (offset/limit)
  - Offset/limit
  - Page number
- Retry logic with exponential backoff
- JSON parsing with JSONPath support

**Effort**: 36 hours
**Test coverage**: 85%+
- Unit tests: Happy path, error cases, retry logic, pagination
- Integration tests: Against mock REST API
- Definition of done**:
  - Fetches 10K+ rows from paginated API
  - Handles timeouts gracefully
  - Properly logs all requests/responses

#### 2.3 Database Writer
**Implement**: `DatabaseWriter`
- INSERT mode (insert all rows)
- UPSERT mode (SQL Server MERGE with key columns)
- Connection management (retry on connection failure)
- Transaction support (rollback on error)
- Batch optimization (batch insert 1000 rows at a time)

**Effort**: 32 hours
**Test coverage**: 85%+
- Unit tests: INSERT, UPSERT, error handling
- Integration tests: Against real SQL Server instance
- Definition of done**:
  - Inserts 10K rows in <2 minutes
  - UPSERT correctly identifies duplicates
  - Transaction rollback works

#### 2.4 Import Execution Service (Core Pipeline)
**Implement**: `ImportExecutionService` with 9-stage pipeline
1. Validation
2. Pre-fetch transformation (skip for MVP)
3. Source read
4. Delta sync (skip for MVP)
5. Row-level transformation (basic field mapping)
6. Schema validation
7. Write to destination
8. Post-write transformation (skip for MVP)
9. Commit & audit

**Effort**: 48 hours
**Test coverage**: 85%+
- Unit tests: Each stage in isolation
- Integration tests: Full pipeline with mock data
- Definition of done**:
  - Executes end-to-end
  - Error handling works (skip/fail modes)
  - Metrics collected and logged

#### 2.5 API Endpoints (MVP)
**Implement minimal REST API**:

```
POST   /api/import-profiles              - Create profile
GET    /api/import-profiles              - List profiles
GET    /api/import-profiles/{id}         - Get profile details
PUT    /api/import-profiles/{id}         - Update profile
DELETE /api/import-profiles/{id}         - Delete profile
POST   /api/import-profiles/{id}/run     - Trigger import

GET    /api/import-executions            - List executions
GET    /api/import-executions/{id}       - Get execution details
```

**Effort**: 20 hours
**Test coverage**: 80%+
- Unit tests: Validation, authorization
- Integration tests: API contract
- Definition of done**:
  - All endpoints tested
  - Error responses documented
  - Request/response schemas consistent

### Phase 1 Success Criteria
✅ Import REST API → SQL database end-to-end
✅ Handles 10K+ rows
✅ Manual trigger works
✅ Execution history captured
✅ All unit tests passing
✅ Integration tests passing
✅ Code review complete

### Phase 1 Risk Mitigation
| Risk | Mitigation |
|------|-----------|
| Database schema issues | Test migrations thoroughly before production |
| REST API pagination complexity | Use mocking for testing edge cases |
| Performance (slow writes) | Benchmark batch sizes, optimize connection pooling |
| Integration with existing scheduler | Design with existing JobScheduler first, then extend |

---

## 3. Phase 2: Delta Sync & Additional Connectors (Weeks 5-7, 160-200 hours)

### Objectives
- Add delta sync capability
- Support multiple source types (S3, FTP, Database)
- Implement transformation pipeline

### Deliverables

#### 3.1 Delta Sync Implementation
**Implement**: `IDeltaSyncService` extended for imports
- Hash-based delta detection (SHA256)
- Timestamp-based delta detection
- Key-based delta detection
- First-run behavior (all rows new)
- State management (create/update delta sync state)

**Effort**: 36 hours
**Test coverage**: 90%+
- Unit tests: Hash calculation, classification logic
- Integration tests: State persistence
- Definition of done**:
  - Correctly identifies new/changed/unchanged rows
  - 100% accuracy (zero false positives)
  - Handles first run (all new)

#### 3.2 S3 Data Source Executor
**Implement**: `S3DataSourceExecutor`
- List S3 objects with prefix filtering
- Download CSV/JSON files
- Streaming for large files (no memory explosion)
- Glob pattern matching (*.csv, *.json)
- AWS credential management (encrypted)

**Effort**: 32 hours
**Test coverage**: 80%+
- Unit tests: Filtering, pagination
- Integration tests: Against S3 or LocalStack
- Definition of done**:
  - Fetches 1GB+ files without memory issues
  - Supports CSV and JSON formats
  - Handles missing files gracefully

#### 3.3 FTP/SFTP Data Source Executor
**Implement**: `FtpDataSourceExecutor`
- FTP and SFTP protocol support
- Directory listing and filtering
- File download with resume support
- Credential management (encrypted)

**Effort**: 28 hours
**Test coverage**: 75%+
- Unit tests: Path parsing, filtering
- Integration tests: Against FTP server
- Definition of done**:
  - Downloads files successfully
  - Handles connection timeouts
  - Supports large files

#### 3.4 Database-to-Database Executor
**Implement**: `DatabaseDataSourceExecutor`
- Reuse existing `QueryExecutor`
- Support for SELECT queries from any connected database
- Parameterized queries (for security)

**Effort**: 12 hours
**Test coverage**: 85%+
- Unit tests: Query validation
- Integration tests: Against various databases
- Definition of done**:
  - Executes queries correctly
  - Handles large result sets

#### 3.5 Row-Level Transformation (Scriban)
**Implement**: Full Scriban support for field transformation
- Execute transformation templates
- Access to row data, context, metadata
- Type conversion within templates
- Default value application

**Effort**: 20 hours
**Test coverage**: 85%+
- Unit tests: Scriban syntax, filters
- Integration tests: Complex templates
- Definition of done**:
  - All Scriban filters work
  - Error messages clear

#### 3.6 Scheduled Job Execution
**Integrate**: Import jobs with existing `JobScheduler`
- Create `ImportJob` records
- Poll for due imports
- Queue import executions
- Handle circuit breaker (pause job on repeated failures)

**Effort**: 32 hours
**Test coverage**: 85%+
- Unit tests: Job scheduling logic
- Integration tests: Full scheduler integration
- Definition of done**:
  - Jobs execute on schedule
  - Circuit breaker activates correctly
  - No race conditions

#### 3.7 Error Handling & Quarantine
**Implement**: Configurable error strategies
- **Skip**: Skip failed rows, continue
- **Fail**: Abort entire import
- **Quarantine**: Write failed rows to quarantine location
- **Retry**: Retry with exponential backoff

**Effort**: 16 hours
**Test coverage**: 85%+
- Unit tests: Each error strategy
- Integration tests: Error scenarios
- Definition of done**:
  - All strategies work
  - Quarantine files created correctly
  - Retry logic doesn't infinite loop

### Phase 2 Success Criteria
✅ Delta sync working (new/changed/unchanged detection)
✅ Multiple source types supported (REST, S3, FTP, Database)
✅ Scheduled imports working
✅ Row transformation with Scriban
✅ Error quarantine working
✅ All tests passing
✅ Performance metrics acceptable

---

## 4. Phase 3: UI & Advanced Features (Weeks 8-10, 120-160 hours)

### Objectives
- User-friendly UI for managing imports
- Advanced features (validation, custom destinations)
- Comprehensive monitoring and alerting

### Deliverables

#### 4.1 Import Profiles UI
**Implement**: Web UI for creating/editing import profiles
- Profile builder form (source, destination, schedule, delta sync)
- Visual connection selector
- Template editor (pre/post-process, transformation)
- Field mapping editor (drag-drop)
- Validation rule editor

**Effort**: 48 hours
**Test coverage**: 75%+ (E2E tests)
- Unit tests: Form validation
- E2E tests: Full profile creation workflow
- Definition of done**:
  - Form validation works
  - All fields editable
  - Preview/test functionality

#### 4.2 Execution History & Monitoring
**Implement**: Dashboard for monitoring imports
- Execution history table (searchable, filterable)
- Detailed execution logs
- Error details with row-level error viewer
- Execution timeline chart
- Success/failure rate metrics

**Effort**: 32 hours
**Test coverage**: 75%+
- Unit tests: Data formatting
- E2E tests: Dashboard interactions
- Definition of done**:
  - Displays all execution data
  - Performance acceptable (<2s load time)
  - Logs viewable in detail

#### 4.3 Delta Sync State Viewer
**Implement**: UI to view and manage delta sync state
- Show last sync timestamp
- Row counts (new/changed/unchanged)
- Reset delta sync button (with confirmation)
- Delta state statistics

**Effort**: 16 hours
**Test coverage**: 75%+
- Unit tests: State calculation
- E2E tests: Reset functionality
- Definition of done**:
  - Shows accurate counts
  - Reset works correctly

#### 4.4 Additional Writers (File, S3)
**Implement**:
- `FileWriter` (CSV, JSON, Parquet output)
- `S3Writer` (multipart upload)
- `AzureBlobWriter` (if applicable)

**Effort**: 28 hours
**Test coverage**: 80%+
- Unit tests: Format generation
- Integration tests: File output
- Definition of done**:
  - Files created with correct format
  - Large file uploads work
  - Multipart retry working

#### 4.5 Validation & Schema Mapping
**Implement**:
- Field type validation (regex, min/max, enum)
- Schema inference from source
- Auto-mapping suggestion

**Effort**: 24 hours
**Test coverage**: 85%+
- Unit tests: Validation rules
- Integration tests: Schema inference
- Definition of done**:
  - Validation rules enforced
  - Schema inference accurate

#### 4.6 Documentation & Help
**Implement**:
- In-app help tooltips
- User guide (Getting Started with Imports)
- Troubleshooting guide
- API documentation

**Effort**: 20 hours
- Acceptance criteria:
  - All features documented
  - Examples provided
  - Help accessible in UI

### Phase 3 Success Criteria
✅ Import UI fully functional
✅ Monitoring dashboard working
✅ Multiple output formats supported
✅ Validation and error reporting
✅ Help documentation complete
✅ All tests passing
✅ UI/UX review approved

---

## 5. Phase 4: Optimization & Polish (Weeks 11-12, 80-120 hours)

### Objectives
- Performance optimization
- Production readiness
- Extended testing (load, security)

### Deliverables

#### 5.1 Performance Optimization
- **Batch optimization**: Fine-tune batch sizes (current: 1000)
- **Connection pooling**: Optimize database connection reuse
- **Memory management**: Stream large datasets instead of loading
- **Query optimization**: Ensure indexes used correctly
- **Caching**: Cache connection metadata, delta sync state

**Effort**: 24 hours
**Target metrics**:
- Import 100K rows in <2 minutes
- 50+ concurrent jobs without resource exhaustion
- Memory usage <500MB for 1M row operations

#### 5.2 Load Testing & Benchmarking
- Create load test scenarios (10K, 100K, 1M rows)
- Benchmark different source/destination combinations
- Identify bottlenecks and optimize
- Document performance characteristics

**Effort**: 20 hours
**Deliverables**:
- Load test suite
- Performance baseline document
- Optimization recommendations

#### 5.3 Security Audit
- Credential encryption validation
- SQL injection prevention
- API authentication/authorization
- Audit logging completeness
- GDPR compliance check

**Effort**: 16 hours
**Deliverables**:
- Security review document
- Vulnerability fixes (if any)
- Security test suite

#### 5.4 Integration Testing (Full Stack)
- Test all source/destination combinations
- Error recovery scenarios
- Circuit breaker behavior
- Concurrent executions

**Effort**: 20 hours
**Test matrix**:
```
Sources:      REST, S3, FTP, Database
Destinations: Database, File, S3
Sizes:        Small (100), Medium (10K), Large (100K)
Error modes:  Network timeout, constraint violation, parsing error
```

#### 5.5 Production Readiness
- Checklist for production deployment
- Runbook for troubleshooting
- Monitoring & alerting setup (Prometheus, DataDog, etc.)
- Backup/restore procedures
- Disaster recovery plan

**Effort**: 16 hours
**Deliverables**:
- Production checklist
- Monitoring dashboards
- Alerting rules
- Runbooks

#### 5.6 Documentation & Release Notes
- Update user documentation
- API documentation (OpenAPI/Swagger)
- Release notes for v1.0
- Migration guide (for upgrades)

**Effort**: 12 hours
**Deliverables**:
- Complete user guide
- API documentation
- Release notes

### Phase 4 Success Criteria
✅ 100K rows imported in <2 minutes
✅ Security audit passed
✅ Load test suite passing
✅ All integration tests passing
✅ Production monitoring configured
✅ Documentation complete
✅ Release approved by stakeholders

---

## 6. Timeline & Milestones

```
WEEK 1  Phase 0: Foundation
├─ Database schema created
├─ Code structure established
└─ CI/CD pipeline ready

WEEK 2-4  Phase 1: MVP (REST → DB)
├─ Week 2: Models, repository layer
├─ Week 3: REST executor, database writer
├─ Week 4: Execution service, API endpoints
└─ MILESTONE: MVP Complete (manual REST import working)

WEEK 5-7  Phase 2: Delta Sync & Connectors
├─ Week 5: Delta sync, S3 executor
├─ Week 6: FTP executor, DB executor, transformations
├─ Week 7: Scheduled jobs, error handling
└─ MILESTONE: Full automation (delta sync, scheduling working)

WEEK 8-10  Phase 3: UI & Features
├─ Week 8: Import UI, execution history
├─ Week 9: Additional writers, validation
├─ Week 10: Documentation, help
└─ MILESTONE: User-ready product (UI complete)

WEEK 11-12  Phase 4: Optimization & Release
├─ Week 11: Performance, load testing, security
├─ Week 12: Production readiness, release
└─ MILESTONE: v1.0 Release (production-ready)
```

---

## 7. Team Structure & Allocation

### Team Composition
- **Backend Engineer (3 FTE)**: Core services, executors, database layer
- **Frontend Engineer (1 FTE)**: UI implementation (from Phase 3)
- **QA Engineer (1 FTE)**: Testing, automation, load testing
- **Product/Tech Lead (0.5 FTE)**: Architecture decisions, reviews

### Weekly Standup Schedule
- Daily 15-minute sync (9:00 AM)
- Weekly architecture review (Monday, 1 hour)
- Weekly QA sync (Wednesday, 1 hour)

---

## 8. Risk Assessment & Mitigation

### High Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Delta sync accuracy (false positives) | Medium | High | Comprehensive unit/integration tests, manual validation on first 10 imports |
| Performance degradation (slow writes) | Medium | High | Benchmark early (Phase 1), optimize batch sizes, connection pooling |
| Schedule conflicts with existing export jobs | Medium | High | Design JobScheduler integration early, use shared queue |
| Third-party API rate limiting | Medium | Medium | Implement rate limit detection, backoff logic, queue management |

### Medium Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| S3/FTP library compatibility | Low | Medium | Test libraries early, use well-maintained packages |
| Scriban template complexity | Medium | Medium | Provide examples, validation in UI, error messages |
| Concurrent import conflicts | Low | Medium | Use database locks for delta sync state, test race conditions |
| Data consistency on partial failure | Low | Medium | Implement transaction rollback, retry idempotency |

### Low Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| Breaking changes to exports | Very Low | High | Architecture review, integration tests, manual testing |
| User confusion with UI | Low | Low | UX testing, help documentation, tooltips |

---

## 9. Success Metrics

### Technical Metrics
- **Code coverage**: ≥85% overall, ≥90% for critical paths
- **Test pass rate**: 100%
- **Performance**: 100K rows in <2 minutes
- **Uptime**: ≥99.5%
- **Error rate**: <0.1% (failed rows / total rows)

### Operational Metrics
- **Time to import**: 50-200 ms/row (depending on destination)
- **Memory usage**: <500MB for 1M row operations
- **Concurrent jobs**: ≥50 without degradation
- **Execution reliability**: ≥95% success rate (excluding user errors)

### Business Metrics
- **User adoption**: ≥30% of export users adopt imports within 3 months
- **Support tickets**: <5 issues per month (post-launch)
- **Feature requests**: Track new connector/feature requests
- **Performance SLA**: 99.5% availability

---

## 10. Dependencies & Assumptions

### External Dependencies
- SQL Server/SQLite database available
- REST API(s) for testing
- S3 bucket for testing
- FTP server for testing
- Existing Reef codebase stability

### Assumptions
- No breaking changes to existing Reef API
- Team experience with Dapper, ASP.NET Core
- Existing scheduler can be extended without modification
- Database schema changes are non-breaking (additive only)

---

## 11. Post-Launch: Phase 5 (Future)

### Future Enhancements (Q2 2025)
- Bidirectional sync (import ↔ export)
- Kafka/event streaming sources
- Database CDC (Change Data Capture)
- GraphQL API support
- Advanced scheduling (conditional triggers)
- Machine learning for anomaly detection
- Webhook destinations
- Custom Lua/Python script support

### Long-Term Vision (Q3-Q4 2025)
- Data quality monitoring
- Data lineage & impact analysis
- Cross-profile dependency chains
- Real-time streaming imports
- Multi-tenant support
- Advanced audit & compliance features

---

## Approval & Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Engineering Lead | TBD | | |
| Product Manager | TBD | | |
| Architect | TBD | | |
| QA Lead | TBD | | |

---

## Appendix: Definition of Done

### Code
- [ ] Follows code style guide
- [ ] Unit tests written (85%+ coverage)
- [ ] Integration tests written (if applicable)
- [ ] Code reviewed and approved
- [ ] No compiler warnings
- [ ] Documentation updated

### Testing
- [ ] Unit tests passing
- [ ] Integration tests passing
- [ ] Manual testing complete
- [ ] Edge cases tested
- [ ] Error scenarios tested
- [ ] Performance tested

### Documentation
- [ ] Code comments added
- [ ] API documentation updated
- [ ] User documentation updated (if UI change)
- [ ] README updated
- [ ] Troubleshooting guide updated

### Quality
- [ ] Sonarqube pass (if applicable)
- [ ] Security review passed
- [ ] Performance acceptable
- [ ] Accessibility check (if UI)
- [ ] Backward compatibility maintained

---

**Document Version**: 1.0
**Status**: Ready for Review
**Next Step**: Team review & approval
