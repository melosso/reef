using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Tests.Services;

public class JobRecoveryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("reef-jobs").FullName;

    private string ConnectionString => $"Data Source={Path.Combine(_folder, "Reef.db")};Pooling=False";

    [Fact]
    public async Task InterruptedRunsAreResetOnStartup()
    {
        await new Reef.Core.Data.DatabaseInitializer(ConnectionString).InitializeAsync();
        await using (var db = new SqliteConnection(ConnectionString))
        {
            await db.ExecuteAsync("""
                INSERT INTO Jobs (Name, Type, ScheduleType, IntervalMinutes, Status, NextRunTime, CreatedAt, Hash)
                VALUES ('nightly', 1, 2, 60, @Running, @Next, @Now, '');
                INSERT INTO JobExecutions (JobId, StartedAt, Status) VALUES (last_insert_rowid(), @Now, @Running);
                """, new { Running = JobStatus.Running, Next = DateTime.UtcNow.AddHours(1), Now = DateTime.UtcNow });
        }

        await new JobService(new DatabaseConfig { ConnectionString = ConnectionString }, new ConfigurationBuilder().Build()).ResetInterruptedRunsAsync();

        await using var check = new SqliteConnection(ConnectionString);
        (await check.ExecuteScalarAsync<int>("SELECT Status FROM Jobs")).Should().Be((int)JobStatus.Idle);
        var execution = await check.QuerySingleAsync<(int Status, string? CompletedAt, string? ErrorMessage)>("SELECT Status, CompletedAt, ErrorMessage FROM JobExecutions");
        execution.Status.Should().Be((int)JobStatus.Failed);
        execution.CompletedAt.Should().NotBeNull();
        execution.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}
