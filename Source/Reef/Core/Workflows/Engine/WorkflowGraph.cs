using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine;

public class WorkflowGraph
{
    private readonly List<WorkflowEdge> _edges;
    private readonly HashSet<WorkflowEdge> _backEdges = [];

    public WorkflowGraph(WorkflowDocument workflow, string triggerId)
    {
        Nodes = workflow.Nodes.ToDictionary(n => n.Id);
        _edges = workflow.Edges;
        Visit(triggerId, []);
    }

    public IReadOnlyDictionary<string, WorkflowNode> Nodes { get; }
    public HashSet<string> Reachable { get; } = [];

    public IEnumerable<WorkflowEdge> From(string nodeId) => _edges.Where(e => e.From == nodeId);

    public bool IsBackEdge(WorkflowEdge edge) => _backEdges.Contains(edge);

    public string[] InputPorts(string nodeId) => [.. _edges.Where(e => e.To == nodeId && Reachable.Contains(e.From)).Select(e => e.ToPort).Distinct()];

    private void Visit(string id, HashSet<string> path)
    {
        Reachable.Add(id);
        path.Add(id);
        foreach (var edge in From(id))
        {
            if (path.Contains(edge.To))
                _backEdges.Add(edge);
            else if (!Reachable.Contains(edge.To))
                Visit(edge.To, path);
        }
        path.Remove(id);
    }
}
