# Phase 4: Performance Analysis & Optimization Plan

**Date**: 2025-11-09
**Status**: Analysis Complete - Ready for Implementation
**Document Version**: 1.0

---

## Executive Summary

The Reef Import system (Phases 1-3) is **functionally complete** and **architecturally sound**, but has **critical performance gaps** that prevent production use at scale.

**Key Finding**: Current implementation uses **in-memory batch processing** with **row-by-row database operations**, limiting throughput to ~50-100 rows/second vs. target of **500+ rows/second** (100K rows in <2 minutes).

**Phase 4 Goal**: Optimize for production scale while maintaining code quality and reliability.

---

## 1. Current Performance Characteristics

### 1.1 Baseline Metrics (Estimated from Code Analysis)

| Metric | Current | Target | Gap |
|--------|---------|--------|-----|
| Throughput (rows/sec) | 50-100 | 500+ | 5-10x slower |
| Memory (100K rows) | ~800MB | <200MB | 4x higher |
| 100K rows execution time | 20-30 min | <2 min | 10-15x slower |
| Connection reuse | None (new/close per write) | Pooled | Missing |
| Batch efficiency | Row-by-row | Batched (1000) | Missing |
| Delta sync performance | O(n²) hash comparisons | O(n) with indexing | Sub-optimal |

### 1.2 Performance Bottlenecks Identified

#### CRITICAL (Blocking Performance Goals)

**1. Row-by-Row Database Operations** (DatabaseWriter.cs:188-202)
```csharp
foreach (var row in rows)  // 100K iterations!
{
    await connection.ExecuteAsync(insertSql, row);  // One INSERT per row
}
```
**Impact**: 100K rows = 100K round-trips to database
**Solution**: Batch INSERT statements (1000 rows at a time)
**Expected Improvement**: 10-20x faster writes

**2. Full Dataset in Memory** (ImportExecutionService.cs:69, 104, 108)
```csharp
List<Dictionary<string, object>> rows = ...;  // Entire result set in RAM
var rowsToProcess = _deltaSyncResult.NewRows.Concat(...).ToList();  // Multiple copies
```
**Impact**: 100K rows × 4KB/row = ~400MB; 1M rows = ~4GB
**Solution**: Stream rows through pipeline in smaller chunks (5000 row batches)
**Expected Improvement**: 80% memory reduction

**3. No Database Connection Pooling** (DatabaseWriter.cs:67)
```csharp
using var connection = new SqliteConnection(connectionString);
// Creates new connection for each write operation
```
**Impact**: Connection overhead ~100ms per operation
**Solution**: Implement SqliteConnectionPool or use SqlConnectionStringBuilder pooling
**Expected Improvement**: 2-3x faster connections

#### HIGH (Performance Degradation)

**4. Repeated Profile Lookups** (ImportExecutionService.cs:200, 214, 241, 272)
```csharp
var profile = await _profileService.GetByIdAsync(profileId, cancellationToken);  // Called 5+ times per execution
```
**Impact**: 5+ database queries per import execution
**Solution**: Load profile once at start, reuse throughout
**Expected Improvement**: 10-20ms per execution

**5. Single-Threaded Pipeline** (ImportExecutionService.cs:44-164)
```csharp
// Pipeline executes sequentially: Source Read → Transform → Validate → Write
```
**Impact**: Cannot parallelize independent operations
**Solution**: Parallel stages where possible (field mappings, validation per-row)
**Expected Improvement**: 2-3x faster for IO-bound stages

**6. Inefficient Delta Sync** (ImportDeltaSyncService.cs:44-50)
```csharp
var sortedKeys = row.Keys.OrderBy(k => k).ToList();  // Sorting every row
foreach (var key in sortedKeys)  // Iterating all columns
{
    var value = NormalizeValueForHash(row[key]);
}
```
**Impact**: O(n·m·log(m)) where m = number of columns
**Solution**: Pre-calculate hash for key columns only, cache schema
**Expected Improvement**: 3-5x faster delta sync

#### MEDIUM (Nice-to-Have)

**7. REST API Pagination In-Memory** (RestDataSourceExecutor.cs:42, 70)
```csharp
var allRows = new List<Dictionary<string, object>>();
while (hasMore) {
    allRows.AddRange(rows);  // Could stream instead
}
```
**Impact**: Must fetch all data before processing
**Solution**: Yield rows as they're fetched
**Expected Improvement**: Faster time-to-first-row

---

## 2. Optimization Strategy

### Phase 4.1: Critical Path (Days 1-3)
1. Implement batch database writes (1000-row batches)
2. Add database connection pooling
3. Load profile once, reuse throughout
4. Result: 5-8x performance improvement

### Phase 4.2: Memory Optimization (Days 4-5)
5. Implement chunked streaming pipeline
6. Use ValueTuple<> instead of Dictionary<> for hot paths
7. Implement object pooling for temp collections
8. Result: 80% memory reduction

### Phase 4.3: Advanced Optimization (Days 6-8)
9. Parallelize independent operations
10. Optimize delta sync with column caching
11. Implement query result streaming
12. Result: 2-3x additional improvement

### Phase 4.4: Testing & Tuning (Days 9-12)
13. Load testing (10K, 100K, 1M rows)
14. Profile critical sections
15. Fine-tune batch sizes and thread pool
16. Document performance characteristics

---

## 3. Detailed Implementation Plan

### 3.1 Batch Database Writes

**Current Code** (DatabaseWriter.cs:188-202):
```csharp
foreach (var row in rows) {
    await connection.ExecuteAsync(insertSql, row);  // 100K calls
}
```

**Optimized Approach**:
```csharp
const int batchSize = 1000;
for (int i = 0; i < rows.Count; i += batchSize) {
    var batch = rows.Skip(i).Take(batchSize).ToList();
    await connection.ExecuteAsync(multiInsertSql, batch);  // ~100 calls
}
```

**Files to Modify**:
- `/Source/Reef/Core/Services/Import/Writers/DatabaseWriter.cs`
  - Implement `InsertBatchAsync()` method
  - Implement `UpsertBatchAsync()` method
  - Use `UNION ALL` for multi-row INSERT (SQLite/SQL Server)

**Effort**: 4 hours
**Test**: Unit tests for batch sizes 100, 500, 1000, 5000

---

### 3.2 Connection Pooling

**Current Code** (DatabaseWriter.cs:67):
```csharp
using var connection = new SqliteConnection(connectionString);
```

**Optimized Approach**:
```csharp
var options = new SqliteConnectionStringBuilder(connectionString)
{
    Pooling = true,
    MaxPoolSize = 20
};
using var connection = new SqliteConnection(options.ToString());
```

**Files to Modify**:
- `/Source/Reef/Core/Services/Import/Writers/DatabaseWriter.cs`
- `/Source/Reef/Core/Services/Import/DataSourceExecutors/DatabaseDataSourceExecutor.cs`
- `/Source/Reef/Core/Services/Import/ImportDeltaSyncService.cs`

**Configuration**:
```csharp
// DatabaseWriterConfig.cs
[JsonPropertyName("poolSize")]
public int PoolSize { get; set; } = 20;

[JsonPropertyName("poolingEnabled")]
public bool PoolingEnabled { get; set; } = true;
```

**Effort**: 2 hours
**Test**: Connection pool stress tests

---

### 3.3 Profile Caching (Single Load)

**Current Code** (ImportExecutionService.cs:200, 214, 241, 272):
```csharp
var profile = await _profileService.GetByIdAsync(profileId);  // Called 5+ times
```

**Optimized Approach**:
```csharp
private async Task<ImportExecutionResult> ExecuteAsync(int profileId, ...) {
    var profile = await _profileService.GetByIdAsync(profileId, cancellationToken);  // Once
    var rows = await StageSourceReadAsync(profileId, profile, execution, cancellationToken);
    // Pass profile to all stages instead of looking it up again
}

private async Task<List<Dictionary<string, object>>> StageSourceReadAsync(
    int profileId,
    ImportProfile profile,  // Pass as parameter
    ImportExecution execution,
    CancellationToken cancellationToken)
{
    // Use profile directly, no lookup needed
}
```

**Files to Modify**:
- `/Source/Reef/Core/Services/Import/ImportExecutionService.cs`
  - Modify stage method signatures to accept `ImportProfile`
  - Load profile once at ExecuteAsync start
  - Pass through all stages

**Effort**: 2 hours
**Test**: Unit tests verify profile is loaded once

---

### 3.4 Streaming Pipeline

**Current Code** (ImportExecutionService.cs:69-100):
```csharp
var rows = await StageSourceReadAsync(profileId, execution, cancellationToken);
// ... all rows in memory until write ...
var writeResult = await StageWriteAsync(profileId, rows, execution, cancellationToken);
```

**Optimized Approach**:
```csharp
// Process in 5000-row chunks
const int chunkSize = 5000;
var totalRows = 0;
var totalWritten = 0;

await foreach (var chunk in StageSourceReadAsyncIterator(profileId, execution, cancellationToken)) {
    var transformedChunk = await StageTransformAsync(profileId, chunk, cancellationToken);
    var validChunk = await StageValidateDataAsync(profileId, transformedChunk, cancellationToken);
    var writeResult = await StageWriteAsync(profileId, validChunk, cancellationToken);
    totalRows += chunk.Count;
    totalWritten += writeResult.RowsWritten;
}
```

**Files to Modify**:
- `/Source/Reef/Core/Services/Import/ImportExecutionService.cs`
  - Create `ExecuteStreamingAsync()` as alternative to current sync
  - Data source executors: Add `ExecuteStreamingAsync(IAsyncEnumerable<Dictionary<string, object>>)`
  - Implement chunking in pipeline

**Effort**: 8 hours
**Test**: Verify memory usage stays <200MB for 100K rows

---

### 3.5 Optimized Delta Sync

**Current Code** (ImportDeltaSyncService.cs:44-50):
```csharp
var sortedKeys = row.Keys.OrderBy(k => k).ToList();  // Every row
foreach (var key in sortedKeys) {
    sb.Append($"{key}={value};");  // All columns
}
```

**Optimized Approach**:
```csharp
// Hash only key columns + modified timestamp
public async Task<string> CalculateRowHashAsync(
    Dictionary<string, object> row,
    List<string> keyColumns,  // Pre-calculated
    string? modifiedTimeColumn = null)
{
    var sb = new StringBuilder();

    // Only hash key columns
    foreach (var keyCol in keyColumns) {
        if (row.TryGetValue(keyCol, out var value)) {
            sb.Append(NormalizeValueForHash(value));
        }
    }

    return CalculateHash(sb.ToString());
}
```

**Configuration in ImportProfile**:
```csharp
[JsonPropertyName("deltaKeyColumns")]
public List<string> DeltaKeyColumns { get; set; } = new();

[JsonPropertyName("deltaModifiedTimeColumn")]
public string? DeltaModifiedTimeColumn { get; set; }
```

**Files to Modify**:
- `/Source/Reef/Core/Services/Import/ImportDeltaSyncService.cs`
  - Optimize hash calculation for key columns only
  - Use timestamp-based comparison when available
  - Cache column list per profile

**Effort**: 3 hours
**Test**: Verify delta detection accuracy unchanged

---

### 3.6 Parallel Processing

**Current Code** (ImportExecutionService.cs):
```csharp
// Sequential: Read → Transform → Validate → Write
var rows = await StageSourceReadAsync(...);
rows = await StageTransformAsync(..., rows, ...);
var validationResult = await StageValidateDataAsync(..., rows, ...);
var writeResult = await StageWriteAsync(..., rows, ...);
```

**Optimized Approach**:
```csharp
// Parallel field transformation per row
var transformedRows = rows.AsParallel()
    .Select(row => _transformationService.TransformRow(row, profile))
    .ToList();

// Parallel validation per row
var validationResults = rows.AsParallel()
    .Select(row => ValidateRow(row, profile.ValidationRules))
    .ToList();
```

**Caution**: Only parallelize CPU-bound operations (not IO-bound)

**Files to Modify**:
- `/Source/Reef/Core/Services/Import/ImportTransformationService.cs`
- `/Source/Reef/Core/Services/Import/ImportExecutionService.cs` (StageValidateDataAsync)

**Effort**: 4 hours
**Test**: Ensure no race conditions in transformation/validation

---

## 4. Performance Testing Plan

### 4.1 Load Test Scenarios

| Scenario | Size | Timeout | Target Time | Status |
|----------|------|---------|-------------|--------|
| Small import | 100 rows | 30s | <5s | TBD |
| Medium import | 10K rows | 2m | <30s | TBD |
| Large import | 100K rows | 5m | <2m | TBD |
| XL import | 1M rows | 10m | <8m | TBD |
| 50 concurrent jobs | 100 rows each | 5m | <3m | TBD |

### 4.2 Benchmark Combinations

Test all source/destination combinations:

```
Sources:      REST API, S3, FTP, Database
Destinations: Database (INSERT, UPSERT)
Sizes:        100, 1K, 10K, 100K rows
```

Total test matrix: 4 × 2 × 4 = 32 combinations

### 4.3 Memory Profiling

Use DotTrace or profiler.dotnet to measure:
- Peak memory during execution
- GC pressure (Gen 0/1/2 collections)
- Heap allocations per row

Target: <200MB for 100K rows (vs. current ~800MB)

### 4.4 Database Profiling

Profile SQLite/SQL Server to measure:
- Query execution time
- Lock contention
- Index utilization
- Transaction overhead

---

## 5. Implementation Timeline

### Week 11 (Phase 4.1 & 4.2)

| Day | Task | Effort | Status |
|-----|------|--------|--------|
| Mon | 3.1 Batch writes + tests | 4h | TBD |
| Tue | 3.2 Connection pooling + tests | 2h | TBD |
| Wed | 3.3 Profile caching + tests | 2h | TBD |
| Thu | 3.4 Streaming pipeline (Part 1) | 4h | TBD |
| Fri | 3.4 Streaming pipeline (Part 2) + tests | 4h | TBD |
| **Week 11 Total** | **16 hours** | | |

### Week 12 (Phase 4.3 & 4.4)

| Day | Task | Effort | Status |
|-----|------|--------|--------|
| Mon | 3.5 Delta sync optimization + tests | 3h | TBD |
| Tue | 3.6 Parallel processing + tests | 4h | TBD |
| Wed | Load testing (scenarios 1-3) | 4h | TBD |
| Thu | Load testing (scenarios 4-5) + memory profiling | 4h | TBD |
| Fri | Tuning & documentation | 4h | TBD |
| **Week 12 Total** | **19 hours** | | |

**Phase 4.1-4.3 Total**: 35 hours (Performance optimization complete)

---

## 6. Rollback Plan

If optimization breaks functionality:

1. **Git branch**: Create `optimize/performance` branch
2. **Incremental commits**: Commit each optimization separately
3. **Test after each**: Run full test suite after each change
4. **Easy rollback**: `git reset HEAD~1` if issues occur

Estimated safe fallback: Within 1 day if major issue found

---

## 7. Success Criteria

### Performance Targets (Phase 4.1-4.3)

- ✅ 100K rows imported in <2 minutes
- ✅ 50+ concurrent jobs without resource exhaustion
- ✅ Memory usage <200MB for 100K row operations
- ✅ Connection pool active with >10 reused connections
- ✅ Delta sync <5% of total execution time
- ✅ Throughput >500 rows/second on average

### Code Quality

- ✅ 85%+ unit test coverage maintained
- ✅ No compiler warnings
- ✅ Code review approved
- ✅ Performance metrics documented

### Documentation

- ✅ Performance tuning guide created
- ✅ Batch size recommendations documented
- ✅ Scaling guidelines documented
- ✅ Load test results published

---

## 8. Post-Phase 4 (Future Enhancements)

### 4.5 Advanced Optimizations (Q1 2025)

- Distributed imports across multiple workers
- GPU-accelerated hash calculations (for huge datasets)
- Incremental import pipelines
- Real-time streaming imports (Kafka, WebSocket)
- Advanced caching layer (Redis)

### 4.6 Monitoring & Observability

- Export performance metrics to Prometheus
- Create Grafana dashboards
- Set up alerts for slow imports
- Track performance trends over time

---

## 9. Appendix: Code Changes Summary

### Files to Modify (7 total)

1. **DatabaseWriter.cs** (Main optimization area)
   - Add batch write methods
   - Add connection pooling config
   - Refactor INSERT/UPSERT loops

2. **ImportExecutionService.cs** (Refactoring for reuse)
   - Load profile once
   - Pass to all stages
   - Implement streaming option

3. **ImportDeltaSyncService.cs** (Hash optimization)
   - Optimize hash calculation
   - Cache column list
   - Support key columns only

4. **ImportTransformationService.cs** (Parallel transforms)
   - Add parallel transformation option
   - Ensure thread safety

5. **RestDataSourceExecutor.cs** (Optional: async enumeration)
   - Add streaming variant
   - Yield rows as fetched

6. **Models/ImportProfile.cs** (Configuration)
   - Add DeltaKeyColumns
   - Add DeltaModifiedTimeColumn
   - Update validation

7. **Models/DatabaseWriterConfig.cs** (Pooling)
   - Add PoolSize
   - Add PoolingEnabled
   - Add BatchSize tuning

### No Breaking Changes

All optimizations are **additive**:
- Existing APIs remain unchanged
- Add new "optimized" methods alongside existing ones
- Feature flags for enable/disable
- Gradual rollout option

---

**Next Step**: Begin implementation of 3.1 (Batch Database Writes) on Day 1

