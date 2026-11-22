using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Models;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Conversion;

public static class ProfileWorkflowConverter
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] Formats = ["JSON", "XML", "CSV", "YAML"];

    public static WorkflowConversion FromExport(Profile profile)
    {
        var chain = new WorkflowChain(profile.Name);
        var notes = new List<string>();

        if (Command(profile.PreProcessType, profile.PreProcessConfig, profile.ConnectionId, skipWhenEmpty: false) is { } pre)
            chain.Then("pre", "command", pre);
        chain.Then("query", "query", Config(("ConnectionId", profile.ConnectionId), ("Query", profile.Query)));

        if (profile.DeltaSyncEnabled)
            chain.Then("deltasync", "deltasync", Config(
                ("IdColumn", profile.DeltaSyncReefIdColumn), ("HashAlgorithm", profile.DeltaSyncHashAlgorithm),
                ("DuplicateIds", profile.DeltaSyncDuplicateStrategy), ("EmptyIds", profile.DeltaSyncNullStrategy),
                ("DecimalPlaces", profile.DeltaSyncNumericPrecision), ("TrackDeletes", profile.DeltaSyncTrackDeletes),
                ("RetentionDays", profile.DeltaSyncRetentionDays), ("ExcludeIdColumn", profile.ExcludeReefIdFromOutput),
                ("IdNormalization", profile.DeltaSyncReefIdNormalization), ("RemoveNonPrintable", profile.DeltaSyncRemoveNonPrintable),
                ("ResetOnSchemaChange", profile.DeltaSyncResetOnSchemaChange)));

        if (profile.IsEmailExport)
            chain.Then("email", "email", Config(
                ("DestinationId", profile.OutputDestinationId), ("TemplateId", profile.EmailTemplateId),
                ("RecipientsFrom", profile.UseHardcodedRecipients ? "Fixed" : "Column"),
                ("RecipientsColumn", profile.EmailRecipientsColumn), ("Recipients", profile.EmailRecipientsHardcoded),
                ("CcColumn", profile.UseHardcodedCc ? null : profile.EmailCcColumn), ("Cc", profile.UseHardcodedCc ? profile.EmailCcHardcoded : null),
                ("SubjectColumn", profile.UseHardcodedSubject ? null : profile.EmailSubjectColumn), ("Subject", profile.EmailSubjectHardcoded),
                ("GroupByColumn", profile.EmailGroupBySplitKey ? profile.SplitKeyColumn : null),
                ("SuccessPercent", profile.EmailSuccessThresholdPercent), ("RequireApproval", profile.EmailApprovalRequired),
                ("ApproverRoles", profile.EmailApprovalRoles), ("EmailAttachmentConfig", profile.EmailAttachmentConfig)));
        else
        {
            if (profile.SplitEnabled)
                chain.Then("split", "split", Config(("Column", profile.SplitKeyColumn), ("FileName", FileName(profile.SplitFilenameTemplate)),
                    ("ExcludeColumn", profile.ExcludeSplitKeyFromOutput)));
            if (profile.TemplateId is { } templateId)
                chain.Then("template", "template", Config(("TemplateId", templateId), ("Options", profile.TransformationOptionsJson)));

            if (profile.OutputDestinationId is null)
                notes.Add("The profile writes to an inline destination, add a destination step to the workflow");
            else
            {
                var format = profile.TemplateId is not null ? "Files" : profile.OutputFormat.ToUpperInvariant();
                if (!Formats.Contains(format) && format != "Files")
                {
                    notes.Add($"Output format {profile.OutputFormat} became JSON");
                    format = "JSON";
                }
                chain.Then("destination", "destination", Config(
                    ("DestinationId", profile.OutputDestinationId), ("EndpointId", profile.OutputDestinationEndpointId), ("Format", format),
                    ("FileName", profile.SplitEnabled && profile.TemplateId is not null ? null : FileName(profile.FilenameTemplate)),
                    ("RemoveOnFailure", profile.PostProcessRollbackOnFailure)));
            }
        }

        if (Command(profile.PostProcessType, profile.PostProcessConfig, profile.ConnectionId, skipWhenEmpty: !profile.PostProcessOnZeroRows) is { } post)
        {
            chain.Then("post", "command", post);
            if (!profile.PostProcessSkipOnFailure)
                notes.Add("The post process step runs only when the steps before it succeed");
            if (profile.SplitEnabled && profile.PostProcessPerSplit)
                notes.Add("The post process step runs once, not for every split file");
        }
        if (!string.IsNullOrWhiteSpace(profile.NotificationConfig))
            notes.Add("Profile notifications are not part of the workflow");
        return new(chain.Document, notes);
    }

    public static WorkflowConversion FromImport(ImportProfile profile, List<ImportOutputTarget> copies)
    {
        var chain = new WorkflowChain(profile.Name);
        var notes = new List<string>();
        var binary = profile.SourceFormat.Equals("Binary", StringComparison.OrdinalIgnoreCase);

        if (Command(profile.PreProcessType, profile.PreProcessConfig, profile.TargetConnectionId, skipWhenEmpty: false) is { } pre)
            chain.Then("pre", "command", pre);
        chain.Then("fetch", "fetch", Config(
            ("From", profile.SourceType), ("Settings", Parse(profile.SourceConfig)), ("Path", profile.SourceFilePath),
            ("Pattern", profile.SourceFilePattern), ("Pick", profile.SourceFileSelection), ("Archive", profile.ArchiveAfterImport),
            ("ArchiveFolder", profile.ArchivePath), ("Method", profile.HttpMethod), ("Body", profile.HttpBodyTemplate),
            ("RecordsPath", profile.HttpDataRootPath), ("Paginate", profile.HttpPaginationEnabled), ("Pagination", profile.HttpPaginationConfig)));
        if (profile.SourceDestinationId is not null)
            notes.Add("The profile reads from a saved destination, set its connection details on the fetch step");

        if (binary)
            notes.Add("Binary files are passed to the export copies without parsing");
        else
        {
            chain.Then("parse", "parse", Config(("Format", profile.SourceFormat), ("Options", Parse(profile.FormatConfig))));
            if (Parse(profile.ColumnMappingsJson) is JsonArray { Count: > 0 } columns)
                chain.Then("mapping", "mapping", Config(("Columns", columns), ("AutoMap", profile.AutoMapColumns && !profile.SkipUnmappedColumns)));
            if (profile.DeltaSyncEnabled)
            {
                chain.Then("deltasync", "deltasync", Config(("IdColumn", profile.DeltaSyncReefIdColumn), ("HashAlgorithm", profile.DeltaSyncHashAlgorithm),
                    ("TrackDeletes", profile.DeltaSyncTrackDeletes), ("RetentionDays", profile.DeltaSyncRetentionDays), ("ExcludeIdColumn", false)));
                notes.Add("Smart Sync history of the import starts over, the first run writes every row again");
                if (profile.DeltaSyncTrackDeletes)
                    notes.Add("Deleted rows are passed on with _ReefDeleted, the target table is not changed for them");
            }
            var local = profile.TargetType.Equals("LocalFile", StringComparison.OrdinalIgnoreCase);
            chain.Then("write", "write", Config(
                ("To", local ? "LocalFile" : "Database"), ("ConnectionId", local ? null : profile.TargetConnectionId), ("Table", local ? null : profile.TargetTable),
                ("Load", profile.LoadStrategy), ("KeyColumns", profile.UpsertKeyColumns), ("BatchSize", profile.BatchSize),
                ("TimeoutSeconds", profile.CommandTimeoutSeconds), ("FilePath", profile.LocalTargetPath), ("FileFormat", profile.LocalTargetFormat),
                ("ExistingFile", profile.LocalTargetWriteMode)));
        }

        var number = 0;
        foreach (var copy in copies.Where(c => c.IsEnabled).OrderBy(c => c.SortOrder))
            chain.Then($"copy{++number}", "destination", Config(
                ("DestinationId", copy.DestinationId), ("EndpointId", copy.DestinationEndpointId),
                ("Format", binary ? "Files" : Formats.Contains(copy.OutputFormat.ToUpperInvariant()) ? copy.OutputFormat.ToUpperInvariant() : "JSON"),
                ("FileName", FileName(copy.FilenameTemplate)), ("ContinueOnError", !copy.OnFailure.Equals("Fail", StringComparison.OrdinalIgnoreCase))));

        if (Command(profile.PostProcessType, profile.PostProcessConfig, profile.TargetConnectionId, skipWhenEmpty: false) is { } post)
            chain.Then("post", "command", post);
        if (profile.OnSourceFailure != "Fail" || profile.MaxFailedRowsBeforeAbort > 0 || profile.MaxFailedRowsPercent > 0)
            notes.Add("Retry and failed row limits of the profile are not part of the workflow");
        if (!string.IsNullOrWhiteSpace(profile.NotificationConfig))
            notes.Add("Profile notifications are not part of the workflow");
        return new(chain.Document, notes);
    }

    private static JsonObject? Command(string? type, string? json, int? connectionId, bool skipWhenEmpty)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(json))
            return null;
        var config = JsonSerializer.Deserialize<ProcessingConfig>(json, CaseInsensitive) ?? new ProcessingConfig { Type = type };
        return Config(
            ("Runs", type), ("ConnectionId", type == "Script" ? null : connectionId), ("Command", config.Command),
            ("Interpreter", config.Interpreter), ("InlineScript", config.ScriptIsInline), ("Script", config.ScriptPathOrInline),
            ("EnvironmentVariables", config.EnvAllowlist is { Count: > 0 } names ? new JsonArray([.. names.Select(n => JsonValue.Create(n))]) : null),
            ("TimeoutSeconds", config.Timeout), ("ContinueOnError", config.ContinueOnError), ("SkipWhenEmpty", skipWhenEmpty));
    }

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonObject Config(params (string Key, object? Value)[] values)
    {
        var config = new JsonObject();
        foreach (var (key, value) in values)
        {
            if (value is null or "")
                continue;
            config[key] = value as JsonNode ?? JsonValue.Create(value);
        }
        return config;
    }

    private static string? FileName(string? template) => template?.Replace("{profile}", "{workflow}", StringComparison.OrdinalIgnoreCase);
}
