using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Reef.Core.Workflows.Engine;

public static partial class WorkflowExpressions
{
    private static readonly HashSet<string> TextKinds = [NodeFieldKinds.Text, NodeFieldKinds.Code, NodeFieldKinds.Textarea];
    private static readonly LexerOptions ScriptOnly = new() { Mode = ScriptMode.ScriptOnly };

    // Selects stay literal for privilege guard
    public static JsonObject Resolve(WorkflowNodeDefinition definition, JsonObject config, ExpressionScope scope)
    {
        JsonObject? resolved = null;
        foreach (var field in definition.Fields.Where(f => TextKinds.Contains(f.Kind) && !definition.SqlFields.Contains(f.Key) && !definition.RawFields.Contains(f.Key)))
        {
            if (ConfigPath.Get(config, field.Key) is not JsonValue value || value.GetValueKind() != JsonValueKind.String || value.GetValue<string>() is not { } text || !text.Contains("{{"))
                continue;
            resolved ??= config.DeepClone().AsObject();
            ConfigPath.Set(resolved, field.Key, JsonValue.Create(Run(Template.Parse(text), field.Label, scope, (t, c) => t.Render(c))));
        }
        return resolved ?? config;
    }

    public static (string Sql, Dictionary<string, string> Parameters) Bind(string sql, ExpressionScope scope, string label)
    {
        var parameters = new Dictionary<string, string>();
        var bound = Block().Replace(sql, match =>
        {
            var name = $"@reef_p{parameters.Count}";
            var value = Run(Template.Parse(match.Groups[1].Value, lexerOptions: ScriptOnly), label, scope, (t, c) => t.Evaluate(c));
            parameters[name] = Text(value)!;
            return name;
        });
        return (bound, parameters);
    }

    // Null binds as sql null
    private static string? Text(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static T Run<T>(Template template, string label, ExpressionScope scope, Func<Template, TemplateContext, T> run)
    {
        if (template.HasErrors)
            throw new NodeExecutionException($"Expression in {label} is invalid: {string.Join("; ", template.Messages.Select(m => m.Message))}");
        try
        {
            return run(template, Context(scope));
        }
        catch (ScriptRuntimeException ex)
        {
            throw new NodeExecutionException($"Expression in {label} failed: {ex.OriginalMessage}");
        }
    }

    private static TemplateContext Context(ExpressionScope scope)
    {
        var nodes = new ScriptObject();
        foreach (var (id, outputs) in scope.Nodes)
        {
            var items = (outputs.GetValueOrDefault(WorkflowPorts.Main) ?? outputs.Values.FirstOrDefault(o => o.Items.Count > 0) ?? new WorkflowItems()).Items;
            nodes[id] = new ScriptObject { ["first"] = items.FirstOrDefault(), ["last"] = items.LastOrDefault(), ["items"] = items };
        }
        var globals = new ScriptObject
        {
            ["json"] = scope.Item,
            ["item_index"] = scope.ItemIndex,
            ["run_index"] = scope.RunIndex,
            ["now"] = DateTime.UtcNow,
            ["node"] = nodes,
            ["workflow"] = new ScriptObject { ["id"] = scope.Workflow.Id, ["name"] = scope.Workflow.Name, ["execution_id"] = scope.ExecutionId }
        };
        var context = new TemplateContext { StrictVariables = true, EnableRelaxedMemberAccess = true, LoopLimit = 10_000, RecursiveLimit = 50, LimitToString = 1_000_000, RegexTimeOut = TimeSpan.FromSeconds(1) };
        context.PushGlobal(globals);
        return context;
    }

    [GeneratedRegex(@"\{\{(.+?)\}\}", RegexOptions.Singleline)]
    private static partial Regex Block();
}
