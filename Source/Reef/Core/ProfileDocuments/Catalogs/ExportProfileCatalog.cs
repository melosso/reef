using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments.Catalogs;

public static class ExportProfileCatalog
{
    private static readonly string[] True = ["true"];
    private static readonly string[] False = ["false"];

    public static ProfileCatalog Build() => new()
    {
        Kind = ProfileKinds.Export,
        Settings =
        [
            new() { Key = "Name", Label = "Name", Required = true },
            new() { Key = "GroupId", Label = "Group", Kind = NodeFieldKinds.Select, OptionsSource = "groups" },
            new() { Key = "IsEnabled", Label = "Enabled", Kind = NodeFieldKinds.Bool }
        ],
        RequireOneOf = [["destination", "emailexport"]],
        Nodes =
        [
            new()
            {
                Type = "preprocess", Label = "Pre-process", Icon = "play", Rank = 10,
                Tooltip = "Run a statement or script before the query",
                Presence = new("PreProcessType"),
                Fields = [.. ProcessingNodeFields.For("PreProcess"),
                    new() { Key = "PreProcessRollbackOnFailure", Label = "Roll back when the query fails", Kind = NodeFieldKinds.Bool }],
                Normalize = c => ProcessingNodeFields.MirrorType(c, "PreProcess")
            },
            new()
            {
                Type = "source", Label = "Query", Icon = "database", Rank = 20, Required = true,
                Tooltip = "Read rows from a database connection",
                Fields =
                [
                    new() { Key = "ConnectionId", Label = "Connection", Kind = NodeFieldKinds.Select, OptionsSource = "connections", Required = true },
                    new() { Key = "Query", Label = "SQL query", Kind = NodeFieldKinds.Code, Required = true, Placeholder = "SELECT * FROM Orders WHERE ..." }
                ]
            },
            new()
            {
                Type = "deltasync", Label = "Smart Sync", Icon = "refresh-cw", Rank = 30,
                Tooltip = "Only pass rows that are new or changed since the last run",
                Presence = new("DeltaSyncEnabled", True),
                Sets = { ["DeltaSyncEnabled"] = true },
                Fields =
                [
                    new() { Key = "DeltaSyncReefIdColumn", Label = "Unique id column", Required = true, Format = NodeFieldFormats.Identifier,
                        Tooltip = "Column that identifies a row across runs" },
                    new() { Key = "DeltaSyncHashAlgorithm", Label = "Hash algorithm", Kind = NodeFieldKinds.Select,
                        Options = [new("SHA256", "SHA256"), new("SHA512", "SHA512"), new("MD5", "MD5")] },
                    new() { Key = "DeltaSyncDuplicateStrategy", Label = "Duplicate ids", Kind = NodeFieldKinds.Select,
                        Options = [new("Strict", "Fail the run"), new("CompositeHash", "Combine into one hash"), new("Skip", "Skip duplicates")] },
                    new() { Key = "DeltaSyncNullStrategy", Label = "Empty ids", Kind = NodeFieldKinds.Select,
                        Options = [new("Strict", "Fail the run"), new("GenerateHash", "Generate an id"), new("Skip", "Skip the row")] },
                    new() { Key = "DeltaSyncNumericPrecision", Label = "Decimal places compared", Kind = NodeFieldKinds.Number },
                    new() { Key = "DeltaSyncTrackDeletes", Label = "Track deleted rows", Kind = NodeFieldKinds.Bool },
                    new() { Key = "DeltaSyncRetentionDays", Label = "Keep history for days", Kind = NodeFieldKinds.Number },
                    new() { Key = "ExcludeReefIdFromOutput", Label = "Leave id column out of the output", Kind = NodeFieldKinds.Bool }
                ]
            },
            new()
            {
                Type = "splitoutput", Label = "Split", Icon = "split", Rank = 40,
                Tooltip = "Write one output per value of a column",
                Presence = new("SplitEnabled", True),
                Sets = { ["SplitEnabled"] = true },
                Excludes = ["emailexport"],
                Requires = ["destination"],
                Fields =
                [
                    new() { Key = "SplitKeyColumn", Label = "Split by column", Required = true },
                    new() { Key = "SplitFilenameTemplate", Label = "File name", Placeholder = "{profile}_{splitkey}_{timestamp}.{format}" },
                    new() { Key = "SplitBatchSize", Label = "Rows per file", Kind = NodeFieldKinds.Number, Tooltip = "1 writes one file per split value" },
                    new() { Key = "PostProcessPerSplit", Label = "Post-process every file", Kind = NodeFieldKinds.Bool },
                    new() { Key = "ExcludeSplitKeyFromOutput", Label = "Leave split column out of the output", Kind = NodeFieldKinds.Bool }
                ]
            },
            new()
            {
                Type = "template", Label = "Transform", Icon = "file-code", Rank = 50,
                Tooltip = "Shape the rows with a template, this replaces the plain output format",
                Presence = new("TemplateId"),
                Excludes = ["emailexport"],
                Fields =
                [
                    new() { Key = "TemplateId", Label = "Template", Kind = NodeFieldKinds.Select, OptionsSource = "templates", Required = true }
                ]
            },
            new()
            {
                Type = "destination", Label = "Destination", Icon = "hard-drive-upload", Rank = 60,
                Tooltip = "Write the output to a destination",
                Presence = new("IsEmailExport", False),
                Sets = { ["IsEmailExport"] = false },
                Excludes = ["emailexport"],
                Fields =
                [
                    new() { Key = "OutputDestinationId", Label = "Destination", Kind = NodeFieldKinds.Select, OptionsSource = "destinations", Required = true },
                    new() { Key = "OutputFormat", Label = "Format", Kind = NodeFieldKinds.Select, Required = true,
                        Options = [new("JSON", "JSON"), new("XML", "XML"), new("CSV", "CSV"), new("YAML", "YAML"), new("TOML", "TOML")] },
                    new() { Key = "FilenameTemplate", Label = "File name", Placeholder = "{profile}_{timestamp}.{format}" }
                ]
            },
            new()
            {
                Type = "emailexport", Label = "Send email", Icon = "mail", Rank = 60,
                Tooltip = "Send each row, or each group of rows, as an email",
                Presence = new("IsEmailExport", True),
                Sets = { ["IsEmailExport"] = true },
                Excludes = ["destination"],
                Fields =
                [
                    new() { Key = "OutputDestinationId", Label = "Mail server", Kind = NodeFieldKinds.Select, OptionsSource = "email-destinations", Required = true },
                    new() { Key = "EmailTemplateId", Label = "Email template", Kind = NodeFieldKinds.Select, OptionsSource = "templates", Required = true },
                    new() { Key = "UseHardcodedRecipients", Label = "Fixed recipient", Kind = NodeFieldKinds.Bool, Tooltip = "Off reads recipients from a column" },
                    new() { Key = "EmailRecipientsColumn", Label = "Recipient column", Required = true, ShowWhen = new("UseHardcodedRecipients", False) },
                    new() { Key = "EmailRecipientsHardcoded", Label = "Recipient", Required = true, Format = NodeFieldFormats.Email, ShowWhen = new("UseHardcodedRecipients", True) },
                    new() { Key = "UseHardcodedCc", Label = "Fixed CC", Kind = NodeFieldKinds.Bool },
                    new() { Key = "EmailCcColumn", Label = "CC column", ShowWhen = new("UseHardcodedCc", False) },
                    new() { Key = "EmailCcHardcoded", Label = "CC", Format = NodeFieldFormats.Email, ShowWhen = new("UseHardcodedCc", True) },
                    new() { Key = "UseHardcodedSubject", Label = "Fixed subject", Kind = NodeFieldKinds.Bool },
                    new() { Key = "EmailSubjectColumn", Label = "Subject column", ShowWhen = new("UseHardcodedSubject", False) },
                    new() { Key = "EmailSubjectHardcoded", Label = "Subject", ShowWhen = new("UseHardcodedSubject", True) },
                    new() { Key = "EmailGroupBySplitKey", Label = "One email per group", Kind = NodeFieldKinds.Bool },
                    new() { Key = "SplitKeyColumn", Label = "Group by column", Required = true, ShowWhen = new("EmailGroupBySplitKey", True) },
                    new() { Key = "EmailSuccessThresholdPercent", Label = "Success threshold in percent", Kind = NodeFieldKinds.Number,
                        Tooltip = "The run succeeds when at least this share of emails is sent" },
                    new() { Key = "EmailApprovalRequired", Label = "Hold for approval", Kind = NodeFieldKinds.Bool },
                    new() { Key = "EmailApprovalRoles", Label = "Approver roles", Kind = NodeFieldKinds.Json, Placeholder = "[\"Admin\"]",
                        ShowWhen = new("EmailApprovalRequired", True) },
                    new() { Key = "EmailAttachmentConfig", Label = "Attachments", Kind = NodeFieldKinds.Json,
                        Tooltip = "Attachment settings as JSON, leave empty to send without attachments" }
                ]
            },
            new()
            {
                Type = "postprocess", Label = "Post-process", Icon = "check-circle", Rank = 70,
                Tooltip = "Run a statement or script after the output is written",
                Presence = new("PostProcessType"),
                Fields = [.. ProcessingNodeFields.For("PostProcess"),
                    new() { Key = "PostProcessSkipOnFailure", Label = "Skip when the export fails", Kind = NodeFieldKinds.Bool },
                    new() { Key = "PostProcessRollbackOnFailure", Label = "Roll back on its own failure", Kind = NodeFieldKinds.Bool },
                    new() { Key = "PostProcessOnZeroRows", Label = "Run when the query returns no rows", Kind = NodeFieldKinds.Bool }],
                Normalize = c => ProcessingNodeFields.MirrorType(c, "PostProcess")
            }
        ]
    };
}
