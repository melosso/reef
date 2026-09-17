using Reef.Core.Models;

namespace Reef.Core.Services;

public static class GraphExecutor
{
    public static (List<ProfileGraphNode> Ordered, string? Error) Sort(ProfileGraph graph)
    {
        var nodesById = graph.Nodes.ToDictionary(n => n.Id);
        var inDegree = graph.Nodes.ToDictionary(n => n.Id, _ => 0);
        var adjacency = graph.Nodes.ToDictionary(n => n.Id, _ => new List<string>());

        foreach (var edge in graph.Edges)
        {
            if (!nodesById.ContainsKey(edge.From) || !nodesById.ContainsKey(edge.To))
                return (new List<ProfileGraphNode>(), $"Edge references unknown node: {edge.From} -> {edge.To}");

            adjacency[edge.From].Add(edge.To);
            inDegree[edge.To]++;
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var ordered = new List<ProfileGraphNode>();

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            ordered.Add(nodesById[id]);

            foreach (var next in adjacency[id])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        if (ordered.Count != graph.Nodes.Count)
            return (new List<ProfileGraphNode>(), "Graph contains a cycle");

        return (ordered, null);
    }
}
