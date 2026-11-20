using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Services;
using Reef.Core.Workflows.Engine.Executors;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Conversion;

public class WorkflowConversionService(ProfileService profiles, ImportProfileService imports, WorkflowService workflows, DatabaseConfig config)
{
    public async Task<WorkflowConversion> ConvertExportAsync(int profileId, int? userId, CancellationToken ct = default)
    {
        var profile = await profiles.GetByIdAsync(profileId, ct) ?? throw new KeyNotFoundException("Profile not found");
        var conversion = ProfileWorkflowConverter.FromExport(profile);
        var saved = await SaveAsync(conversion.Workflow, userId, ct);
        if (profile.DeltaSyncEnabled)
            await CopySmartSyncHistoryAsync(profileId, DeltaSyncNodeExecutor.Scope(saved.Id!.Value, "deltasync"), ct);
        return conversion with { Workflow = saved };
    }

    public async Task<WorkflowConversion> ConvertImportAsync(int importProfileId, int? userId, CancellationToken ct = default)
    {
        var profile = await imports.GetByIdAsync(importProfileId) ?? throw new KeyNotFoundException("Import profile not found");
        var conversion = ProfileWorkflowConverter.FromImport(profile, await imports.GetOutputTargetsAsync(importProfileId));
        return conversion with { Workflow = await SaveAsync(conversion.Workflow, userId, ct) };
    }

    private async Task<WorkflowDocument> SaveAsync(WorkflowDocument workflow, int? userId, CancellationToken ct)
    {
        var taken = (await workflows.ListAsync(ct)).Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = workflow.Name;
        for (var copy = 2; taken.Contains(name); copy++)
            name = $"{workflow.Name} ({copy})";
        workflow.Name = name;
        return await workflows.SaveAsync(null, workflow, userId, ct);
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
