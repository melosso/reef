using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine;

public interface INodeExecutor
{
    IReadOnlyCollection<string> Types { get; }

    Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct);
}
