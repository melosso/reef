using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class CleanupJobApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task CleanupJobDeletesExecutionsPastRetention()
    {
        var client = await factory.CreateAdminClientAsync();
        var created = await client.PostAsJsonAsync("/api/jobs", new { name = $"Cleanup {Guid.NewGuid():N}", type = "Cleanup", scheduleType = "Manual", isEnabled = true }, _ct);
        var body = await created.Content.ReadAsStringAsync(_ct);
        created.IsSuccessStatusCode.Should().BeTrue(body);
        var jobId = JsonNode.Parse(body)!["id"]!.GetValue<string>();

        await using var db = new SqliteConnection($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")};Pooling=False");
        var job = await db.ExecuteScalarAsync<long>("SELECT Id FROM Jobs WHERE Ulid = @jobId", new { jobId });
        var old = DateTime.UtcNow.AddDays(-100);
        var recent = DateTime.UtcNow.AddDays(-10);
        await db.ExecuteAsync("INSERT INTO JobExecutions (JobId, StartedAt, CompletedAt, Status, ErrorMessage) VALUES (@job, @old, @old, 3, 'old'), (@job, @recent, @recent, 3, 'recent')", new { job, old, recent });

        (await client.PostAsJsonAsync($"/api/jobs/{jobId}/trigger", new { }, _ct)).EnsureSuccessStatusCode();

        var status = "Running";
        for (var i = 0; i < 60 && status == "Running"; i++)
        {
            await Task.Delay(500, _ct);
            status = (await client.GetFromJsonAsync<JsonObject>($"/api/jobs/{jobId}/executions/latest", _ct))!["status"]!.GetValue<string>();
        }
        status.Should().Be("Completed");
        var left = (await db.QueryAsync<string?>("SELECT ErrorMessage FROM JobExecutions WHERE JobId = @job AND ErrorMessage IS NOT NULL", new { job })).ToList();
        left.Should().Equal("recent");
    }
}
