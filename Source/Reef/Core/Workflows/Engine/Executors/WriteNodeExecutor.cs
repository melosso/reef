using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.Targets;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class WriteNodeExecutor(ConnectionService connections, DatabaseImportTarget database, LocalFileImportTarget localFile) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["write"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var profile = ImportNodeProfile.From("target", context.Config, context.Workflow.Name);
        var isLocal = string.Equals(profile.TargetType, "LocalFile", StringComparison.OrdinalIgnoreCase);
        var connection = isLocal ? null : await connections.GetByIdAsync(profile.TargetConnectionId ?? 0)
            ?? throw new NodeExecutionException("Target connection not found");
        var format = profile.LocalTargetFormat ?? "CSV";
        var write = new ImportWriteContext
        {
            TargetType = profile.TargetType ?? "Database",
            TargetConnection = connection,
            TargetTable = profile.TargetTable ?? "",
            LoadStrategy = profile.LoadStrategy,
            UpsertKeyColumns = [.. (profile.UpsertKeyColumns ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            CommandTimeoutSeconds = profile.CommandTimeoutSeconds,
            BatchSize = profile.BatchSize,
            TargetFilePath = string.IsNullOrWhiteSpace(profile.LocalTargetPath) ? null
                : FileNames.Generate(profile.LocalTargetPath, context.Workflow.Name, format.ToLowerInvariant(), context.ExecutionId),
            TargetFormat = format,
            TargetWriteMode = profile.LocalTargetWriteMode ?? "Overwrite"
        };
        IImportTarget target = isLocal ? localFile : database;

        var input = context.Input;
        var results = new List<ImportBatchResult>();
        if (write.LoadStrategy.Equals("FullReplace", StringComparison.OrdinalIgnoreCase))
            results.Add(await target.FullReplaceAsync(input.Items, write, ct));
        else
            foreach (var batch in input.Items.Chunk(Math.Max(1, write.BatchSize)))
                results.Add(await target.WriteBatchAsync(batch, write, ct));

        var failed = results.Sum(r => r.RowsFailed);
        var firstError = results.SelectMany(r => r.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
        return new NodeResult
        {
            Outputs = { [WorkflowPorts.Main] = input },
            Warning = failed == 0 ? null : $"{failed} rows were not written, first: {firstError}"
        };
    }
}
