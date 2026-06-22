namespace Reef.Core.Recipes;

/// <summary>
/// Persisted progress for one user's walk through a recipe. Mirrors the RecipeRuns
/// table. Recipe *definitions* live in code (RecipeRegistry); only run *progress*
/// needs DB persistence, so a user can close the wizard mid-setup and resume later
/// with already-created/verified entities intact.
/// </summary>
public class RecipeRun
{
    public int Id { get; set; }
    public required string RecipeKey { get; set; }

    /// <summary>InProgress, Completed, Abandoned.</summary>
    public string Status { get; set; } = "InProgress";

    public string? CurrentStepKey { get; set; }

    /// <summary>JSON: { stepKey: { entityId, verified, lastVerifiedAt, paramsJson } }</summary>
    public string? StepStateJson { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Per-step state stored inside RecipeRun.StepStateJson, keyed by RecipeStepDefinition.StepKey.
/// </summary>
public class RecipeStepState
{
    /// <summary>Id of the entity this step created/updated (Connection, ImportProfile, Destination, etc.).</summary>
    public int? EntityId { get; set; }

    public bool Verified { get; set; }

    public DateTime? LastVerifiedAt { get; set; }

    public string? LastVerifyMessage { get; set; }

    /// <summary>Raw params the step was last saved with - lets verifiers (e.g. ExportQueryVerifier) see step-specific config like the query text without re-fetching the entity.</summary>
    public Dictionary<string, object?> Params { get; set; } = new();

    /// <summary>True when the user explicitly chose to skip an optional step.</summary>
    public bool Skipped { get; set; }
}
