namespace Reef.Core.PublicIds;

// Matches a route value only when it is the Ulid of a row in the named table, so unknown ids end in a 404.
public sealed class UlidRouteConstraint(PublicIdMap ids, string table) : IRouteConstraint
{
    private readonly string _table = table is PublicIdTables.ByKind or PublicIdTables.ByType ? table : PublicIdTables.Known(table);

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection) =>
        values.TryGetValue(routeKey, out var value) && value?.ToString() is { } text &&
        (routeDirection == RouteDirection.UrlGeneration
            ? PublicIdMap.IsUlid(text)
            : PublicIdTables.RouteTable(_table, values, httpContext) is { } name && ids.TryToId(name, text, out _));
}
