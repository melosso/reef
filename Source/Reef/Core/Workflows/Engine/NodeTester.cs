using System.Text.Json.Nodes;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine;

public class NodeTester(WorkflowService workflows, NodeInvoker invoker)
{
    private static readonly string[] FullRunOnly = [WorkflowNodeGroups.Deliver, WorkflowNodeGroups.Hooks];

    // Run commits are skipped so a test leaves no state
    public async Task<NodeResult> TestAsync(int workflowId, string nodeId, WorkflowItems input, CancellationToken ct)
    {
        var workflow = await workflows.GetAsync(workflowId, ct) ?? throw new KeyNotFoundException("Workflow not found");
        var node = workflow.Nodes.FirstOrDefault(n => n.Id == nodeId) ?? throw new KeyNotFoundException($"Step '{nodeId}' not found");
        var definition = WorkflowNodeCatalog.Catalog.Find(node.Type);
        if (definition is null || FullRunOnly.Contains(definition.Group))
            throw new InvalidOperationException($"{definition?.Label ?? node.Type} sends or changes data and runs only in a full run");

        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Reef.Helpers.TempPaths.WorkflowRoot, $"test-{Guid.NewGuid():N}")).FullName;
        try
        {
            return await invoker.InvokeAsync(new NodeContext
            {
                Workflow = workflow, Node = node, ExecutionId = 0, WorkFolder = folder, Config = node.Config ?? new JsonObject(),
                Inputs = new() { [WorkflowPorts.Main] = input },
                Scope = new ExpressionScope(workflow, 0, new Dictionary<string, Dictionary<string, WorkflowItems>>(), 0)
            }, ct);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
