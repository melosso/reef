using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments;

public static class ProfileDocumentMapper
{
    private const double OriginX = 320;
    private const double OriginY = 40;
    private const double Spacing = 88;

    public static ProfileDocument ToDocument(ProfileCatalog catalog, JsonObject entity, ProfileDocumentLayout? layout, int? id, string? code)
    {
        var placed = layout?.Nodes
            .Where(n => !string.IsNullOrEmpty(n.Type))
            .GroupBy(n => n.Type)
            .ToDictionary(g => g.Key, g => g.First()) ?? new();

        var document = new ProfileDocument
        {
            Kind = catalog.Kind,
            Id = id,
            Code = code,
            Settings = ReadFields(catalog.Settings, entity)
        };

        foreach (var definition in catalog.Nodes.OrderBy(n => n.Rank))
        {
            if (!IsPresent(definition, entity))
                continue;
            var position = placed.GetValueOrDefault(definition.Type);
            document.Nodes.Add(new ProfileDocumentNode
            {
                Id = definition.Type,
                Type = definition.Type,
                X = position?.X ?? OriginX,
                Y = position?.Y ?? OriginY + document.Nodes.Count * Spacing,
                Config = ReadFields(definition.Fields, entity)
            });
        }

        return document;
    }

    public static JsonObject Apply(ProfileCatalog catalog, ProfileDocument document, JsonObject entity, JsonObject defaults)
    {
        var result = entity.DeepClone().AsObject();
        WriteFields(catalog.Settings, document.Settings, result, defaults);

        var present = document.Nodes.GroupBy(n => n.Type).ToDictionary(g => g.Key, g => g.First());

        foreach (var definition in catalog.Nodes)
        {
            foreach (var column in OwnedColumns(definition))
                EntityFields.Reset(result, column, defaults);
        }

        foreach (var definition in catalog.Nodes.OrderBy(n => n.Rank))
        {
            if (!present.TryGetValue(definition.Type, out var node))
                continue;
            var config = node.Config?.DeepClone().AsObject() ?? new JsonObject();
            definition.Normalize?.Invoke(config);
            foreach (var (column, value) in definition.Sets)
                result[column] = value?.DeepClone();
            WriteFields(definition.Fields, config, result, defaults);
        }

        return result;
    }

    public static ProfileDocumentLayout ToLayout(ProfileDocument document) => new()
    {
        Nodes = document.Nodes.Select(n => new ProfileDocumentNode { Id = n.Id, Type = n.Type, X = n.X, Y = n.Y }).ToList()
    };

    private static bool IsPresent(NodeDefinition definition, JsonObject entity)
    {
        if (definition.Required)
            return true;
        if (definition.Presence is null)
            return false;
        return NodeConditions.Matches(definition.Presence, key => ConfigPath.Get(entity, key));
    }

    private static IEnumerable<string> OwnedColumns(NodeDefinition definition) =>
        definition.Fields.Select(f => EntityFields.Column(f.Key)).Concat(definition.Sets.Keys).Distinct();

    private static JsonObject ReadFields(IEnumerable<NodeField> fields, JsonObject entity)
    {
        var config = new JsonObject();
        foreach (var field in fields)
        {
            var value = EntityFields.Read(entity, field);
            if (value is not null)
                ConfigPath.Set(config, field.Key, value);
        }
        return config;
    }

    private static void WriteFields(IEnumerable<NodeField> fields, JsonObject config, JsonObject entity, JsonObject defaults)
    {
        foreach (var field in fields.DistinctBy(f => f.Key))
            EntityFields.Write(entity, field, ConfigPath.Get(config, field.Key)?.DeepClone(), defaults);
    }
}
