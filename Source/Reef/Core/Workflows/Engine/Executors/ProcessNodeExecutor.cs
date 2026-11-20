using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Models;
using Reef.Core.Scripting;
using Reef.Core.Services;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class ProcessNodeExecutor(ConnectionService connections, QueryExecutor queries, IScriptRunner scripts) : INodeExecutor
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public IReadOnlyCollection<string> Types { get; } = ["preprocess", "postprocess"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var prefix = context.Node.Type == "preprocess" ? "PreProcess" : "PostProcess";
        var settings = (context.Config[$"{prefix}Config"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
        settings["Type"] = NodeConfig.Text(context.Config, $"{prefix}Type");
        var config = settings.Deserialize<ProcessingConfig>(Json) ?? throw new NodeExecutionException("Processing settings are missing");

        var input = context.Input;
        var processing = new ProcessingContext
        {
            ExecutionId = context.ExecutionId,
            RowCount = input.Items.Count,
            OutputPath = input.Files.LastOrDefault()?.Path,
            FileSizeBytes = input.Files.LastOrDefault()?.Size
        };

        var error = config.Type.Equals("Script", StringComparison.OrdinalIgnoreCase)
            ? await RunScriptAsync(config, processing, ct)
            : await RunCommandAsync(context, config, processing);
        if (error is null)
            return NodeResult.Main(input);
        if (!config.ContinueOnError)
            throw new NodeExecutionException(error);
        return new NodeResult { Outputs = { [WorkflowPorts.Main] = input }, Warning = error };
    }

    private async Task<string?> RunCommandAsync(NodeContext context, ProcessingConfig config, ProcessingContext processing)
    {
        var connection = await connections.GetByIdAsync(NodeConfig.Id(context.Config, "ConnectionId"))
            ?? throw new NodeExecutionException("Connection not found");
        var (command, parameters) = ProcessingCommandBuilder.Build(connection.Type, config, processing);
        var (success, _, error, _) = await queries.ExecuteCommandAsync(connection, command, parameters, config.Timeout);
        return success ? null : error ?? "Command failed";
    }

    private async Task<string?> RunScriptAsync(ProcessingConfig config, ProcessingContext processing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Interpreter) || string.IsNullOrWhiteSpace(config.ScriptPathOrInline))
            return "Script needs an interpreter and a script";
        var result = await scripts.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = config.Interpreter,
            ScriptPathOrInline = config.ScriptPathOrInline,
            IsInline = config.ScriptIsInline,
            StdinJson = JsonSerializer.Serialize(new { processing.ExecutionId, processing.RowCount, processing.OutputPath, processing.FileSizeBytes }),
            TimeoutSeconds = config.Timeout,
            EnvAllowlist = config.EnvAllowlist
        }, ct);
        return result.Success ? null : result.ErrorMessage ?? $"Script exited with code {result.ExitCode}";
    }
}
