using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Profile dependency entity for execution chains
/// </summary>
public class ProfileDependency
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int DependsOnProfileId { get; set; }
    public int ExecutionOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
