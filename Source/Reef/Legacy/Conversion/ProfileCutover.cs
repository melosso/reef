using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Reef.Legacy.Conversion;

// One time profile to workflow cutover
public class ProfileCutover(LegacyProfileReader legacy, WorkflowConversionService conversions, DatabaseConfig config)
{
    private static readonly string[] LegacyTables =
    [
        "ProfileExecutionSplits", "ProfileExecutionErrors", "ProfileParameters", "ProfileTransformations", "ProfileDependencies",
        "ScheduledTasks", "ImportExecutionErrors", "ImportDeltaSyncState", "ImportOutputTargets", "ImportProfileExecutions",
        "ProfileExecutions", "ImportProfiles", "Profiles", "ProfileGroups"
    ];

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var hasExports = await legacy.ExistsAsync("Profiles", ct);
        var hasImports = await legacy.ExistsAsync("ImportProfiles", ct);
        if (!hasExports && !hasImports)
            return 0;

        var exports = hasExports ? await legacy.ExportsAsync(ct: ct) : [];
        var imports = hasImports ? await legacy.ImportsAsync(ct: ct) : [];
        if (exports.Count + imports.Count > 0)
            await BackupAsync(ct);

        var converted = 0;
        foreach (var (kind, id, name) in exports.Select(p => ("export", p.Id, p.Name)).Concat(imports.Select(p => ("import", p.Id, p.Name))))
        {
            try
            {
                var conversion = kind == "import" ? await conversions.ConvertImportAsync(id, null, ct) : await conversions.ConvertExportAsync(id, null, ct);
                converted++;
                Log.Information("Profile {Profile} is now workflow {Workflow}", name, conversion.Workflow.Name);
                foreach (var note in conversion.Notes)
                    Log.Warning("Workflow {Workflow}: {Note}", conversion.Workflow.Name, note);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "Profile {Profile} could not be converted, it stays in the backup", name);
            }
        }

        await DropAsync(hasExports, ct);
        Log.Information("Converted {Converted} of {Total} profiles to workflows", converted, exports.Count + imports.Count);
        return converted;
    }

    private async Task BackupAsync(CancellationToken ct)
    {
        var source = new SqliteConnectionStringBuilder(config.ConnectionString).DataSource;
        var target = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)) ?? ".", $"{Path.GetFileNameWithoutExtension(source)}.pre-workflows-{DateTime.UtcNow:yyyyMMddHHmmssfff}.db");
        await using var db = new SqliteConnection(config.ConnectionString);
        await db.ExecuteAsync(new CommandDefinition("VACUUM INTO @target", new { target }, cancellationToken: ct));
        Log.Information("Backed up the database to {Backup} before the workflow conversion", target);
    }

    // Approvals keep roles and mail server
    private async Task DropAsync(bool hasExports, CancellationToken ct)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        await db.OpenAsync(ct);
        await db.ExecuteAsync("PRAGMA foreign_keys = OFF");
        try
        {
            await using var tx = db.BeginTransaction();
            if (hasExports)
                await db.ExecuteAsync("""
                    UPDATE PendingEmailApprovals SET
                        ApproverRoles = COALESCE(ApproverRoles, (SELECT p.EmailApprovalRoles FROM Profiles p WHERE p.Id = PendingEmailApprovals.ProfileId)),
                        DestinationId = COALESCE(DestinationId, (SELECT p.OutputDestinationId FROM Profiles p WHERE p.Id = PendingEmailApprovals.ProfileId))
                    WHERE ProfileId IS NOT NULL
                    """, transaction: tx);
            await db.ExecuteAsync("""
                UPDATE PendingEmailApprovals SET ProfileId = NULL, ProfileExecutionId = NULL WHERE ProfileId IS NOT NULL OR ProfileExecutionId IS NOT NULL;
                DELETE FROM DeltaSyncState WHERE ProfileId IS NOT NULL;
                DELETE FROM JobExecutions WHERE JobId IN (SELECT Id FROM Jobs WHERE ProfileId IS NOT NULL OR ImportProfileId IS NOT NULL);
                DELETE FROM JobDependencies WHERE JobId IN (SELECT Id FROM Jobs WHERE ProfileId IS NOT NULL OR ImportProfileId IS NOT NULL)
                    OR DependsOnJobId IN (SELECT Id FROM Jobs WHERE ProfileId IS NOT NULL OR ImportProfileId IS NOT NULL);
                DELETE FROM Jobs WHERE ProfileId IS NOT NULL OR ImportProfileId IS NOT NULL;
                DELETE FROM WebhookTriggers WHERE ProfileId IS NOT NULL OR ImportProfileId IS NOT NULL;
                """, transaction: tx);
            foreach (var table in LegacyTables)
                await db.ExecuteAsync($"DROP TABLE IF EXISTS \"{table}\"", transaction: tx);
            await tx.CommitAsync(ct);
        }
        finally
        {
            await db.ExecuteAsync("PRAGMA foreign_keys = ON");
        }
        await Reef.Core.Database.MissingReferences.RemoveAsync(db);
    }
}
