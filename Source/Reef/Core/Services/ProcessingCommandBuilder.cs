using System.Text.RegularExpressions;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

// Text values from the run are bound as parameters, numbers and configured column names are inlined
public static partial class ProcessingCommandBuilder
{
    public static (string Command, Dictionary<string, string> Parameters) Build(string connectionType, ProcessingConfig config, ProcessingContext context)
    {
        if (string.IsNullOrWhiteSpace(config.Command))
            throw new ArgumentException($"ProcessingConfig.Command is required for Type={config.Type}");

        var text = TextValues(context);
        var command = Inline(config.Command, context);
        if (LegacyPlaceholder().IsMatch(command))
        {
            Log.Warning("Processing command uses text placeholders such as '{{splitkey}}', these are sent as parameters, write @splitkey instead");
            command = LegacyPlaceholder().Replace(command, m => "@" + m.Groups["name"].Value.ToLowerInvariant());
        }

        var parameters = text
            .Where(v => Regex.IsMatch(command, $@"@{v.Key}\b", RegexOptions.IgnoreCase))
            .ToDictionary(v => "@" + v.Key, v => v.Value);
        foreach (var param in config.Parameters ?? [])
            parameters[param.Name.StartsWith('@') ? param.Name : "@" + param.Name] = Substitute(param.Value, context, text);

        return (Wrap(connectionType, config, command), parameters);
    }

    private static Dictionary<string, string> TextValues(ProcessingContext context) => new()
    {
        ["outputpath"] = context.OutputPath ?? "",
        ["outputformat"] = context.OutputFormat,
        ["triggeredby"] = context.TriggeredBy ?? "",
        ["startedat"] = context.StartedAt.ToString("O"),
        ["completedat"] = context.CompletedAt?.ToString("O") ?? "",
        ["status"] = context.Status,
        ["errormessage"] = context.ErrorMessage ?? "",
        ["splitkey"] = context.SplitKey ?? ""
    };

    private static string Inline(string input, ProcessingContext context) => input
        .Replace("{executionid}", context.ExecutionId.ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{profileid}", context.ProfileId.ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{rowcount}", context.RowCount.ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{filesizebytes}", (context.FileSizeBytes ?? 0).ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{executiontimems}", context.ExecutionTimeMs.ToString(), StringComparison.OrdinalIgnoreCase)
        .Replace("{deltasyncreefidcolumn}", context.DeltaSyncReefIdColumn ?? "", StringComparison.OrdinalIgnoreCase)
        .Replace("{splitkeycolumn}", context.SplitKeyColumn ?? "", StringComparison.OrdinalIgnoreCase);

    private static string Substitute(string input, ProcessingContext context, Dictionary<string, string> text) =>
        text.Aggregate(Inline(input, context), (value, v) => value.Replace($"{{{v.Key}}}", v.Value, StringComparison.OrdinalIgnoreCase));

    private static string Wrap(string connectionType, ProcessingConfig config, string command)
    {
        if (config.Type.Equals("Query", StringComparison.OrdinalIgnoreCase))
            return command;

        if (!config.Type.Equals("StoredProcedure", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown processing type: {config.Type}");

        var procedureName = command.Trim();
        var parameterList = config.Parameters is { Count: > 0 }
            ? " " + string.Join(", ", config.Parameters.Select(p => p.Name.StartsWith('@') || p.Name.StartsWith("p_") ? p.Name : $"@{p.Name}"))
            : "";

        return connectionType.ToLowerInvariant() switch
        {
            "mysql" or "postgresql" => $"CALL {procedureName}({parameterList.Trim()})",
            _ => $"EXEC {procedureName}{parameterList}"
        };
    }

    [GeneratedRegex(@"'?\{(?<name>outputpath|outputformat|triggeredby|startedat|completedat|status|errormessage|splitkey)\}'?", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyPlaceholder();
}
