namespace Reef.Core.Workflows.Models;

public class WorkflowEdge
{
    public string From { get; set; } = "";
    public string FromPort { get; set; } = WorkflowPorts.Main;
    public string To { get; set; } = "";
    public string ToPort { get; set; } = WorkflowPorts.Main;
}
