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
        var config = context.Config;
        var isLocal = NodeConfig.Text(config, "To") == "LocalFile";
        var connection = isLocal ? null : await connections.GetByIdAsync(NodeConfig.Id(config, "ConnectionId"))
            ?? throw new NodeExecutionException("Target connection not found");
        var format = NodeConfig.Text(config, "FileFormat") is { Length: > 0 } fileFormat ? fileFormat : "CSV";
        var filePath = NodeConfig.Text(config, "FilePath");
        var write = new ImportWriteContext
        {
            TargetType = isLocal ? "LocalFile" : "Database",
            TargetConnection = connection,
            TargetTable = NodeConfig.Text(config, "Table"),
            LoadStrategy = NodeConfig.Text(config, "Load") is { Length: > 0 } load ? load : "Upsert",
            UpsertKeyColumns = [.. NodeConfig.Text(config, "KeyColumns").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            CommandTimeoutSeconds = Number(config, "TimeoutSeconds", 120),
            BatchSize = Number(config, "BatchSize", 500),
            TargetFilePath = filePath.Length == 0 ? null : FileNames.Generate(filePath, context.Workflow.Name, format.ToLowerInvariant(), context.ExecutionId),
            TargetFormat = format,
            TargetWriteMode = NodeConfig.Text(config, "ExistingFile") is { Length: > 0 } mode ? mode : "Overwrite"
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

    private static int Number(System.Text.Json.Nodes.JsonObject config, string key, int fallback) =>
        int.TryParse(NodeConfig.Text(config, key), out var value) && value > 0 ? value : fallback;
}
