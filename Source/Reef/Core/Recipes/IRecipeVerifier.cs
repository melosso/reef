namespace Reef.Core.Recipes;

/// <summary>
/// Result of a live verification check performed by an IRecipeVerifier.
/// </summary>
public class RecipeVerifyResult
{
    public required bool Success { get; set; }
    public required string Message { get; set; }

    /// <summary>Optional extra detail surfaced to the wizard UI (e.g. row count, response time, rendered preview).</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// Context handed to a verifier so it can locate the entity (and any peer entities)
/// needed to perform a real, live check. Built by RecipeService from the run's
/// StepStateJson immediately before verification.
/// </summary>
public class RecipeVerifyContext
{
    /// <summary>Id of the entity created/updated by this step (Connection, ImportProfile, Destination, etc.), when applicable.</summary>
    public int? EntityId { get; set; }

    /// <summary>Id of the staging-table Connection, when this verifier needs to reach the database (StagingTable, ExportQuery).</summary>
    public int? ConnectionId { get; set; }

    /// <summary>Staging table name, when this verifier needs to confirm/query a table (StagingTable, ExportQuery, ScribanTemplate).</summary>
    public string? TableName { get; set; }

    /// <summary>Raw step parameters as saved by ExecuteStepAsync, for verifiers that need step-specific config (e.g. the export query text).</summary>
    public Dictionary<string, object?> Params { get; set; } = [];

    /// <summary>
    /// Recipe-specific mock row used by ScribanTemplateVerifier when no real staging row is
    /// available (table empty or this recipe has no staging table at all). Lets each recipe
    /// supply mock data shaped for its own template's column names instead of the verifier
    /// hardcoding one recipe's shape - keeps the verifier itself recipe-agnostic. Null means
    /// "use the verifier's built-in WooCommerce-shaped default" for backward compatibility.
    /// </summary>
    public Dictionary<string, object>? MockTemplateRow { get; set; }
}

/// <summary>
/// Performs a real, live verification of one recipe step's entity - never just
/// "saved to DB". All implementations are async, accept a CancellationToken, and
/// avoid blocking calls, matching the bar IScriptRunner/InterpreterService already meet.
/// </summary>
public interface IRecipeVerifier
{
    Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default);
}
