using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Reef.Core.Nodes.Models;

namespace Reef.Core.Nodes;

public static partial class NodeFieldValidator
{
    public static void Validate(IEnumerable<NodeField> fields, JsonObject config, string? nodeId, string owner, List<NodeError> errors)
    {
        foreach (var field in fields)
        {
            if (field.Hidden || !NodeConditions.Matches(field.ShowWhen, key => ConfigPath.Get(config, key)))
                continue;

            var value = ConfigPath.Get(config, field.Key);
            var problem = Check(field, value);
            if (problem is not null)
                errors.Add(new($"{owner}: {field.Label} {problem}", nodeId, field.Key));

            if (field.Columns is null || value is not JsonArray rows)
                continue;
            for (var i = 0; i < rows.Count; i++)
            {
                foreach (var column in field.Columns)
                {
                    var cell = Check(column, (rows[i] as JsonObject)?[column.Key]);
                    if (cell is not null)
                        errors.Add(new($"{owner}: {field.Label} row {i + 1} {column.Label} {cell}", nodeId, field.Key));
                }
            }
        }
    }

    private static string? Check(NodeField field, JsonNode? value)
    {
        if (field.OptionsSource is not null && value?.GetValueKind() == JsonValueKind.Number && value.ToJsonString() is "0" or "-1")
            value = null;
        if (!ConfigPath.HasValue(value) && !(value is JsonValue && field.Kind is NodeFieldKinds.Bool or NodeFieldKinds.Number))
            return field.Required ? "is required" : null;

        switch (field.Kind)
        {
            case NodeFieldKinds.Number when value?.GetValueKind() != JsonValueKind.Number:
                return "must be a number";
            case NodeFieldKinds.Bool when value?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False):
                return "must be on or off";
            case NodeFieldKinds.List when value is not JsonArray:
                return "must be a list";
            case NodeFieldKinds.Table when value is not JsonArray:
                return "must be a list of rows";
            case NodeFieldKinds.Json when value?.GetValueKind() == JsonValueKind.String && ConfigPath.ParseAny(value) == value:
                return "must be valid JSON";
            case NodeFieldKinds.Select when field.Options is { Count: > 0 } && !field.Options.Any(o => o.Value == ConfigPath.ToText(value)):
                return "has an unknown value";
            case NodeFieldKinds.Select when field.OptionsSource is not null && value?.GetValueKind() != JsonValueKind.Number:
                return "must be selected from the list";
        }

        var text = ConfigPath.ToText(value);
        return field.Format switch
        {
            NodeFieldFormats.Email when text.Split(',', ';').Any(a => !MailAddress.TryCreate(a.Trim(), out _)) => "must be a valid email address",
            NodeFieldFormats.Identifier when !IdentifierPattern().IsMatch(text) => "must be a valid SQL identifier",
            _ => null
        };
    }

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex IdentifierPattern();
}
