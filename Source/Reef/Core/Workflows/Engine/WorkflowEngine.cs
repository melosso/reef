using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;
using Serilog;

namespace Reef.Core.Workflows.Engine;

public class WorkflowEngine(WorkflowService workflows, WorkflowRunStore runs, NodeInvoker invoker, Reef.Core.Services.NotificationService notifications)
{
    private const int SampleCount = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WorkflowExecution> RunAsync(int workflowId, string? triggerNodeId, WorkflowItems? triggerItems = null, CancellationToken ct = default)
    {
        var workflow = await workflows.GetAsync(workflowId, ct) ?? throw new KeyNotFoundException("Workflow not found");
        var trigger = workflow.Nodes.FirstOrDefault(n => triggerNodeId is null ? n.Type == "manual" : n.Id == triggerNodeId)
            ?? throw new KeyNotFoundException(triggerNodeId is null ? "Workflow has no manual trigger" : $"Trigger '{triggerNodeId}' not found");
        if (WorkflowNodeCatalog.Catalog.Find(trigger.Type)?.Group != WorkflowNodeGroups.Triggers)
            throw new InvalidOperationException($"Node '{trigger.Id}' is not a trigger");

        var run = new WorkflowExecution { WorkflowId = workflowId, WorkflowVersion = workflow.Version, TriggerNodeId = trigger.Id, StartedAt = DateTime.UtcNow };
        run.Id = await runs.StartAsync(run, ct);

        var graph = new WorkflowGraph(workflow, trigger.Id);
        var pending = new LinkedList<PendingNode>([new PendingNode(trigger, new() { [WorkflowPorts.Main] = triggerItems ?? new WorkflowItems() })]);
        var waiting = new List<PendingNode>();
        var latest = new Dictionary<string, Dictionary<string, WorkflowItems>>();
        var states = new Dictionary<string, Dictionary<string, object>>();
        var passes = new Dictionary<string, int>();
        var commits = new List<(string NodeId, Func<Task> Commit)>();
        string? failedNode = null;
        var undos = new List<Func<Task>>();
        var workFolder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Reef.Helpers.TempPaths.WorkflowRoot, run.Id.ToString())).FullName;
        try
        {
            while (Next(pending, waiting) is { } step)
            {
                var (node, inputs) = step;
                var pass = passes.GetValueOrDefault(node.Id);
                if (pass >= workflow.MaxPasses)
                {
                    run.Status = WorkflowRunStatus.Failed;
                    failedNode = node.Id;
                    run.ErrorMessage = $"{node.Name ?? node.Id}: stopped after {workflow.MaxPasses} passes, the pass limit of this workflow";
                    break;
                }
                passes[node.Id] = pass + 1;

                var record = new NodeRun { WorkflowExecutionId = run.Id, NodeId = node.Id, Iteration = pass, StartedAt = DateTime.UtcNow };
                if (node.Id != trigger.Id && inputs.Values.All(i => i.IsEmpty) && SkipsWhenEmpty(node))
                {
                    await SkipAsync(run, record);
                    var empty = inputs.Values.First();
                    latest[node.Id] = (WorkflowNodeCatalog.Catalog.Find(node.Type)?.OutputPorts(node.Config ?? new JsonObject()) ?? WorkflowPorts.Single).ToDictionary(p => p, _ => empty);
                    Push(pending, Deliver(graph, node.Id, latest[node.Id], waiting));
                    continue;
                }

                var context = new NodeContext
                {
                    Workflow = workflow, Node = node, ExecutionId = run.Id, Inputs = inputs, WorkFolder = workFolder, Config = node.Config ?? new JsonObject(),
                    Scope = new ExpressionScope(workflow, run.Id, latest, pass),
                    State = states.TryGetValue(node.Id, out var state) ? state : states[node.Id] = new()
                };
                Dictionary<string, WorkflowItems> outputs;
                try
                {
                    var result = await invoker.InvokeAsync(context, ct);
                    outputs = result.Outputs;
                    record.Status = WorkflowRunStatus.Success;
                    record.ErrorMessage = result.Warning;
                    if (result.OnRunSuccess is { } commit)
                        commits.Add((node.Id, commit));
                    if (result.OnRunFailure is { } undo)
                        undos.Insert(0, undo);
                    record.RowCount = outputs.Values.Sum(o => o.Items.Count);
                    record.OutputSampleJson = JsonSerializer.Serialize(outputs.ToDictionary(o => o.Key, o => new
                    {
                        o.Value.Columns,
                        Items = SampleRows.Take(o.Value.Items, SampleCount),
                        o.Value.Files,
                        Total = o.Value.Items.Count
                    }), Json);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Log.Warning(ex, "Workflow {WorkflowId} node {NodeId} failed", workflowId, node.Id);
                    record.ErrorMessage = ex is NodeExecutionException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                    outputs = new() { [WorkflowPorts.Main] = inputs.Values.First() };
                    if (NodeConfig.Flag(context.Config, "ContinueOnError"))
                        record.Status = WorkflowRunStatus.Success;
                    else
                    {
                        record.Status = WorkflowRunStatus.Failed;
                        run.Status = WorkflowRunStatus.Failed;
                        failedNode = node.Id;
                        run.ErrorMessage = $"{node.Name ?? WorkflowNodeCatalog.Catalog.Find(node.Type)?.Label ?? node.Id}: {record.ErrorMessage}";
                    }
                }
                finally
                {
                    record.CompletedAt = DateTime.UtcNow;
                    run.Nodes.Add(record);
                    await runs.SaveNodeAsync(record);
                }
                if (run.Status == WorkflowRunStatus.Failed)
                    break;
                latest[node.Id] = outputs;
                Push(pending, Deliver(graph, node.Id, outputs, waiting));
            }
        }
        finally
        {
            Directory.Delete(workFolder, recursive: true);
        }

        if (run.Status != WorkflowRunStatus.Failed)
        {
            foreach (var node in workflow.Nodes.Where(n => graph.Reachable.Contains(n.Id) && !passes.ContainsKey(n.Id)))
                await SkipAsync(run, new NodeRun { WorkflowExecutionId = run.Id, NodeId = node.Id, StartedAt = DateTime.UtcNow });
            run.Status = WorkflowRunStatus.Success;
        }
        try
        {
            foreach (var (_, commit) in commits.Where(c => failedNode is null || !graph.Reaches(c.NodeId, failedNode)))
                await commit();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Workflow {WorkflowId} could not save its run state", workflowId);
            run.ErrorMessage = run.Status == WorkflowRunStatus.Failed
                ? $"{run.ErrorMessage} Smart Sync state was not saved: {ex.Message}"
                : $"Output was written but the Smart Sync state was not saved: {ex.Message}";
            run.Status = WorkflowRunStatus.Failed;
        }
        if (run.Status == WorkflowRunStatus.Failed)
        {
            foreach (var undo in undos)
            {
                try
                {
                    await undo();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Workflow {WorkflowId} could not undo a step", workflowId);
                    run.ErrorMessage += $" Undo failed: {ex.Message}";
                }
            }
        }
        run.CompletedAt = DateTime.UtcNow;
        await runs.CompleteAsync(run);
        await notifications.SendWorkflowRunAsync(run, workflow.Name, CancellationToken.None);
        return run;
    }

    // Partial inputs run when idle
    private static PendingNode? Next(LinkedList<PendingNode> pending, List<PendingNode> waiting)
    {
        if (pending.First is { } first)
        {
            pending.RemoveFirst();
            return first.Value;
        }
        if (waiting.Count == 0)
            return null;
        var partial = waiting[0];
        waiting.RemoveAt(0);
        return partial;
    }

    private static void Push(LinkedList<PendingNode> pending, List<PendingNode> children)
    {
        for (var i = children.Count - 1; i >= 0; i--)
            pending.AddFirst(children[i]);
    }

    private static List<PendingNode> Deliver(WorkflowGraph graph, string nodeId, Dictionary<string, WorkflowItems> outputs, List<PendingNode> waiting)
    {
        var children = new List<PendingNode>();
        foreach (var edge in graph.From(nodeId))
        {
            var target = graph.Nodes[edge.To];
            if (!outputs.TryGetValue(edge.FromPort, out var items) || items.IsEmpty && graph.IsBackEdge(edge) && target.Type != WorkflowNodeCatalog.Loop)
                continue;
            var ports = graph.InputPorts(target.Id);
            if (ports.Length <= 1)
            {
                children.Add(new PendingNode(target, new() { [edge.ToPort] = items }));
                continue;
            }
            var slot = waiting.FirstOrDefault(w => w.Node.Id == target.Id && !w.Inputs.ContainsKey(edge.ToPort));
            if (slot is null)
                waiting.Add(slot = new PendingNode(target, new()));
            slot.Inputs[edge.ToPort] = items;
            if (ports.All(slot.Inputs.ContainsKey))
            {
                waiting.Remove(slot);
                children.Add(slot);
            }
        }
        return children;
    }

    private async Task SkipAsync(WorkflowExecution run, NodeRun record)
    {
        record.Status = WorkflowRunStatus.Skipped;
        record.CompletedAt = record.StartedAt;
        run.Nodes.Add(record);
        await runs.SaveNodeAsync(record);
    }

    private static bool SkipsWhenEmpty(WorkflowNode node) =>
        WorkflowNodeCatalog.Catalog.Find(node.Type) is not { RunsWhenEmpty: true } || NodeConfig.Flag(node.Config ?? new(), "SkipWhenEmpty");
}
