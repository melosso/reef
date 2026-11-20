using System.Text.Json.Nodes;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Conversion;

public sealed class WorkflowChain
{
    private const double Spacing = 240;
    private readonly WorkflowDocument _document;
    private string _last;

    public WorkflowChain(string name)
    {
        _document = new WorkflowDocument { Name = name };
        _document.Nodes.Add(new WorkflowNode { Id = "start", Type = "manual", X = 0, Y = 0, Config = new JsonObject() });
        _last = "start";
    }

    public string Last => _last;

    public WorkflowDocument Document => _document;

    public WorkflowChain Then(string id, string type, JsonObject config) => Branch(_last, id, type, config, keep: false);

    public WorkflowChain Branch(string from, string id, string type, JsonObject config, bool keep = true)
    {
        var parent = _document.Nodes.First(n => n.Id == from);
        var siblings = _document.Edges.Count(e => e.From == from);
        _document.Nodes.Add(new WorkflowNode { Id = id, Type = type, X = parent.X + Spacing, Y = parent.Y + siblings * 120, Config = config });
        _document.Edges.Add(new WorkflowEdge { From = from, To = id });
        if (!keep)
            _last = id;
        return this;
    }
}
