using System.Text.Json.Nodes;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments;

public static class ProfileDocumentValidator
{
    public static List<NodeError> Validate(ProfileCatalog catalog, ProfileDocument document)
    {
        var errors = new List<NodeError>();
        var fieldErrors = new List<NodeError>();
        NodeFieldValidator.Validate(catalog.Settings, document.Settings, null, "Profile", fieldErrors);

        var definitions = catalog.Nodes.ToDictionary(n => n.Type);
        var seen = new Dictionary<string, ProfileDocumentNode>();

        foreach (var node in document.Nodes)
        {
            if (!definitions.TryGetValue(node.Type, out var definition))
            {
                errors.Add(new($"Unknown node type '{node.Type}'", node.Id));
                continue;
            }
            if (!seen.TryAdd(node.Type, node))
            {
                errors.Add(new($"A profile can hold one {definition.Label} node", node.Id));
                continue;
            }
            NodeFieldValidator.Validate(definition.Fields, node.Config ?? new JsonObject(), node.Id, definition.Label, fieldErrors);
        }

        foreach (var definition in catalog.Nodes.Where(d => d.Required && !seen.ContainsKey(d.Type)))
            errors.Add(new($"Add a {definition.Label} node"));

        foreach (var group in catalog.RequireOneOf)
        {
            if (!group.Any(seen.ContainsKey))
                errors.Add(new($"Add a {string.Join(" or ", group.Select(t => definitions[t].Label))} node"));
        }

        foreach (var (type, node) in seen)
        {
            var definition = definitions[type];
            foreach (var other in definition.Excludes.Where(seen.ContainsKey))
            {
                if (string.CompareOrdinal(type, other) < 0 || !definitions[other].Excludes.Contains(type))
                    errors.Add(new($"{definition.Label} cannot be combined with {definitions[other].Label}", node.Id));
            }
            foreach (var needed in definition.Requires.Where(t => !seen.ContainsKey(t)))
                errors.Add(new($"{definition.Label} needs a {definitions[needed].Label} node", node.Id));
        }

        errors.AddRange(fieldErrors);
        return errors;
    }
}
