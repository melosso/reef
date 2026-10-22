namespace Reef.Core.ProfileDocuments.Models;

// stored in Profile.CanvasLayoutJson, node positions only since configuration lives in the profile columns
public class ProfileDocumentLayout
{
    public List<ProfileDocumentNode> Nodes { get; set; } = new();
}
