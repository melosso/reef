using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments.Catalogs;

// shared fields for pre and post processing nodes, prefix is PreProcess or PostProcess
public static class ProcessingNodeFields
{
    private static readonly string[] SqlTypes = ["Query", "StoredProcedure"];
    private static readonly string[] ScriptType = ["Script"];

    public static List<NodeField> For(string prefix)
    {
        var type = $"{prefix}Type";
        var config = $"{prefix}Config";
        return
        [
            new() { Key = type, Label = "Runs", Kind = NodeFieldKinds.Select, Required = true, Options =
            [
                new("Query", "SQL statement"),
                new("StoredProcedure", "Stored procedure"),
                new("Script", "Script")
            ] },
            new() { Key = $"{config}.Command", Label = "Command", Kind = NodeFieldKinds.Code, Required = true,
                Placeholder = "UPDATE Items SET Status = 'Processing' WHERE Id = {executionid}",
                Tooltip = "SQL statement or stored procedure name, {executionid} and {rowcount} are replaced at run time",
                ShowWhen = new(type, SqlTypes) },
            new() { Key = $"{config}.Interpreter", Label = "Interpreter", Kind = NodeFieldKinds.Select, Required = true,
                ShowWhen = new(type, ScriptType), Options =
            [
                new("pwsh", "PowerShell"), new("python", "Python"), new("node", "Node.js"),
                new("bash", "Bash"), new("sh", "sh"), new("cmd", "cmd")
            ] },
            new() { Key = $"{config}.ScriptIsInline", Label = "Inline script", Kind = NodeFieldKinds.Bool,
                Tooltip = "Off runs the script at the given path", ShowWhen = new(type, ScriptType), Default = true },
            new() { Key = $"{config}.ScriptPathOrInline", Label = "Script", Kind = NodeFieldKinds.Code, Required = true,
                ShowWhen = new(type, ScriptType) },
            new() { Key = $"{config}.EnvAllowlist", Label = "Environment variables", Kind = NodeFieldKinds.List,
                Tooltip = "Comma separated names passed through to the script", ShowWhen = new(type, ScriptType) },
            new() { Key = $"{config}.Timeout", Label = "Timeout in seconds", Kind = NodeFieldKinds.Number, Default = 30 },
            new() { Key = $"{config}.ContinueOnError", Label = "Continue on error", Kind = NodeFieldKinds.Bool,
                Tooltip = "A failure here does not stop the profile" },
            new() { Key = $"{config}.Type", Label = "Type", Hidden = true }
        ];
    }

    public static void MirrorType(JsonObject config, string prefix)
    {
        var type = ConfigPath.Get(config, $"{prefix}Type")?.DeepClone();
        ConfigPath.Set(config, $"{prefix}Config.Type", type);
    }
}
