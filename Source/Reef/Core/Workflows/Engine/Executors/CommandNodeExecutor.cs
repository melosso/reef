using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Models;
using Reef.Core.Scripting;
using Reef.Core.Services;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class CommandNodeExecutor(ConnectionService connections, QueryExecutor queries, IScriptRunner scripts) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["command"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var settings = context.Config;
        var config = new ProcessingConfig
        {
            Type = NodeConfig.Text(settings, "Runs"),
            Command = NodeConfig.Text(settings, "Command"),
            Timeout = int.TryParse(NodeConfig.Text(settings, "TimeoutSeconds"), out var timeout) && timeout > 0 ? timeout : 30,
            Interpreter = NodeConfig.Text(settings, "Interpreter"),
            ScriptPathOrInline = NodeConfig.Text(settings, "Script"),
            ScriptIsInline = settings["InlineScript"]?.GetValueKind() != JsonValueKind.False,
            EnvAllowlist = (settings["EnvironmentVariables"] as JsonArray)?.Select(v => v?.ToString() ?? "").Where(v => v.Length > 0).ToList()
        };

        var input = context.Input;
        var processing = new ProcessingContext
        {
            ExecutionId = context.ExecutionId,
            RowCount = input.Items.Count,
            OutputPath = input.Files.LastOrDefault()?.Path,
            FileSizeBytes = input.Files.LastOrDefault()?.Size
        };

        var error = config.Type == "Script" ? await RunScriptAsync(config, processing, ct) : await RunCommandAsync(context, config, processing);
        return error is null ? NodeResult.Main(input) : throw new NodeExecutionException(error);
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
