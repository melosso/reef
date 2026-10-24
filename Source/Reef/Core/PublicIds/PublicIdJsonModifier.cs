using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Reef.Core.Models;

namespace Reef.Core.PublicIds;

// Serializes integer id properties as Ulids and reads Ulids back, so the api never shows or accepts a guessable id.
public sealed class PublicIdJsonModifier(PublicIdMap ids)
{
    public void Apply(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        for (var i = 0; i < typeInfo.Properties.Count; i++)
        {
            var property = typeInfo.Properties[i];
            var declaringType = property.DeclaringType;
            var clrName = (property.AttributeProvider as System.Reflection.MemberInfo)?.Name ?? property.Name;

            if (declaringType == typeof(ReorderOutputTargetsRequest) && clrName == nameof(ReorderOutputTargetsRequest.Ids))
            {
                typeInfo.Properties[i] = IdList(typeInfo, property, "ImportOutputTargets");
                continue;
            }

            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (underlying != typeof(int) && underlying != typeof(long)) continue;

            var table = PublicIdTables.For(declaringType, clrName);
            if (table is not null)
                typeInfo.Properties[i] = Id(typeInfo, property, underlying, table);
        }
    }

    private JsonPropertyInfo Id(JsonTypeInfo typeInfo, JsonPropertyInfo original, Type underlying, Func<object, string?> table)
    {
        var nullable = Nullable.GetUnderlyingType(original.PropertyType) is not null;
        var replacement = typeInfo.CreateJsonPropertyInfo(typeof(string), original.Name);
        replacement.Order = original.Order;

        if (original.Get is { } get)
            replacement.Get = owner => get(owner) is { } value && table(owner) is { } name ? ids.ToUlid(name, Convert.ToInt64(value)) : null;

        if (original.Set is { } set)
            replacement.Set = (owner, value) =>
            {
                if (value is null)
                {
                    set(owner, nullable ? null : Activator.CreateInstance(underlying));
                    return;
                }
                if (table(owner) is not { } name || !ids.TryToId(name, (string)value, out var id))
                    throw new JsonException($"{original.Name} does not match a known id");
                set(owner, underlying == typeof(int) ? (object)checked((int)id) : id);
            };

        return replacement;
    }

    private JsonPropertyInfo IdList(JsonTypeInfo typeInfo, JsonPropertyInfo original, string table)
    {
        var replacement = typeInfo.CreateJsonPropertyInfo(typeof(List<string>), original.Name);
        if (original.Get is { } get)
            replacement.Get = owner => (get(owner) as List<int>)?.Select(id => ids.ToUlid(table, id)).ToList();
        if (original.Set is { } set)
            replacement.Set = (owner, value) => set(owner, ((List<string>?)value ?? []).Select(ulid =>
                ids.TryToId(table, ulid, out var id) ? checked((int)id) : throw new JsonException($"{original.Name} does not match a known id")).ToList());
        return replacement;
    }
}
