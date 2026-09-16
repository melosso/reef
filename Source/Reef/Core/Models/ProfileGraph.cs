namespace Reef.Core.Models;

// the contract stored in Profile.CanvasLayoutJson, decoupled from any frontend canvas library's own export format
public class ProfileGraph
{
    public List<ProfileGraphNode> Nodes { get; set; } = new();
    public List<ProfileGraphEdge> Edges { get; set; } = new();
}

public class ProfileGraphNode
{
    public required string Id { get; set; }
    public required string Type { get; set; } // source, preprocess, deltasync, splitoutput, emailexport, template, destination, postprocess
    public object? Config { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}

public class ProfileGraphEdge
{
    public required string From { get; set; }
    public required string To { get; set; }
}
