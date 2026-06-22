namespace Reef.Core.Recipes;

/// <summary>
/// The extension point for Store recipes. Phase 1 shipped WooCommerce Order Confirmation;
/// Phase 3 added System Error Daily Digest (ErrorDigestRecipe) as a second, deliberately
/// simpler recipe to prove the engine is genuinely generic - it required zero changes to
/// RecipeService, RecipesEndpoints, or any IRecipeVerifier. Adding a future recipe means
/// adding another RecipeDefinition here, nothing else.
/// </summary>
public static class RecipeRegistry
{
    public static IReadOnlyList<RecipeDefinition> All { get; } = new List<RecipeDefinition>
    {
        WooCommerceRecipe.Definition,
        ErrorDigestRecipe.Definition,
        MagentoRecipe.Definition,
        ExactGlobeRecipe.Definition
    };

    public static RecipeDefinition? GetByKey(string key) =>
        All.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}
