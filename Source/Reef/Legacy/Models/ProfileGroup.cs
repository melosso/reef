using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Profile group entity for organizing profiles into folders
/// </summary>
public class ProfileGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ParentId { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
