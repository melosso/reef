using System.Text.Json.Nodes;

namespace Reef.Core.ProfileDocuments.Models;

public class ProfileDocumentNode
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public JsonObject? Config { get; set; }
}
