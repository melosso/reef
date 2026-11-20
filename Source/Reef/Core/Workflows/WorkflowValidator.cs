using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments;
using Reef.Core.ProfileDocuments.Models;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows;

public static class WorkflowValidator
{
    public static List<ProfileDocumentError> Validate(WorkflowCatalog catalog, WorkflowDocument document)
    {
        var errors = new List<ProfileDocumentError>();
        if (string.IsNullOrWhiteSpace(document.Name))
            errors.Add(new("Workflow: Name is required", null, "Name"));

        var nodes = new Dictionary<string, (WorkflowNode Node, WorkflowNodeDefinition Definition)>();
        foreach (var node in document.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id) || nodes.ContainsKey(node.Id))
            {
                errors.Add(new($"Node id '{node.Id}' must be unique and not empty", node.Id));
                continue;
            }
            if (catalog.Find(node.Type) is not { } definition)
            {
                errors.Add(new($"Unknown node type '{node.Type}'", node.Id));
                continue;
            }
            nodes[node.Id] = (node, definition);
            ProfileDocumentValidator.ValidateFields(definition.Fields, node.Config ?? new JsonObject(), node.Id, node.Name ?? definition.Label, errors);
        }

        if (!nodes.Values.Any(n => n.Definition.Group == WorkflowNodeGroups.Triggers))
            errors.Add(new("Add a trigger node to start the workflow"));

        var valid = new List<WorkflowEdge>();
        foreach (var edge in document.Edges)
        {
            if (!nodes.TryGetValue(edge.From, out var from) || !nodes.TryGetValue(edge.To, out var to))
            {
                errors.Add(new($"Connection from '{edge.From}' to '{edge.To}' points at a node that does not exist",
                    nodes.ContainsKey(edge.From) ? edge.From : null));
                continue;
            }
            if (!from.Definition.OutputPorts(from.Node.Config ?? new JsonObject()).Contains(edge.FromPort))
                errors.Add(new($"{from.Definition.Label} has no output '{edge.FromPort}'", edge.From));
            else if (!to.Definition.Inputs.Contains(edge.ToPort))
                errors.Add(new($"{to.Definition.Label} has no input '{edge.ToPort}'", edge.To));
            else if (valid.Any(e => e.From == edge.From && e.FromPort == edge.FromPort && e.To == edge.To && e.ToPort == edge.ToPort))
                errors.Add(new($"Connection from '{edge.From}' to '{edge.To}' is listed twice", edge.From));
            else
                valid.Add(edge);
        }

        if (Looped(nodes.Keys, valid) is { Count: > 0 } looped)
            errors.Add(new($"Connections form a loop through {string.Join(", ", looped)}", looped[0]));

        return errors;
    }

    // Kahn's algorithm, the nodes it cannot order sit on a cycle or after one.
    private static List<string> Looped(IEnumerable<string> ids, List<WorkflowEdge> edges)
    {
        var incoming = ids.ToDictionary(id => id, id => edges.Count(e => e.To == id));
        var ready = new Queue<string>(incoming.Where(p => p.Value == 0).Select(p => p.Key));
        while (ready.TryDequeue(out var id))
        {
            incoming.Remove(id);
            foreach (var edge in edges.Where(e => e.From == id))
            {
                if (--incoming[edge.To] == 0)
                    ready.Enqueue(edge.To);
            }
        }
        return [.. incoming.Keys];
    }
}
