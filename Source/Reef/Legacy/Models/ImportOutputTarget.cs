using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// A destination to which parsed import rows are fanned out after the primary DB write.
/// Mirrors the ImportOutputTargets table.
/// </summary>
public class ImportOutputTarget
{
    public int Id { get; set; }
    public int ImportProfileId { get; set; }
    public int DestinationId { get; set; }
    public int? DestinationEndpointId { get; set; }

    /// <summary>JSON, CSV, JSONL, or XML</summary>
    public string OutputFormat { get; set; } = "JSON";

    /// <summary>Template for the output filename. Tokens: {profile}, {timestamp}, {date}, {guid}</summary>
    public string? FilenameTemplate { get; set; }

    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Warn | Fail, what to do when this target fails</summary>
    public string OnFailure { get; set; } = "Warn";

    public DateTime CreatedAt { get; set; }

    // Joined (not stored in DB) 
    public string? DestinationName { get; set; }
    public string? DestinationType { get; set; }
}
