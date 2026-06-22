namespace Reef.Core.Recipes;

public class RecipeVerifyResult
{
    public required bool Success { get; set; }
    public required string Message { get; set; }
    public string? Detail { get; set; }
}

public class RecipeVerifyContext
{
    public int? EntityId { get; set; }
    public int? ConnectionId { get; set; }
    public string? TableName { get; set; }
    public Dictionary<string, object?> Params { get; set; } = [];

    // Lets each recipe supply mock data shaped for its own template's columns instead of
    // the verifier hardcoding one recipe's shape. Null falls back to the verifier's
    // built-in WooCommerce-shaped default.
    public Dictionary<string, object>? MockTemplateRow { get; set; }
}

// Real, live verification of a step's entity - never just "saved to DB".
public interface IRecipeVerifier
{
    Task<RecipeVerifyResult> VerifyAsync(RecipeVerifyContext context, CancellationToken ct = default);
}
