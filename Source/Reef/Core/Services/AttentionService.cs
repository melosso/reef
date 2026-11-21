using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Models.Dashboard;

namespace Reef.Core.Services;

/// <summary>
/// Finds the workflows and jobs that need a person, for the home page
/// </summary>
public class AttentionService(DatabaseConfig config)
{
    private const int Baseline = 10;
    private const int MinimumBaseline = 3;

    // shortcut ranks every run per workflow, add a StartedAt window past a few hundred thousand runs
    private const string RunsSql = """
        WITH runs AS (
            SELECT e.WorkflowId, w.Name AS WorkflowName, w.DocumentJson LIKE '%"type":"deltasync"%' AS DeltaSyncEnabled, e.Id AS ExecutionId, e.Status, e.StartedAt,
                   e.ErrorMessage, (SELECT MAX(RowCount) FROM NodeRuns n WHERE n.WorkflowExecutionId = e.Id) AS Rows,
                   CAST((julianday(e.CompletedAt) - julianday(e.StartedAt)) * 86400000 AS INTEGER) AS DurationMs
            FROM WorkflowExecutions e JOIN Workflows w ON w.Id = e.WorkflowId
            WHERE e.Status <> 'Running'
        ), ranked AS (
            SELECT *,
                   ROW_NUMBER() OVER (PARTITION BY WorkflowId ORDER BY ExecutionId DESC) AS Recent,
                   ROW_NUMBER() OVER (PARTITION BY WorkflowId, Status = 'Success' ORDER BY ExecutionId DESC) AS SuccessRank
            FROM runs
        )
        SELECT * FROM ranked WHERE Recent = 1 OR (Status = 'Success' AND SuccessRank <= @Window)
        ORDER BY WorkflowId, ExecutionId DESC
        """;

    private const string TotalsSql = """
        SELECT COUNT(*) AS Runs, COALESCE(SUM(Status = 'Failed'), 0) AS Failed FROM WorkflowExecutions
        WHERE julianday(StartedAt) >= julianday('now', '-1 day')
        """;

    private const string UpNextSql = """
        SELECT Id, Name, NextRunTime FROM Jobs
        WHERE IsEnabled = 1 AND NextRunTime IS NOT NULL AND julianday(NextRunTime) <= julianday('now', '+1 day')
        ORDER BY julianday(NextRunTime)
        LIMIT 20
        """;

    public async Task<DashboardAttention> GetAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(config.ConnectionString);
        await conn.OpenAsync(ct);

        var runs = await conn.QueryAsync<AttentionRun>(new CommandDefinition(RunsSql, new { Window = Baseline + 1 }, cancellationToken: ct));
        var totals = await conn.QuerySingleAsync<AttentionTotals>(new CommandDefinition(TotalsSql, cancellationToken: ct));
        var upNext = await conn.QueryAsync<AttentionJob>(new CommandDefinition(UpNextSql, cancellationToken: ct));

        var failed = new List<AttentionFailure>();
        var anomalies = new List<AttentionAnomaly>();
        foreach (var workflow in runs.GroupBy(r => r.WorkflowId))
        {
            var latest = workflow.First(r => r.Recent == 1);
            if (latest.Status == "Failed")
            {
                failed.Add(new AttentionFailure(latest.WorkflowId, latest.WorkflowName, latest.ExecutionId,
                    FirstLine(latest.ErrorMessage), latest.StartedAt));
            }
            else if (latest.Status == "Success" && FindAnomaly(latest, workflow.Where(r => r.Status == "Success" && r.ExecutionId != latest.ExecutionId).ToList()) is { } anomaly)
            {
                anomalies.Add(anomaly);
            }
        }

        return new DashboardAttention(
            failed.OrderByDescending(f => f.StartedAt).ToList(),
            anomalies.OrderByDescending(a => a.StartedAt).ToList(),
            upNext.ToList(),
            totals);
    }

    /// <summary>
    /// Flags a run with under 10 percent of the usual rows, or more than twice the usual duration and at least a second slower
    /// </summary>
    public static AttentionAnomaly? FindAnomaly(AttentionRun latest, IReadOnlyList<AttentionRun> previous)
    {
        if (previous.Count < MinimumBaseline) return null;

        // delta sync returns no rows when nothing changed, so an empty run is normal there
        if (!latest.DeltaSyncEnabled && latest.Rows is { } rows && Median(previous.Select(r => r.Rows)) is { } usualRows && usualRows > 0 && rows * 10 < usualRows)
            return Anomaly(latest, "rows", rows, usualRows);

        if (latest.DurationMs is { } ms && Median(previous.Select(r => r.DurationMs)) is { } usualMs && ms > usualMs * 2 && ms - usualMs >= 1000)
            return Anomaly(latest, "duration", ms, usualMs);

        return null;
    }

    private static AttentionAnomaly Anomaly(AttentionRun run, string reason, long value, long median) =>
        new(run.WorkflowId, run.WorkflowName, run.ExecutionId, reason, value, median, run.StartedAt);

    private static long? Median(IEnumerable<long?> values)
    {
        var sorted = values.OfType<long>().Order().ToList();
        return sorted.Count < MinimumBaseline ? null : sorted[sorted.Count / 2];
    }

    private static string FirstLine(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "Run failed" : message.Split('\n', 2)[0].Trim();
}
