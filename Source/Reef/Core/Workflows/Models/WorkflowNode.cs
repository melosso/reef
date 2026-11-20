using System.Text.Json.Nodes;

namespace Reef.Core.Workflows.Models;

public class WorkflowNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Name { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public JsonObject? Config { get; set; }
}
