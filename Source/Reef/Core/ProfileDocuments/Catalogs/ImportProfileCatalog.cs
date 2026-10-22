using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments.Catalogs;

public static class ImportProfileCatalog
{
    private static readonly string[] True = ["true"];
    private static readonly string[] Local = ["Local"];
    private static readonly string[] Sftp = ["Sftp"];
    private static readonly string[] Http = ["Http"];
    private static readonly string[] S3 = ["S3"];
    private static readonly string[] Azure = ["AzureBlob"];
    private static readonly string[] Share = ["NetworkShare"];
    private static readonly string[] Files = ["Local", "Sftp", "S3", "AzureBlob", "NetworkShare"];
    private static readonly string[] Delimited = ["CSV", "TSV"];
    private static readonly string[] Json = ["JSON", "JSONL"];
    private static readonly string[] Database = ["Database"];
    private static readonly string[] LocalFile = ["LocalFile"];

    public static ProfileCatalog Build() => new()
    {
        Kind = ProfileKinds.Import,
        Settings =
        [
            new() { Key = "Name", Label = "Name", Required = true },
            new() { Key = "GroupId", Label = "Group", Kind = NodeFieldKinds.Select, OptionsSource = "groups" },
            new() { Key = "IsEnabled", Label = "Enabled", Kind = NodeFieldKinds.Bool },
            new() { Key = "OnSourceFailure", Label = "When the source fails", Kind = NodeFieldKinds.Select,
                Options = [new("Fail", "Stop the run"), new("Skip", "Continue without data"), new("Retry", "Retry")] },
            new() { Key = "RetryCount", Label = "Retries", Kind = NodeFieldKinds.Number, ShowWhen = new("OnSourceFailure", ["Retry"]) },
            new() { Key = "OnParseFailure", Label = "When a row cannot be read", Kind = NodeFieldKinds.Select,
                Options = [new("SkipRow", "Skip the row"), new("Fail", "Stop the run"), new("SkipFile", "Skip the file")] },
            new() { Key = "OnRowFailure", Label = "When a row cannot be written", Kind = NodeFieldKinds.Select,
                Options = [new("SkipRow", "Skip the row"), new("Fail", "Stop the run"), new("Rollback", "Roll back everything")] },
            new() { Key = "OnConstraintViolation", Label = "When a constraint is violated", Kind = NodeFieldKinds.Select,
                Options = [new("SkipRow", "Skip the row"), new("Fail", "Stop the run"), new("Overwrite", "Overwrite")] },
            new() { Key = "MaxFailedRowsBeforeAbort", Label = "Stop after failed rows", Kind = NodeFieldKinds.Number, Tooltip = "0 never stops" },
            new() { Key = "MaxFailedRowsPercent", Label = "Stop after failed rows in percent", Kind = NodeFieldKinds.Number, Tooltip = "0 never stops" },
            new() { Key = "RollbackOnAbort", Label = "Roll back when stopped", Kind = NodeFieldKinds.Bool }
        ],
        Nodes =
        [
            new()
            {
                Type = "preprocess", Label = "Pre-process", Icon = "play", Rank = 10,
                Tooltip = "Run a statement or script before the import",
                Presence = new("PreProcessType"),
                Fields = [.. ProcessingNodeFields.For("PreProcess"),
                    new() { Key = "PreProcessRollbackOnFailure", Label = "Roll back when the import fails", Kind = NodeFieldKinds.Bool }],
                Normalize = c => ProcessingNodeFields.MirrorType(c, "PreProcess")
            },
            new()
            {
                Type = "source", Label = "Fetch", Icon = "download", Rank = 20, Required = true,
                Tooltip = "Read a file or an API response",
                Fields =
                [
                    new() { Key = "SourceType", Label = "From", Kind = NodeFieldKinds.Select, Required = true, Options =
                    [
                        new("Local", "Local file"), new("Sftp", "SFTP"), new("Http", "HTTP API"),
                        new("S3", "S3"), new("AzureBlob", "Azure Blob"), new("NetworkShare", "Network share")
                    ] },
                    new() { Key = "SourceFilePath", Label = "Folder or file", Required = true, ShowWhen = new("SourceType", Local) },
                    new() { Key = "SourceConfig.host", Label = "Host", Required = true, ShowWhen = new("SourceType", Sftp) },
                    new() { Key = "SourceConfig.port", Label = "Port", Kind = NodeFieldKinds.Number, ShowWhen = new("SourceType", Sftp), Default = 22 },
                    new() { Key = "SourceConfig.username", Label = "User", ShowWhen = new("SourceType", Sftp) },
                    new() { Key = "SourceConfig.authType", Label = "Sign in with", Kind = NodeFieldKinds.Select, ShowWhen = new("SourceType", Sftp),
                        Options = [new("password", "Password"), new("privatekey", "Private key")] },
                    new() { Key = "SourceConfig.password", Label = "Password", Kind = NodeFieldKinds.Password, ShowWhen = new("SourceConfig.authType", ["password"]) },
                    new() { Key = "SourceConfig.privateKey", Label = "Private key", Kind = NodeFieldKinds.Textarea, ShowWhen = new("SourceConfig.authType", ["privatekey"]) },
                    new() { Key = "SourceConfig.privateKeyPassphrase", Label = "Key passphrase", Kind = NodeFieldKinds.Password, ShowWhen = new("SourceConfig.authType", ["privatekey"]) },
                    new() { Key = "SourceConfig.path", Label = "Remote folder", ShowWhen = new("SourceType", Sftp) },
                    new() { Key = "SourceConfig.url", Label = "URL", Required = true, ShowWhen = new("SourceType", Http) },
                    new() { Key = "HttpMethod", Label = "Method", Kind = NodeFieldKinds.Select, ShowWhen = new("SourceType", Http),
                        Options = [new("GET", "GET"), new("POST", "POST")] },
                    new() { Key = "SourceConfig.authType", Label = "Authentication", Kind = NodeFieldKinds.Select, ShowWhen = new("SourceType", Http),
                        Options = [new("", "None"), new("Bearer", "Bearer token"), new("Basic", "Basic"), new("ApiKey", "API key")] },
                    new() { Key = "SourceConfig.authToken", Label = "Token or credentials", Kind = NodeFieldKinds.Password, ShowWhen = new("SourceConfig.authType", ["Bearer", "Basic", "ApiKey"]) },
                    new() { Key = "SourceConfig.apiKeyHeader", Label = "API key header", Placeholder = "X-Api-Key", ShowWhen = new("SourceConfig.authType", ["ApiKey"]) },
                    new() { Key = "SourceConfig.headers", Label = "Headers", Kind = NodeFieldKinds.Json, ShowWhen = new("SourceType", Http) },
                    new() { Key = "HttpBodyTemplate", Label = "Body", Kind = NodeFieldKinds.Code, ShowWhen = new("HttpMethod", ["POST"]) },
                    new() { Key = "HttpDataRootPath", Label = "Records at path", Placeholder = "data.items", ShowWhen = new("SourceType", Http) },
                    new() { Key = "HttpPaginationEnabled", Label = "Paginate", Kind = NodeFieldKinds.Bool, ShowWhen = new("SourceType", Http) },
                    new() { Key = "HttpPaginationConfig", Label = "Pagination", Kind = NodeFieldKinds.Json, ShowWhen = new("HttpPaginationEnabled", True) },
                    new() { Key = "SourceConfig.bucketName", Label = "Bucket", Required = true, ShowWhen = new("SourceType", S3) },
                    new() { Key = "SourceConfig.region", Label = "Region", ShowWhen = new("SourceType", S3) },
                    new() { Key = "SourceConfig.prefix", Label = "Prefix", ShowWhen = new("SourceType", S3) },
                    new() { Key = "SourceConfig.serviceUrl", Label = "Service URL", Tooltip = "For S3 compatible storage", ShowWhen = new("SourceType", S3) },
                    new() { Key = "SourceConfig.accessKey", Label = "Access key", ShowWhen = new("SourceType", S3) },
                    new() { Key = "SourceConfig.secretKey", Label = "Secret key", Kind = NodeFieldKinds.Password, ShowWhen = new("SourceType", S3) },
                    new() { Key = "SourceConfig.container", Label = "Container", Required = true, ShowWhen = new("SourceType", Azure) },
                    new() { Key = "SourceConfig.connectionString", Label = "Connection string", Kind = NodeFieldKinds.Password, ShowWhen = new("SourceType", Azure) },
                    new() { Key = "SourceConfig.uncPath", Label = "UNC path", Required = true, Placeholder = @"\\server\share", ShowWhen = new("SourceType", Share) },
                    new() { Key = "SourceConfig.subFolder", Label = "Sub folder", ShowWhen = new("SourceType", Share) },
                    new() { Key = "SourceConfig.domain", Label = "Domain", ShowWhen = new("SourceType", Share) },
                    new() { Key = "SourceFilePattern", Label = "File pattern", Placeholder = "*.csv", ShowWhen = new("SourceType", Files) },
                    new() { Key = "SourceFileSelection", Label = "Pick", Kind = NodeFieldKinds.Select, ShowWhen = new("SourceType", Files),
                        Options = [new("Latest", "Latest file"), new("Oldest", "Oldest file"), new("All", "All matching files"), new("Exact", "Exact path")] },
                    new() { Key = "ArchiveAfterImport", Label = "Archive after import", Kind = NodeFieldKinds.Bool, ShowWhen = new("SourceType", Files) },
                    new() { Key = "ArchivePath", Label = "Archive folder", ShowWhen = new("ArchiveAfterImport", True) }
                ]
            },
            new()
            {
                Type = "parse", Label = "Parse", Icon = "file-text", Rank = 30, Required = true,
                Tooltip = "Read records out of the fetched content",
                Fields =
                [
                    new() { Key = "SourceFormat", Label = "Format", Kind = NodeFieldKinds.Select, Required = true, Options =
                    [
                        new("CSV", "CSV"), new("TSV", "TSV"), new("JSON", "JSON"), new("JSONL", "JSON Lines"), new("XML", "XML"), new("YAML", "YAML")
                    ] },
                    new() { Key = "FormatConfig.delimiter", Label = "Delimiter", Placeholder = ",", ShowWhen = new("SourceFormat", ["CSV"]) },
                    new() { Key = "FormatConfig.quoteChar", Label = "Quote", Placeholder = "\"", ShowWhen = new("SourceFormat", Delimited) },
                    new() { Key = "FormatConfig.encoding", Label = "Encoding", Kind = NodeFieldKinds.Select, ShowWhen = new("SourceFormat", Delimited), Default = "UTF-8",
                        Options = [new("UTF-8", "UTF-8"), new("UTF-8-BOM", "UTF-8 with BOM"), new("Latin1", "Latin-1"), new("Windows1252", "Windows-1252")] },
                    new() { Key = "FormatConfig.hasHeader", Label = "First row is a header", Kind = NodeFieldKinds.Bool, ShowWhen = new("SourceFormat", Delimited), Default = true },
                    new() { Key = "FormatConfig.trimWhitespace", Label = "Trim whitespace", Kind = NodeFieldKinds.Bool, ShowWhen = new("SourceFormat", Delimited) },
                    new() { Key = "FormatConfig.skipRows", Label = "Skip leading rows", Kind = NodeFieldKinds.Number, ShowWhen = new("SourceFormat", Delimited) },
                    new() { Key = "FormatConfig.nullValue", Label = "Treat as empty", Placeholder = "NULL", ShowWhen = new("SourceFormat", Delimited) },
                    new() { Key = "FormatConfig.dataRootPath", Label = "Records at path", Placeholder = "data.items", ShowWhen = new("SourceFormat", Json) },
                    new() { Key = "FormatConfig.recordElement", Label = "Record element", Required = true, Placeholder = "Order", ShowWhen = new("SourceFormat", ["XML"]) },
                    new() { Key = "FormatConfig.isJsonLines", Label = "JSON Lines", Kind = NodeFieldKinds.Bool, Hidden = true }
                ],
                Normalize = NormalizeFormat
            },
            new()
            {
                Type = "mapping", Label = "Map columns", Icon = "arrow-right-left", Rank = 40,
                Tooltip = "Rename, type and pick the columns that are written",
                Presence = new("ColumnMappingsJson"),
                Fields =
                [
                    new() { Key = "AutoMapColumns", Label = "Map remaining columns by name", Kind = NodeFieldKinds.Bool },
                    new() { Key = "SkipUnmappedColumns", Label = "Drop columns without a match", Kind = NodeFieldKinds.Bool },
                    new() { Key = "ColumnMappingsJson", Label = "Columns", Kind = NodeFieldKinds.Table, Required = true, Columns =
                    [
                        new() { Key = "sourceColumn", Label = "Source", Required = true },
                        new() { Key = "targetColumn", Label = "Target", Required = true },
                        new() { Key = "dataType", Label = "Type", Kind = NodeFieldKinds.Select,
                            Options = [new("", "Auto"), new("string", "Text"), new("int", "Whole number"), new("decimal", "Decimal"), new("datetime", "Date and time"), new("bool", "Yes or no")] },
                        new() { Key = "isKeyColumn", Label = "Key", Kind = NodeFieldKinds.Bool },
                        new() { Key = "skipOnNull", Label = "Skip", Kind = NodeFieldKinds.Bool, Tooltip = "Skip the column when the value is empty" }
                    ] }
                ]
            },
            new()
            {
                Type = "deltasync", Label = "Smart Sync", Icon = "refresh-cw", Rank = 50,
                Tooltip = "Only write records that are new or changed since the last run",
                Presence = new("DeltaSyncEnabled", True),
                Sets = { ["DeltaSyncEnabled"] = true },
                Fields =
                [
                    new() { Key = "DeltaSyncReefIdColumn", Label = "Unique id field", Required = true },
                    new() { Key = "DeltaSyncHashAlgorithm", Label = "Hash algorithm", Kind = NodeFieldKinds.Select,
                        Options = [new("SHA256", "SHA256"), new("MD5", "MD5")] },
                    new() { Key = "DeltaSyncTrackDeletes", Label = "Handle deleted records", Kind = NodeFieldKinds.Bool },
                    new() { Key = "DeltaSyncDeleteStrategy", Label = "On delete", Kind = NodeFieldKinds.Select, ShowWhen = new("DeltaSyncTrackDeletes", True),
                        Options = [new("SoftDelete", "Mark the row"), new("HardDelete", "Delete the row")] },
                    new() { Key = "DeltaSyncDeleteColumn", Label = "Mark column", Required = true, ShowWhen = new("DeltaSyncDeleteStrategy", ["SoftDelete"]) },
                    new() { Key = "DeltaSyncDeleteValue", Label = "Mark value", ShowWhen = new("DeltaSyncDeleteStrategy", ["SoftDelete"]) },
                    new() { Key = "DeltaSyncRetentionDays", Label = "Keep history for days", Kind = NodeFieldKinds.Number }
                ]
            },
            new()
            {
                Type = "target", Label = "Write", Icon = "database", Rank = 60, Required = true,
                Tooltip = "Write the records to a table or a local file",
                Fields =
                [
                    new() { Key = "TargetType", Label = "To", Kind = NodeFieldKinds.Select, Required = true,
                        Options = [new("Database", "Database table"), new("LocalFile", "Local file")] },
                    new() { Key = "TargetConnectionId", Label = "Connection", Kind = NodeFieldKinds.Select, OptionsSource = "connections", Required = true, ShowWhen = new("TargetType", Database) },
                    new() { Key = "TargetTable", Label = "Table", Required = true, ShowWhen = new("TargetType", Database) },
                    new() { Key = "LoadStrategy", Label = "Load", Kind = NodeFieldKinds.Select, ShowWhen = new("TargetType", Database),
                        Options = [new("Upsert", "Insert or update"), new("Insert", "Insert new only"), new("Append", "Always insert"), new("FullReplace", "Replace all rows")] },
                    new() { Key = "UpsertKeyColumns", Label = "Key columns", Required = true, Placeholder = "OrderId", Tooltip = "Comma separated", ShowWhen = new("LoadStrategy", ["Upsert"]) },
                    new() { Key = "BatchSize", Label = "Batch size", Kind = NodeFieldKinds.Number, ShowWhen = new("TargetType", Database) },
                    new() { Key = "CommandTimeoutSeconds", Label = "Timeout in seconds", Kind = NodeFieldKinds.Number, ShowWhen = new("TargetType", Database) },
                    new() { Key = "LocalTargetPath", Label = "File path", Required = true, ShowWhen = new("TargetType", LocalFile) },
                    new() { Key = "LocalTargetFormat", Label = "Format", Kind = NodeFieldKinds.Select, ShowWhen = new("TargetType", LocalFile),
                        Options = [new("CSV", "CSV"), new("JSON", "JSON"), new("JSONL", "JSON Lines")] },
                    new() { Key = "LocalTargetWriteMode", Label = "Existing file", Kind = NodeFieldKinds.Select, ShowWhen = new("TargetType", LocalFile),
                        Options = [new("Overwrite", "Overwrite"), new("Append", "Append")] }
                ]
            },
            new()
            {
                Type = "outputs", Label = "Export copies", Icon = "copy", Rank = 65,
                Tooltip = "Also write the imported records to one or more destinations",
                Presence = new(ProfileDocumentService.OutputTargetsKey),
                Fields =
                [
                    new() { Key = ProfileDocumentService.OutputTargetsKey, Label = "Copies", Kind = NodeFieldKinds.Table, Required = true, Columns =
                    [
                        new() { Key = "DestinationId", Label = "Destination", Kind = NodeFieldKinds.Select, OptionsSource = "destinations", Required = true },
                        new() { Key = "OutputFormat", Label = "Format", Kind = NodeFieldKinds.Select, Required = true, Default = "JSON",
                            Options = [new("JSON", "JSON"), new("JSONL", "JSON Lines"), new("CSV", "CSV"), new("XML", "XML")] },
                        new() { Key = "FilenameTemplate", Label = "File name", Placeholder = "{profile}_{timestamp}.{format}" },
                        new() { Key = "OnFailure", Label = "On error", Kind = NodeFieldKinds.Select, Default = "Warn",
                            Options = [new("Warn", "Warn"), new("Fail", "Fail")] },
                        new() { Key = "IsEnabled", Label = "On", Kind = NodeFieldKinds.Bool, Default = true }
                    ] }
                ]
            },
            new()
            {
                Type = "postprocess", Label = "Post-process", Icon = "check-circle", Rank = 70,
                Tooltip = "Run a statement or script after the import",
                Presence = new("PostProcessType"),
                Fields = [.. ProcessingNodeFields.For("PostProcess"),
                    new() { Key = "PostProcessSkipOnFailure", Label = "Skip when the import fails", Kind = NodeFieldKinds.Bool },
                    new() { Key = "PostProcessRollbackOnFailure", Label = "Roll back on its own failure", Kind = NodeFieldKinds.Bool }],
                Normalize = c => ProcessingNodeFields.MirrorType(c, "PostProcess")
            }
        ]
    };

    private static void NormalizeFormat(JsonObject config)
    {
        var format = ConfigPath.ToText(ConfigPath.Get(config, "SourceFormat"));
        if (format == "TSV")
            ConfigPath.Set(config, "FormatConfig.delimiter", "\t");
        if (format is "JSON" or "JSONL")
            ConfigPath.Set(config, "FormatConfig.isJsonLines", format == "JSONL");
    }
}
