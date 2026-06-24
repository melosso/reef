namespace Reef.Core.Recipes;

// Recipe definitions live in code, not the DB - only a handful ship and verification
// logic is bespoke per step, so a DB-driven recipe DSL would solve a problem that
// doesn't exist yet. Run progress (resumability) is what needs persistence - see
// RecipeRuns / RecipeService.
public class RecipeDefinition
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public required string Icon { get; set; }
    public List<RecipeStepDefinition> Steps { get; set; } = new();
}

public class RecipeStepDefinition
{
    public required string StepKey { get; set; }
    public required string Title { get; set; }
    public required RecipeEntityType EntityType { get; set; }
    public bool IsOptional { get; set; } = false;

    // Shared infrastructure reused across flows (Connection/Group/Destination); renders
    // under a "Shared" heading instead of its flow's own section.
    public bool IsShared { get; set; } = false;

    public RecipeVerifierKind? VerifierKind { get; set; }

    // UI grouping for non-shared steps (e.g. "Order Confirmation", "Tracking Link") so one
    // RecipeDefinition can hold multiple flows without flattening them into one step list.
    public string? FlowGroup { get; set; }

    // Opt-in for "simple mode": when true and the run has no saved value for this step yet,
    // RecipeService.StartRecipeAsync silently provisions (find-or-create) a sensible default
    // entity - a dedicated Sqlite staging Connection, or a Group named after the recipe - and
    // seeds it into StepStateJson as Verified. The wizard's step rail hides these steps unless
    // the user flips the "Advanced" toggle, letting them override with a real connection/group.
    public bool CanAutoProvision { get; set; } = false;
}

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

public enum RecipeVerifierKind
{
    Connection,
    HttpSource,
    StagingTable,
    EmailDestination,
    ScribanTemplate,
    ExportQuery
}
