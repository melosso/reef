using System.Text.Json.Nodes;
using Dapper;
using Reef.Core.Models;
using Reef.Core.Workflows.Catalogs;
using Microsoft.Data.Sqlite;
using Reef.Core.Services;
using Reef.Core.Workflows.Engine.Executors;
using Reef.Core.Workflows.Models;
using Reef.Core.Workflows;

namespace Reef.Legacy.Conversion;

public class WorkflowConversionService(LegacyProfileReader legacy, WorkflowService workflows, DatabaseConfig config)
{
    public async Task<WorkflowConversion> ConvertExportAsync(int profileId, int? userId, CancellationToken ct = default)
    {
        var profile = (await legacy.ExportsAsync(profileId, ct)).FirstOrDefault() ?? throw new KeyNotFoundException("Profile not found");
        var conversion = ProfileWorkflowConverter.FromExport(profile);
        conversion.Workflow.Active = profile.IsEnabled;
        var hashes = await AddTriggersAsync(conversion, await legacy.JobsAsync("ProfileId", profileId, ct), "ProfileId", profileId);
        var saved = await SaveAsync(conversion.Workflow, userId, ct, hashes);
        if (profile.DeltaSyncEnabled)
            await CopySmartSyncHistoryAsync(profileId, DeltaSyncNodeExecutor.Scope(saved.Id!.Value, "deltasync"), ct);
        return conversion with { Workflow = saved };
    }

    public async Task<WorkflowConversion> ConvertImportAsync(int importProfileId, int? userId, CancellationToken ct = default)
    {
        var profile = (await legacy.ImportsAsync(importProfileId, ct)).FirstOrDefault() ?? throw new KeyNotFoundException("Import profile not found");
        var conversion = ProfileWorkflowConverter.FromImport(profile, await legacy.OutputTargetsAsync(importProfileId, ct));
        conversion.Workflow.Active = profile.IsEnabled;
        var hashes = await AddTriggersAsync(conversion, await legacy.JobsAsync("ImportProfileId", importProfileId, ct), "ImportProfileId", importProfileId);
        return conversion with { Workflow = await SaveAsync(conversion.Workflow, userId, ct, hashes) };
    }

    // Old webhook URLs keep working
    private async Task<Dictionary<string, string>> AddTriggersAsync(WorkflowConversion conversion, IEnumerable<Job> profileJobs, string column, int id)
    {
        var workflow = conversion.Workflow;
        var first = workflow.Edges.FirstOrDefault(e => e.From == WorkflowChain.Start)?.To;
        var y = 160.0;
        void Add(string nodeId, string type, JsonObject config)
        {
            workflow.Nodes.Add(new WorkflowNode { Id = nodeId, Type = type, X = 0, Y = y += 160, Config = config });
            if (first is not null)
                workflow.Edges.Add(new WorkflowEdge { From = nodeId, To = first });
        }

        var schedules = 0;
        foreach (var job in profileJobs)
        {
            if (JobSchedule.ToTrigger(job) is { } trigger)
                Add($"schedule{++schedules}", "schedule", trigger);
            else
                conversion.Notes.Add($"Job {job.Name} has a schedule without a workflow trigger, add a schedule step");
            if (!job.IsEnabled)
                conversion.Notes.Add($"Job {job.Name} was off, its schedule step starts runs once the workflow is on");
        }

        await using var db = new SqliteConnection(config.ConnectionString);
        var tokens = (await db.QueryAsync<string>($"SELECT Token FROM WebhookTriggers WHERE {column} = @id AND IsActive = 1", new { id })).ToList();
        var hashes = new Dictionary<string, string>();
        foreach (var token in tokens)
        {
            var nodeId = $"webhook{hashes.Count + 1}";
            Add(nodeId, WorkflowNodeCatalog.Webhook, new JsonObject());
            hashes[nodeId] = token;
        }
        return hashes;
    }

    private async Task<WorkflowDocument> SaveAsync(WorkflowDocument workflow, int? userId, CancellationToken ct, Dictionary<string, string> hashes)
    {
        var taken = (await workflows.ListAsync(ct)).Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = workflow.Name;
        for (var copy = 2; taken.Contains(name); copy++)
            name = $"{workflow.Name} ({copy})";
        workflow.Name = name;
        var saved = await workflows.SaveAsync(null, workflow, userId, ct);
        foreach (var (nodeId, hash) in hashes)
            await workflows.SetWebhookTokenHashAsync(saved.Id!.Value, nodeId, hash, ct);
        return hashes.Count == 0 ? saved : await workflows.GetAsync(saved.Id!.Value, ct) ?? saved;
    }

    private async Task CopySmartSyncHistoryAsync(int profileId, string scope, CancellationToken ct)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        await db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO DeltaSyncState (ProfileId, Scope, ReefId, RowHash, LastSeenExecutionId, FirstSeenAt, LastSeenAt, IsDeleted, DeletedAt)
            SELECT NULL, @scope, ReefId, RowHash, NULL, FirstSeenAt, LastSeenAt, IsDeleted, DeletedAt FROM DeltaSyncState WHERE ProfileId = @profileId AND Scope IS NULL
            """,
            new { profileId, scope }, cancellationToken: ct));
    }
}
