namespace Reef.Core.Recipes;

// Single-flow recipe (split out of WooCommerceRecipe's former Flow B) - pulls shipment
// tracking data from WooCommerce's REST API and emails customers an update. Step keys are
// plain (no "shipments-" prefix) since there's only one flow here, mirroring MagentoRecipe's
// shape exactly - RecipeService dispatches on recipe.Key instead.
public static class WooCommerceTrackingRecipe
{
    public const string Key = "woocommerce-tracking-link";

    public static RecipeDefinition Definition { get; } = new()
    {
        Key = Key,
        Name = "WooCommerce Tracking Link",
        Description = "Pull shipment tracking data from your WooCommerce store's REST API and automatically email customers a tracking-link update.",
        Category = "E-commerce",
        Icon = "truck",
        Steps = new List<RecipeStepDefinition>
        {
            new()
            {
                StepKey = "connection",
                Title = "Database Connection",
                EntityType = RecipeEntityType.Connection,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.Connection,
                CanAutoProvision = true
            },
            new()
            {
                StepKey = "group",
                Title = "Organize",
                EntityType = RecipeEntityType.Group,
                IsShared = true,
                VerifierKind = null,
                CanAutoProvision = true
            },
            new()
            {
                StepKey = "destination",
                Title = "Email Destination",
                EntityType = RecipeEntityType.Destination,
                IsShared = true,
                VerifierKind = RecipeVerifierKind.EmailDestination
            },
            new()
            {
                StepKey = "staging-table",
                Title = "Staging Table",
                EntityType = RecipeEntityType.StagingTable,
                VerifierKind = RecipeVerifierKind.StagingTable
            },
            new()
            {
                StepKey = "import-profile",
                Title = "Pull Tracking Updates from WooCommerce",
                EntityType = RecipeEntityType.ImportProfile,
                VerifierKind = RecipeVerifierKind.HttpSource
            },
            new()
            {
                StepKey = "query-template",
                Title = "Email Template",
                EntityType = RecipeEntityType.QueryTemplate,
                VerifierKind = RecipeVerifierKind.ScribanTemplate
            },
            new()
            {
                StepKey = "export-profile",
                Title = "Send Tracking Emails",
                EntityType = RecipeEntityType.Profile,
                VerifierKind = RecipeVerifierKind.ExportQuery
            },
            new()
            {
                StepKey = "jobs",
                Title = "Scheduling & Webhook (optional)",
                EntityType = RecipeEntityType.Job,
                IsOptional = true,
                VerifierKind = null
            }
        }
    };
}
