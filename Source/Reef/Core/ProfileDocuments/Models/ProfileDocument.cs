using System.Text.Json.Nodes;

namespace Reef.Core.ProfileDocuments.Models;

public class ProfileDocument
{
    public string Kind { get; set; } = ProfileKinds.Export;
    public int? Id { get; set; }
    public string? Code { get; set; }
    public JsonObject Settings { get; set; } = new();
    public List<ProfileDocumentNode> Nodes { get; set; } = new();
}
