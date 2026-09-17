namespace Reef.Core.Models;

// the contract stored in Profile.CanvasLayoutJson, decoupled from any frontend canvas library's own export format
public class ProfileGraph
{
    public List<ProfileGraphNode> Nodes { get; set; } = new();
    public List<ProfileGraphEdge> Edges { get; set; } = new();
}
