namespace Reef.Core.ProfileDocuments.Models;

// Node positions for CanvasLayoutJson. Step configuration is stored in profile columns.
public class ProfileDocumentLayout
{
    public List<ProfileDocumentNode> Nodes { get; set; } = new();
}
