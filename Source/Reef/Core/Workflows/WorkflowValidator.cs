using System.Text.Json.Nodes;
using NCrontab;
using Reef.Core.Workflows.Models;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.Nodes.Exceptions;

namespace Reef.Core.Workflows;

public static class WorkflowValidator
{
    public static List<NodeError> Validate(WorkflowCatalog catalog, WorkflowDocument document)
    {
        var errors = new List<NodeError>();
        if (string.IsNullOrWhiteSpace(document.Name))
            errors.Add(new("Workflow: Name is required", null, "Name"));

        if (document.MaxPasses is < 1 or > 100_000)
            errors.Add(new("Workflow: Pass limit must be between 1 and 100000", null, "MaxPasses"));

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
            NodeFieldValidator.Validate(definition.Fields, node.Config ?? new JsonObject(), node.Id, node.Name ?? definition.Label, errors);
            if (node.Type == "schedule")
                ValidateSchedule(node, errors);
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

        return errors;
    }

    private static void ValidateSchedule(WorkflowNode node, List<NodeError> errors)
    {
        var config = node.Config ?? new JsonObject();
        var label = node.Name ?? "Schedule";
        if (ConfigPath.ToText(config["Repeat"]) == "Interval")
        {
            if (!int.TryParse(ConfigPath.ToText(config["EveryMinutes"]), out var minutes) || minutes < 1)
                errors.Add(new($"{label}: Every minutes must be 1 or more", node.Id, "EveryMinutes"));
        }
        else if (ConfigPath.ToText(config["Cron"]) is { Length: > 0 } cron && CrontabSchedule.TryParse(cron) is null)
            errors.Add(new($"{label}: Cron expression '{cron}' is not valid", node.Id, "Cron"));
    }
}
