using System.Text.Json;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;
using Serilog;

namespace Reef.Core.Workflows.Engine;

public class WorkflowEngine(WorkflowService workflows, WorkflowRunStore runs, IEnumerable<INodeExecutor> executors)
{
    private const int SampleRows = 20;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, INodeExecutor> _executors = executors.SelectMany(e => e.Types.Select(t => (t, e))).ToDictionary(p => p.t, p => p.e);

    public async Task<WorkflowExecution> RunAsync(int workflowId, string? triggerNodeId, WorkflowItems? triggerItems = null, CancellationToken ct = default)
    {
        var workflow = await workflows.GetAsync(workflowId, ct) ?? throw new KeyNotFoundException("Workflow not found");
        var trigger = workflow.Nodes.FirstOrDefault(n => triggerNodeId is null ? n.Type == "manual" : n.Id == triggerNodeId)
            ?? throw new KeyNotFoundException(triggerNodeId is null ? "Workflow has no manual trigger" : $"Trigger '{triggerNodeId}' not found");
        if (WorkflowNodeCatalog.Catalog.Find(trigger.Type)?.Group != WorkflowNodeGroups.Triggers)
            throw new InvalidOperationException($"Node '{trigger.Id}' is not a trigger");

        var run = new WorkflowExecution { WorkflowId = workflowId, WorkflowVersion = workflow.Version, TriggerNodeId = trigger.Id, StartedAt = DateTime.UtcNow };
        run.Id = await runs.StartAsync(run, ct);

        var outputs = new Dictionary<(string Node, string Port), WorkflowItems>();
        var commits = new List<Func<Task>>();
        var undos = new List<Func<Task>>();
        var workFolder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "reef-workflows", run.Id.ToString())).FullName;
        try
        {
            foreach (var node in Order(workflow, trigger.Id))
            {
                var record = new NodeRun { WorkflowExecutionId = run.Id, NodeId = node.Id, StartedAt = DateTime.UtcNow };
                var incoming = workflow.Edges.Where(e => e.To == node.Id && outputs.ContainsKey((e.From, e.FromPort))).ToList();
                if (node.Id != trigger.Id && incoming.Count == 0)
                {
                    await SkipAsync(run, record);
                    continue;
                }

                var inputs = node.Id == trigger.Id
                    ? new Dictionary<string, WorkflowItems> { [WorkflowPorts.Main] = triggerItems ?? new WorkflowItems() }
                    : incoming.GroupBy(e => e.ToPort).ToDictionary(g => g.Key, g => Combine(g.Select(e => outputs[(e.From, e.FromPort)])));
                if (node.Id != trigger.Id && inputs.Values.All(i => i.Items.Count == 0 && i.Files.Count == 0) && SkipsWhenEmpty(node))
                {
                    await SkipAsync(run, record);
                    continue;
                }
                try
                {
                    if (!_executors.TryGetValue(node.Type, out var executor))
                        throw new NodeExecutionException($"Node type '{node.Type}' cannot run yet");
                    var result = await executor.ExecuteAsync(new NodeContext { Workflow = workflow, Node = node, ExecutionId = run.Id, Inputs = inputs, WorkFolder = workFolder }, ct);
                    foreach (var (port, items) in result.Outputs)
                        outputs[(node.Id, port)] = items;
                    record.Status = WorkflowRunStatus.Success;
                    record.ErrorMessage = result.Warning;
                    if (result.OnRunSuccess is { } commit)
                        commits.Add(commit);
                    if (result.OnRunFailure is { } undo)
                        undos.Insert(0, undo);
                    record.RowCount = result.Outputs.Values.Sum(o => o.Items.Count);
                    record.OutputSampleJson = JsonSerializer.Serialize(result.Outputs.ToDictionary(o => o.Key, o => new
                    {
                        o.Value.Columns,
                        Items = o.Value.Items.Take(SampleRows),
                        o.Value.Files,
                        Total = o.Value.Items.Count
                    }), Json);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    Log.Warning(ex, "Workflow {WorkflowId} node {NodeId} failed", workflowId, node.Id);
                    record.Status = WorkflowRunStatus.Failed;
                    record.ErrorMessage = ex is NodeExecutionException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                    run.Status = WorkflowRunStatus.Failed;
                    run.ErrorMessage = $"{node.Name ?? node.Id}: {record.ErrorMessage}";
                }
                finally
                {
                    record.CompletedAt = DateTime.UtcNow;
                    run.Nodes.Add(record);
                    await runs.SaveNodeAsync(record);
                }
                if (run.Status == WorkflowRunStatus.Failed)
                    break;
            }
        }
        finally
        {
            Directory.Delete(workFolder, recursive: true);
        }

        if (run.Status != WorkflowRunStatus.Failed)
        {
            run.Status = WorkflowRunStatus.Success;
            try
            {
                foreach (var commit in commits)
                    await commit();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Workflow {WorkflowId} could not save its run state", workflowId);
                run.Status = WorkflowRunStatus.Failed;
                run.ErrorMessage = $"Output was written but the Smart Sync state was not saved: {ex.Message}";
            }
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
        return run;
    }

    // shortcut nodes run sequentially
    private static List<WorkflowNode> Order(WorkflowDocument workflow, string triggerId)
    {
        var reachable = new HashSet<string> { triggerId };
        var queue = new Queue<string>(reachable);
        while (queue.TryDequeue(out var id))
        {
            foreach (var edge in workflow.Edges.Where(e => e.From == id && reachable.Add(e.To)))
                queue.Enqueue(edge.To);
        }

        var edges = workflow.Edges.Where(e => reachable.Contains(e.From) && reachable.Contains(e.To)).ToList();
        var incoming = reachable.ToDictionary(id => id, id => edges.Count(e => e.To == id));
        var ready = new Queue<string>([triggerId]);
        var order = new List<WorkflowNode>();
        while (ready.TryDequeue(out var id))
        {
            order.Add(workflow.Nodes.First(n => n.Id == id));
            foreach (var edge in edges.Where(e => e.From == id))
            {
                if (--incoming[edge.To] == 0)
                    ready.Enqueue(edge.To);
            }
        }
        return order;
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

    private static WorkflowItems Combine(IEnumerable<WorkflowItems> parts)
    {
        var list = parts.ToList();
        if (list.Count == 1)
            return list[0];
        return new WorkflowItems
        {
            Columns = [.. list.SelectMany(p => p.Columns).DistinctBy(c => c.Name)],
            Items = [.. list.SelectMany(p => p.Items)],
            Files = [.. list.SelectMany(p => p.Files)]
        };
    }
}
