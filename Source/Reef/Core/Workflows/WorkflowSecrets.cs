using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Exceptions;
using Reef.Core.Nodes.Models;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Models;
using Reef.Core.Services;

namespace Reef.Core.Workflows;

public sealed class WorkflowSecrets(EncryptionService encryption)
{
    public const string Masked = "[SECRET]";
    private static readonly string[] Targets = ["From", "Settings.url", "Settings.host", "Settings.port", "Settings.serviceUrl", "Settings.uncPath", "Settings.region"];

    // Empty or masked input keeps secret
    public void Protect(WorkflowDocument document, WorkflowDocument? stored)
    {
        foreach (var node in document.Nodes)
        {
            if (node.Config is not { } config)
                continue;
            var previous = stored?.Nodes.FirstOrDefault(n => n.Id == node.Id && n.Type == node.Type)?.Config;
            var moved = previous is not null && Targets.Any(t => ConfigPath.ToText(ConfigPath.Get(config, t)) != ConfigPath.ToText(ConfigPath.Get(previous, t)));
            foreach (var key in Keys(node.Type))
            {
                if (Text(ConfigPath.Get(config, key)) is not { } text)
                    continue;
                var old = Text(ConfigPath.Get(previous, key)) ?? "";
                if (moved && old.Length > 0 && (text == Masked || text == old))
                    throw new NodeValidationException([new NodeError("Enter this secret again because the target changed", node.Id, key)]);
                if (text.Length == 0 || text == Masked || text == old)
                    ConfigPath.Set(config, key, old.Length > 0 ? JsonValue.Create(old) : null);
                else
                    ConfigPath.Set(config, key, JsonValue.Create(encryption.Encrypt(text)));
            }
        }
    }

    public WorkflowDocument Mask(WorkflowDocument document)
    {
        foreach (var node in document.Nodes.Where(n => n.Config is not null))
        {
            foreach (var key in Keys(node.Type).Where(k => Text(ConfigPath.Get(node.Config, k)) is { Length: > 0 }))
                ConfigPath.Set(node.Config!, key, JsonValue.Create(Masked));
        }
        return document;
    }

    public JsonObject Reveal(WorkflowNodeDefinition definition, JsonObject config)
    {
        JsonObject? revealed = null;
        foreach (var field in definition.Fields.Where(IsSecret))
        {
            if (Text(ConfigPath.Get(config, field.Key)) is not { } text || !encryption.IsEncrypted(text))
                continue;
            revealed ??= config.DeepClone().AsObject();
            ConfigPath.Set(revealed, field.Key, JsonValue.Create(encryption.Decrypt(text)));
        }
        return revealed ?? config;
    }

    private static IEnumerable<string> Keys(string type) => WorkflowNodeCatalog.Catalog.Find(type)?.Fields.Where(IsSecret).Select(f => f.Key).Distinct() ?? [];

    private static bool IsSecret(NodeField field) => field.Kind == NodeFieldKinds.Password || field.Secret;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
