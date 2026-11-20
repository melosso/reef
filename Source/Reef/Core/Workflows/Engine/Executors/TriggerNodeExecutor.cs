using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class TriggerNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["manual", "schedule", "webhook"];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct) => Task.FromResult(NodeResult.Main(context.Input));
}
