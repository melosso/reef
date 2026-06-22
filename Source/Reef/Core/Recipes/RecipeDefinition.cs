namespace Reef.Core.Recipes;

/// <summary>
/// Top-level definition of a guided Store recipe: a named, ordered sequence of steps
/// that creates and live-verifies a known-good integration pattern end to end.
/// Recipe definitions live in code (not the DB) - only one recipe ships in Phase 1,
/// and the verification logic is bespoke per step, so a DB-driven recipe DSL would be
/// solving a problem that doesn't exist yet. Run progress (resumability) is the part
/// that genuinely needs persistence - see RecipeRuns table / RecipeService.
/// </summary>
public class RecipeDefinition
{
    /// <summary>Stable machine key, e.g. "woocommerce-order-confirmation". Used in routes and StepStateJson.</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public List<RecipeStepDefinition> Steps { get; set; } = new();
}

/// <summary>
/// One step in a recipe's guided flow. Each step maps to exactly one entity
/// (Connection, ImportProfile, Profile, etc.) that RecipeService creates/updates
/// via the existing entity service, then optionally live-verifies via an IRecipeVerifier.
/// </summary>
public class RecipeStepDefinition
{
    /// <summary>Stable machine key within the recipe, e.g. "connection", "staging-table".</summary>
    public required string StepKey { get; set; }

    public required string Title { get; set; }

    /// <summary>Which kind of entity this step creates/updates: Connection, Group, Destination, StagingTable, ImportProfile, QueryTemplate, Profile, Job.</summary>
    public required RecipeEntityType EntityType { get; set; }

    /// <summary>When true, the wizard allows skipping this step entirely (e.g. optional Jobs step).</summary>
    public bool IsOptional { get; set; } = false;

    /// <summary>When true, this step is shared infrastructure reused across multiple flows in the recipe (Connection/Group/Destination), shown under a "Shared" heading in the UI.</summary>
    public bool IsShared { get; set; } = false;

    /// <summary>Which verifier kind performs the live check for this step. Null when the step has no live verification (e.g. Group, optional Jobs).</summary>
    public RecipeVerifierKind? VerifierKind { get; set; }

    /// <summary>
    /// UI grouping label for non-shared steps (e.g. "Order Confirmation", "Tracking Link").
    /// Lets a single RecipeDefinition contain multiple flows that share steps 1-3 (IsShared=true)
    /// while still rendering as distinct step-rail sections instead of one flattened list.
    /// Ignored when IsShared is true (those always render under "Shared").
    /// </summary>
    public string? FlowGroup { get; set; }
}

/// <summary>
/// The kind of entity a recipe step creates or updates via the existing entity services.
/// </summary>
public enum RecipeEntityType
{
    Connection,
    Group,
    Destination,
    StagingTable,
    ImportProfile,
    QueryTemplate,
    Profile,
    Job,
    Webhook
}

/// <summary>
/// The kind of live verification a recipe step performs. Each kind maps to one
/// IRecipeVerifier adapter, shared across every step that needs that kind of check.
/// </summary>
public enum RecipeVerifierKind
{
    /// <summary>Real connectivity probe against a database Connection (wraps ConnectionService.TestConnectionAsync).</summary>
    Connection,

    /// <summary>Reachability/auth probe against an HTTP API source (wraps HttpApiSource via ImportProfilesEndpoints.TestSource).</summary>
    HttpSource,

    /// <summary>Confirms a staging table created by the wizard actually exists (wraps DatabaseImportTarget.TestAsync).</summary>
    StagingTable,

    /// <summary>Real SMTP send (wraps DestinationService.TestDestinationConfigurationAsync).</summary>
    EmailDestination,

    /// <summary>Real Scriban render against a staging row or mock data (wraps the QueryTemplate preview path).</summary>
    ScribanTemplate,

    /// <summary>Runs the Export Profile's query against the live staging table (wraps ProfilesEndpoints TestQuery/TestQueryPreview path).</summary>
    ExportQuery
}
