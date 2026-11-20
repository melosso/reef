using System.Text.Json.Nodes;
using Reef.Core.Models;
using Reef.Core.ProfileDocuments;
using Reef.Core.ProfileDocuments.Catalogs;
using Reef.Core.ProfileDocuments.Models;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Catalogs;

// Reuses profile step config keys
public static class WorkflowNodeCatalog
{
    public const string Fallback = "fallback";

    private static readonly string[] Cron = [nameof(ScheduleType.Cron)];
    private static readonly string[] Interval = [nameof(ScheduleType.Interval)];
    private static readonly string[] Join = ["join"];
    private static readonly string[] WithValue = ["equals", "notEquals", "contains", "greaterThan", "lessThan"];

    public static readonly WorkflowCatalog Catalog = Build();

    private static WorkflowCatalog Build()
    {
        var export = ProfileCatalogs.Find(ProfileKinds.Export)!;
        var import = ProfileCatalogs.Find(ProfileKinds.Import)!;

        return new()
        {
            Nodes =
            [
                Trigger("manual", "Manual", "mouse-pointer-click", "Start the workflow from the editor, the API or Ctrl K", []),
                Trigger("schedule", "Schedule", "clock", "Start the workflow on a cron expression or an interval",
                [
                    new() { Key = "ScheduleType", Label = "Repeat", Kind = NodeFieldKinds.Select, Required = true, Default = nameof(ScheduleType.Cron),
                        Options = [new(nameof(ScheduleType.Cron), "Cron expression"), new(nameof(ScheduleType.Interval), "Interval")] },
                    new() { Key = "CronExpression", Label = "Cron expression", Required = true, Placeholder = "0 6 * * *", ShowWhen = new("ScheduleType", Cron) },
                    new() { Key = "IntervalMinutes", Label = "Every minutes", Kind = NodeFieldKinds.Number, Required = true, ShowWhen = new("ScheduleType", Interval) }
                ]),
                Trigger("webhook", "Webhook", "webhook", "Start the workflow when a request reaches its webhook URL", []),

                From(export, "source", "query", WorkflowNodeGroups.Sources),
                From(import, "source", "fetch", WorkflowNodeGroups.Sources),

                From(import, "parse", "parse", WorkflowNodeGroups.Transform),
                From(import, "mapping", "mapping", WorkflowNodeGroups.Transform),
                Union(export, import, "deltasync", WorkflowNodeGroups.Transform),
                From(export, "splitoutput", "split", WorkflowNodeGroups.Transform),
                From(export, "template", "template", WorkflowNodeGroups.Transform),

                new()
                {
                    Type = "if", Label = "If", Icon = "git-branch", Group = WorkflowNodeGroups.Flow,
                    Tooltip = "Send each row to true or false by a column value",
                    Outputs = ["true", "false"],
                    Fields =
                    [
                        new() { Key = "Column", Label = "Column", Required = true },
                        new() { Key = "Operator", Label = "Operator", Kind = NodeFieldKinds.Select, Required = true, Default = "equals", Options =
                        [
                            new("equals", "Equals"), new("notEquals", "Does not equal"), new("contains", "Contains"),
                            new("greaterThan", "Greater than"), new("lessThan", "Less than"), new("isEmpty", "Is empty"), new("isNotEmpty", "Is not empty")
                        ] },
                        new() { Key = "Value", Label = "Value", ShowWhen = new("Operator", WithValue) }
                    ]
                },
                new()
                {
                    Type = "switch", Label = "Switch", Icon = "split", Group = WorkflowNodeGroups.Flow,
                    Tooltip = "Send each row to the output that matches its column value, other rows go to fallback",
                    Outputs = [Fallback],
                    OutputsFor = SwitchOutputs,
                    Fields =
                    [
                        new() { Key = "Column", Label = "Column", Required = true },
                        new() { Key = "Cases", Label = "Cases", Kind = NodeFieldKinds.Table, Required = true,
                            Columns = [new() { Key = "Value", Label = "Value", Required = true }] }
                    ]
                },
                new()
                {
                    Type = "merge", Label = "Merge", Icon = "merge", Group = WorkflowNodeGroups.Flow,
                    Tooltip = "Combine two branches by appending rows or joining on a column",
                    Inputs = ["input1", "input2"],
                    Fields =
                    [
                        new() { Key = "Mode", Label = "Combine", Kind = NodeFieldKinds.Select, Required = true, Default = "append",
                            Options = [new("append", "Append rows"), new("join", "Join on a column")] },
                        new() { Key = "JoinColumn", Label = "Join column", Required = true, ShowWhen = new("Mode", Join) }
                    ]
                },

                From(export, "destination", "destination", WorkflowNodeGroups.Deliver),
                From(export, "emailexport", "email", WorkflowNodeGroups.Deliver),
                From(import, "target", "write", WorkflowNodeGroups.Deliver),

                Union(export, import, "preprocess", WorkflowNodeGroups.Hooks),
                Union(export, import, "postprocess", WorkflowNodeGroups.Hooks)
            ]
        };
    }

    private static WorkflowNodeDefinition Trigger(string type, string label, string icon, string tooltip, List<NodeField> fields) =>
        new() { Type = type, Label = label, Icon = icon, Tooltip = tooltip, Group = WorkflowNodeGroups.Triggers, Inputs = WorkflowPorts.None, Fields = fields };

    private static WorkflowNodeDefinition From(ProfileCatalog catalog, string step, string type, string group) =>
        From(catalog.Nodes.Single(n => n.Type == step), type, group, catalog.Nodes.Single(n => n.Type == step).Fields);

    private static WorkflowNodeDefinition Union(ProfileCatalog first, ProfileCatalog second, string step, string group)
    {
        var primary = first.Nodes.Single(n => n.Type == step);
        var fields = primary.Fields.Concat(second.Nodes.Single(n => n.Type == step).Fields).DistinctBy(f => f.Key).ToList();
        return From(primary, step, group, fields);
    }

    private static WorkflowNodeDefinition From(NodeDefinition step, string type, string group, List<NodeField> fields) =>
        new() { Type = type, Label = step.Label, Icon = step.Icon, Tooltip = step.Tooltip, Group = group, Fields = fields };

    private static string[] SwitchOutputs(JsonObject config) =>
        [.. (config["Cases"] as JsonArray ?? []).OfType<JsonObject>().Select(c => ConfigPath.ToText(c["Value"])).Where(v => v.Length > 0).Distinct(), Fallback];
}
