using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Request body for reordering output targets
/// </summary>
public class ReorderOutputTargetsRequest
{
    public List<int> Ids { get; set; } = new();
}
