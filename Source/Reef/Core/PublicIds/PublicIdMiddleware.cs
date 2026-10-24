using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;

namespace Reef.Core.PublicIds;

// Hands endpoints the integer id behind each Ulid in the route and query string, so handlers keep working with ints.
public sealed class PublicIdMiddleware(RequestDelegate next, PublicIdMap ids)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint endpoint)
        {
            foreach (var (name, policies) in endpoint.RoutePattern.ParameterPolicies)
            {
                if (TableOf(policies) is { } argument && PublicIdTables.RouteTable(argument, context.Request.RouteValues, context) is { } table
                    && context.Request.RouteValues[name]?.ToString() is { } ulid && ids.TryToId(table, ulid, out var id))
                    context.Request.RouteValues[name] = id.ToString();
            }
        }

        var query = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        var rewritten = false;
        foreach (var (key, value) in context.Request.Query)
        {
            if (PublicIdTables.ForName(key) is { } table && !StringValues.IsNullOrEmpty(value))
            {
                if (!ids.TryToId(table, value.ToString(), out var id))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { success = false, message = $"{key} does not match a known id" });
                    return;
                }
                query[key] = id.ToString();
                rewritten = true;
            }
            else
            {
                query[key] = value;
            }
        }
        if (rewritten) context.Request.Query = new QueryCollection(query);

        await next(context);
    }

    private static string? TableOf(IReadOnlyList<RoutePatternParameterPolicyReference> policies) =>
        policies.Select(p => p.Content).FirstOrDefault(c => c is not null && c.StartsWith("ulid(", StringComparison.Ordinal)) is { } content
            ? content["ulid(".Length..^1]
            : null;
}
