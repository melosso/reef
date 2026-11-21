using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine;

public class NodeInvoker(IEnumerable<INodeExecutor> executors, WorkflowSecrets secrets)
{
    private readonly Dictionary<string, INodeExecutor> _executors = executors.SelectMany(e => e.Types.Select(t => (t, e))).ToDictionary(p => p.t, p => p.e);

    public async Task<NodeResult> InvokeAsync(NodeContext context, CancellationToken ct)
    {
        if (!_executors.TryGetValue(context.Node.Type, out var executor) || WorkflowNodeCatalog.Catalog.Find(context.Node.Type) is not { } definition)
            throw new NodeExecutionException($"Node type '{context.Node.Type}' cannot run yet");

        var input = context.Input;
        if (!NodeConfig.Flag(context.Config, "RunPerRow") || input.Items.Count == 0)
            return await executor.ExecuteAsync(Resolve(context, definition, context.Scope with { Item = input.Items.FirstOrDefault() }), ct);

        var results = new List<NodeResult>();
        for (var i = 0; i < input.Items.Count; i++)
        {
            var inputs = new Dictionary<string, WorkflowItems>(context.Inputs) { [WorkflowPorts.Main] = input.WithRows([input.Items[i]]) };
            results.Add(await executor.ExecuteAsync(Resolve(context with { Inputs = inputs }, definition, context.Scope with { Item = input.Items[i], ItemIndex = i }), ct));
        }
        return new NodeResult
        {
            Outputs = results.SelectMany(r => r.Outputs).GroupBy(o => o.Key).ToDictionary(g => g.Key, g => WorkflowItems.Combine(g.Select(o => o.Value))),
            Warning = string.Join(" ", results.Select(r => r.Warning).OfType<string>()) is { Length: > 0 } warning ? warning : null,
            OnRunSuccess = Chain(results.Select(r => r.OnRunSuccess)),
            OnRunFailure = Chain(results.Select(r => r.OnRunFailure).Reverse())
        };
    }

    private NodeContext Resolve(NodeContext context, WorkflowNodeDefinition definition, ExpressionScope scope) =>
        context with { Scope = scope, Config = WorkflowExpressions.Resolve(definition, secrets.Reveal(definition, context.Config), scope) };

    private static Func<Task>? Chain(IEnumerable<Func<Task>?> steps)
    {
        var list = steps.OfType<Func<Task>>().ToList();
        return list.Count == 0 ? null : async () =>
        {
            foreach (var step in list)
                await step();
        };
    }
}
