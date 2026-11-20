using System.Text.Json.Nodes;
using Reef.Core.Workflows.Models;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.Nodes.Exceptions;

namespace Reef.Core.Workflows.Catalogs;

public static class WorkflowNodeCatalog
{
    public const string Fallback = "fallback";
    public const string FilesFormat = "Files";

    private static readonly string[] True = ["true"];
    private static readonly string[] Cron = ["Cron"];
    private static readonly string[] Interval = ["Interval"];
    private static readonly string[] Join = ["join"];
    private static readonly string[] WithValue = ["equals", "notEquals", "contains", "greaterThan", "lessThan"];
    private static readonly string[] Sql = ["Query", "StoredProcedure"];
    private static readonly string[] Script = ["Script"];
    private static readonly string[] Local = ["Local"];
    private static readonly string[] Sftp = ["Sftp"];
    private static readonly string[] Http = ["Http"];
    private static readonly string[] S3 = ["S3"];
    private static readonly string[] Azure = ["AzureBlob"];
    private static readonly string[] Share = ["NetworkShare"];
    private static readonly string[] Files = ["Local", "Sftp", "S3", "AzureBlob", "NetworkShare"];
    private static readonly string[] Delimited = ["CSV", "TSV"];
    private static readonly string[] JsonFormats = ["JSON", "JSONL"];
    private static readonly string[] Database = ["Database"];
    private static readonly string[] LocalFile = ["LocalFile"];
    private static readonly string[] Fixed = ["Fixed"];
    private static readonly string[] FromColumn = ["Column"];

    public static readonly WorkflowCatalog Catalog = new()
    {
        Nodes =
        [
            Trigger("manual", "Manual", "mouse-pointer-click", "Starts the workflow from the editor, the API or Ctrl K", []),
            Trigger("schedule", "Schedule", "clock", "Starts the workflow on a cron expression or a fixed interval",
            [
                new() { Key = "Repeat", Label = "Repeat", Kind = NodeFieldKinds.Select, Required = true, Default = "Cron",
                    Options = [new("Cron", "Cron expression"), new("Interval", "Interval")] },
                new() { Key = "Cron", Label = "Cron expression", Required = true, Placeholder = "0 6 * * *", ShowWhen = new("Repeat", Cron) },
                new() { Key = "EveryMinutes", Label = "Every minutes", Kind = NodeFieldKinds.Number, Required = true, ShowWhen = new("Repeat", Interval) }
            ]),
            Trigger("webhook", "Webhook", "webhook", "Starts the workflow when a request reaches its webhook URL", []),

            Node("query", "Query", "database", WorkflowNodeGroups.Sources, "Reads rows from a database connection",
            [
                Connection(),
                new() { Key = "Query", Label = "SQL query", Kind = NodeFieldKinds.Code, Required = true, Placeholder = "SELECT * FROM Orders WHERE ..." }
            ]),
            Node("fetch", "Fetch", "download", WorkflowNodeGroups.Sources, "Downloads files or an API response for the next step to read", FetchFields()),

            Node("parse", "Parse", "file-text", WorkflowNodeGroups.Transform, "Reads rows out of the fetched files", ParseFields()),
            Node("mapping", "Map columns", "arrow-right-left", WorkflowNodeGroups.Transform, "Renames, converts and picks the columns that move on",
            [
                new() { Key = "AutoMap", Label = "Keep columns without a mapping", Kind = NodeFieldKinds.Bool },
                new() { Key = "Columns", Label = "Columns", Kind = NodeFieldKinds.Table, Required = true, Columns =
                [
                    new() { Key = "sourceColumn", Label = "Source", Required = true },
                    new() { Key = "targetColumn", Label = "Target", Required = true },
                    new() { Key = "dataType", Label = "Type", Kind = NodeFieldKinds.Select,
                        Options = [new("", "Auto"), new("string", "Text"), new("int", "Whole number"), new("decimal", "Decimal"), new("datetime", "Date and time"), new("bool", "Yes or no")] },
                    new() { Key = "isKeyColumn", Label = "Key", Kind = NodeFieldKinds.Bool },
                    new() { Key = "skipOnNull", Label = "Skip", Kind = NodeFieldKinds.Bool, Tooltip = "Leaves the column out when the value is empty" },
                    new() { Key = "defaultValue", Label = "Default" }
                ] }
            ]),
            Node("deltasync", "Smart Sync", "refresh-cw", WorkflowNodeGroups.Transform, "Passes only rows that are new or changed since the last successful run",
            [
                new() { Key = "IdColumn", Label = "Unique id column", Required = true, Format = NodeFieldFormats.Identifier,
                    Tooltip = "Column that identifies a row across runs" },
                new() { Key = "HashAlgorithm", Label = "Hash algorithm", Kind = NodeFieldKinds.Select, Default = "SHA256",
                    Options = [new("SHA256", "SHA256"), new("SHA512", "SHA512"), new("MD5", "MD5")] },
                new() { Key = "DuplicateIds", Label = "Duplicate ids", Kind = NodeFieldKinds.Select, Default = "Strict",
                    Options = [new("Strict", "Fail the run"), new("CompositeHash", "Combine into one hash"), new("Skip", "Skip duplicates")] },
                new() { Key = "EmptyIds", Label = "Empty ids", Kind = NodeFieldKinds.Select, Default = "Strict",
                    Options = [new("Strict", "Fail the run"), new("GenerateHash", "Generate an id"), new("Skip", "Skip the row")] },
                new() { Key = "DecimalPlaces", Label = "Decimal places compared", Kind = NodeFieldKinds.Number, Default = 6 },
                new() { Key = "TrackDeletes", Label = "Pass deleted rows", Kind = NodeFieldKinds.Bool,
                    Tooltip = "Adds a row with _ReefDeleted for every id missing since the last run" },
                new() { Key = "RetentionDays", Label = "Keep history for days", Kind = NodeFieldKinds.Number },
                new() { Key = "ExcludeIdColumn", Label = "Leave the id column out", Kind = NodeFieldKinds.Bool },
                new() { Key = "IdNormalization", Label = "Compare ids", Kind = NodeFieldKinds.Select, Default = "Trim",
                    Options = [new("Trim", "Ignore leading and trailing spaces"), new("Trim,Lowercase", "Ignore spaces and case"), new("RemoveWhitespace", "Ignore all spaces")] },
                new() { Key = "RemoveNonPrintable", Label = "Ignore hidden characters", Kind = NodeFieldKinds.Bool },
                new() { Key = "ResetOnSchemaChange", Label = "Start over when columns change", Kind = NodeFieldKinds.Bool,
                    Tooltip = "Clears the saved history when the query returns different columns" }
            ]),
            Node("split", "Split", "split", WorkflowNodeGroups.Transform, "Groups rows by a column so the next steps write one file per value",
            [
                new() { Key = "Column", Label = "Split by column", Required = true },
                new() { Key = "FileName", Label = "File name", Placeholder = "{workflow}_{splitkey}_{timestamp}.{format}" },
                new() { Key = "ExcludeColumn", Label = "Leave the split column out", Kind = NodeFieldKinds.Bool }
            ]),
            Node("template", "Template", "file-code", WorkflowNodeGroups.Transform, "Renders the rows into a file with a template",
            [
                new() { Key = "TemplateId", Label = "Template", Kind = NodeFieldKinds.Select, OptionsSource = "templates", Required = true },
                new() { Key = "Options", Label = "Options", Kind = NodeFieldKinds.Json, Tooltip = "Options for SQL Server FOR XML and FOR JSON templates" }
            ]),

            new()
            {
                Type = "if", Label = "If", Icon = "git-branch", Group = WorkflowNodeGroups.Flow,
                Tooltip = "Sends each row to true or false by a column value",
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
                Tooltip = "Sends each row to the output that matches its column value, other rows go to fallback",
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
                Tooltip = "Combines two branches by appending rows or joining them on a column",
                Inputs = ["input1", "input2"],
                Fields =
                [
                    new() { Key = "Mode", Label = "Combine", Kind = NodeFieldKinds.Select, Required = true, Default = "append",
                        Options = [new("append", "Append rows"), new("join", "Join on a column")] },
                    new() { Key = "JoinColumn", Label = "Join column", Required = true, ShowWhen = new("Mode", Join) }
                ]
            },

            Node("destination", "Destination", "hard-drive-upload", WorkflowNodeGroups.Deliver, "Writes the rows or the files from the previous step to a destination",
            [
                new() { Key = "DestinationId", Label = "Destination", Kind = NodeFieldKinds.Select, OptionsSource = "destinations", Required = true },
                new() { Key = "EndpointId", Label = "Endpoint", Kind = NodeFieldKinds.Select, OptionsSource = "destination-endpoints",
                    Tooltip = "Sends to one of the endpoints saved on the destination" },
                new() { Key = "Format", Label = "Format", Kind = NodeFieldKinds.Select, Required = true, Default = "JSON",
                    Tooltip = "Files from input uploads the files the previous step made",
                    Options = [new("JSON", "JSON"), new("XML", "XML"), new("CSV", "CSV"), new("YAML", "YAML"), new(FilesFormat, "Files from input")] },
                new() { Key = "FileName", Label = "File name", Placeholder = "{workflow}_{timestamp}.{format}" },
                new() { Key = "RemoveOnFailure", Label = "Remove the files when the run fails", Kind = NodeFieldKinds.Bool,
                    Tooltip = "Deletes the uploaded files if a later step fails, so no half finished output is left behind" }
            ]),
            Node("email", "Send email", "mail", WorkflowNodeGroups.Deliver, "Sends each row, or each group of rows, as an email",
            [
                new() { Key = "DestinationId", Label = "Mail server", Kind = NodeFieldKinds.Select, OptionsSource = "email-destinations", Required = true },
                new() { Key = "TemplateId", Label = "Email template", Kind = NodeFieldKinds.Select, OptionsSource = "templates", Required = true },
                new() { Key = "RecipientsFrom", Label = "Recipients from", Kind = NodeFieldKinds.Select, Required = true, Default = "Column",
                    Options = [new("Column", "A column"), new("Fixed", "A fixed address")] },
                new() { Key = "RecipientsColumn", Label = "Recipient column", Required = true, ShowWhen = new("RecipientsFrom", FromColumn) },
                new() { Key = "Recipients", Label = "Recipient", Required = true, Format = NodeFieldFormats.Email, ShowWhen = new("RecipientsFrom", Fixed) },
                new() { Key = "CcColumn", Label = "CC column" },
                new() { Key = "Cc", Label = "CC", Format = NodeFieldFormats.Email },
                new() { Key = "SubjectColumn", Label = "Subject column", Tooltip = "Leave empty to use the subject below" },
                new() { Key = "Subject", Label = "Subject" },
                new() { Key = "GroupByColumn", Label = "One email per value of", Tooltip = "Leave empty to send one email per row" },
                new() { Key = "SuccessPercent", Label = "Success threshold in percent", Kind = NodeFieldKinds.Number, Default = 100,
                    Tooltip = "The step succeeds when at least this share of emails is sent" },
                new() { Key = "RequireApproval", Label = "Hold for approval", Kind = NodeFieldKinds.Bool },
                new() { Key = "ApproverRoles", Label = "Approver roles", Kind = NodeFieldKinds.Json, Placeholder = "[\"Admin\"]", ShowWhen = new("RequireApproval", True) },
                new() { Key = "EmailAttachmentConfig", Label = "Attachments", Kind = NodeFieldKinds.Json,
                    Tooltip = "Attachment settings as JSON, leave empty to send without attachments" }
            ]),
            Node("write", "Write", "database", WorkflowNodeGroups.Deliver, "Inserts or updates the rows in a table, or writes them to a local file",
            [
                new() { Key = "To", Label = "To", Kind = NodeFieldKinds.Select, Required = true, Default = "Database",
                    Options = [new("Database", "Database table"), new("LocalFile", "Local file")] },
                new() { Key = "ConnectionId", Label = "Connection", Kind = NodeFieldKinds.Select, OptionsSource = "connections", Required = true, ShowWhen = new("To", Database) },
                new() { Key = "Table", Label = "Table", Required = true, ShowWhen = new("To", Database) },
                new() { Key = "Load", Label = "Load", Kind = NodeFieldKinds.Select, Default = "Upsert", ShowWhen = new("To", Database),
                    Options = [new("Upsert", "Insert or update"), new("Insert", "Insert new only"), new("Append", "Always insert"), new("FullReplace", "Replace all rows")] },
                new() { Key = "KeyColumns", Label = "Key columns", Required = true, Placeholder = "OrderId", Tooltip = "Comma separated", ShowWhen = new("Load", ["Upsert"]) },
                new() { Key = "BatchSize", Label = "Batch size", Kind = NodeFieldKinds.Number, Default = 500, ShowWhen = new("To", Database) },
                new() { Key = "TimeoutSeconds", Label = "Timeout in seconds", Kind = NodeFieldKinds.Number, Default = 120, ShowWhen = new("To", Database) },
                new() { Key = "FilePath", Label = "File path", Required = true, ShowWhen = new("To", LocalFile) },
                new() { Key = "FileFormat", Label = "Format", Kind = NodeFieldKinds.Select, Default = "CSV", ShowWhen = new("To", LocalFile),
                    Options = [new("CSV", "CSV"), new("JSON", "JSON"), new("JSONL", "JSON Lines")] },
                new() { Key = "ExistingFile", Label = "Existing file", Kind = NodeFieldKinds.Select, Default = "Overwrite", ShowWhen = new("To", LocalFile),
                    Options = [new("Overwrite", "Overwrite"), new("Append", "Append")] }
            ]),

            Node("command", "Run command", "terminal", WorkflowNodeGroups.Hooks, "Runs a SQL statement, a stored procedure or a script, the rows pass through unchanged",
            [
                new() { Key = "Runs", Label = "Runs", Kind = NodeFieldKinds.Select, Required = true, Default = "Query",
                    Options = [new("Query", "SQL statement"), new("StoredProcedure", "Stored procedure"), new("Script", "Script")] },
                new() { Key = "ConnectionId", Label = "Connection", Kind = NodeFieldKinds.Select, OptionsSource = "connections", Required = true, ShowWhen = new("Runs", Sql) },
                new() { Key = "Command", Label = "Command", Kind = NodeFieldKinds.Code, Required = true, ShowWhen = new("Runs", Sql),
                    Placeholder = "UPDATE Items SET Status = 'Done' WHERE Id = {executionid}",
                    Tooltip = "{executionid} and {rowcount} are replaced when the step runs" },
                new() { Key = "Interpreter", Label = "Interpreter", Kind = NodeFieldKinds.Select, Required = true, ShowWhen = new("Runs", Script),
                    Options = [new("pwsh", "PowerShell"), new("python", "Python"), new("node", "Node.js"), new("bash", "Bash"), new("sh", "sh"), new("cmd", "cmd")] },
                new() { Key = "InlineScript", Label = "Inline script", Kind = NodeFieldKinds.Bool, Default = true, ShowWhen = new("Runs", Script),
                    Tooltip = "Off runs the script file at the given path" },
                new() { Key = "Script", Label = "Script", Kind = NodeFieldKinds.Code, Required = true, ShowWhen = new("Runs", Script) },
                new() { Key = "EnvironmentVariables", Label = "Environment variables", Kind = NodeFieldKinds.List, ShowWhen = new("Runs", Script),
                    Tooltip = "Names of the variables passed to the script" },
                new() { Key = "TimeoutSeconds", Label = "Timeout in seconds", Kind = NodeFieldKinds.Number, Default = 30 },
                new() { Key = "ContinueOnError", Label = "Continue on error", Kind = NodeFieldKinds.Bool,
                    Tooltip = "A failure is recorded on the step and the run goes on" },
                new() { Key = "SkipWhenEmpty", Label = "Skip when no rows arrive", Kind = NodeFieldKinds.Bool,
                    Tooltip = "Other steps skip on empty input by default, commands run unless this is on" }
            ])
        ]
    };

    private static NodeField Connection() =>
        new() { Key = "ConnectionId", Label = "Connection", Kind = NodeFieldKinds.Select, OptionsSource = "connections", Required = true };

    private static List<NodeField> FetchFields() =>
    [
        new() { Key = "From", Label = "From", Kind = NodeFieldKinds.Select, Required = true, Default = "Local", Options =
        [
            new("Local", "Local file"), new("Sftp", "SFTP"), new("Http", "HTTP API"),
            new("S3", "S3"), new("AzureBlob", "Azure Blob"), new("NetworkShare", "Network share")
        ] },
        new() { Key = "Path", Label = "Folder or file", Required = true, ShowWhen = new("From", Local) },
        new() { Key = "Settings.host", Label = "Host", Required = true, ShowWhen = new("From", Sftp) },
        new() { Key = "Settings.port", Label = "Port", Kind = NodeFieldKinds.Number, Default = 22, ShowWhen = new("From", Sftp) },
        new() { Key = "Settings.username", Label = "User", ShowWhen = new("From", Sftp) },
        new() { Key = "Settings.authType", Label = "Sign in with", Kind = NodeFieldKinds.Select, ShowWhen = new("From", Sftp),
            Options = [new("password", "Password"), new("privatekey", "Private key")] },
        new() { Key = "Settings.password", Label = "Password", Kind = NodeFieldKinds.Password, ShowWhen = new("Settings.authType", ["password"]) },
        new() { Key = "Settings.privateKey", Label = "Private key", Kind = NodeFieldKinds.Textarea, ShowWhen = new("Settings.authType", ["privatekey"]) },
        new() { Key = "Settings.privateKeyPassphrase", Label = "Key passphrase", Kind = NodeFieldKinds.Password, ShowWhen = new("Settings.authType", ["privatekey"]) },
        new() { Key = "Settings.path", Label = "Remote folder", ShowWhen = new("From", Sftp) },
        new() { Key = "Settings.url", Label = "URL", Required = true, ShowWhen = new("From", Http) },
        new() { Key = "Method", Label = "Method", Kind = NodeFieldKinds.Select, Default = "GET", ShowWhen = new("From", Http),
            Options = [new("GET", "GET"), new("POST", "POST")] },
        new() { Key = "Settings.authType", Label = "Authentication", Kind = NodeFieldKinds.Select, ShowWhen = new("From", Http),
            Options = [new("", "None"), new("Bearer", "Bearer token"), new("Basic", "Basic"), new("ApiKey", "API key")] },
        new() { Key = "Settings.authToken", Label = "Token or credentials", Kind = NodeFieldKinds.Password, ShowWhen = new("Settings.authType", ["Bearer", "Basic", "ApiKey"]) },
        new() { Key = "Settings.apiKeyHeader", Label = "API key header", Placeholder = "X-Api-Key", ShowWhen = new("Settings.authType", ["ApiKey"]) },
        new() { Key = "Settings.headers", Label = "Headers", Kind = NodeFieldKinds.Json, ShowWhen = new("From", Http) },
        new() { Key = "Body", Label = "Body", Kind = NodeFieldKinds.Code, ShowWhen = new("Method", ["POST"]) },
        new() { Key = "RecordsPath", Label = "Records at path", Placeholder = "data.items", ShowWhen = new("From", Http) },
        new() { Key = "Paginate", Label = "Paginate", Kind = NodeFieldKinds.Bool, ShowWhen = new("From", Http) },
        new() { Key = "Pagination", Label = "Pagination", Kind = NodeFieldKinds.Json, ShowWhen = new("Paginate", True) },
        new() { Key = "Settings.bucketName", Label = "Bucket", Required = true, ShowWhen = new("From", S3) },
        new() { Key = "Settings.region", Label = "Region", ShowWhen = new("From", S3) },
        new() { Key = "Settings.prefix", Label = "Prefix", ShowWhen = new("From", S3) },
        new() { Key = "Settings.serviceUrl", Label = "Service URL", Tooltip = "For S3 compatible storage", ShowWhen = new("From", S3) },
        new() { Key = "Settings.accessKey", Label = "Access key", ShowWhen = new("From", S3) },
        new() { Key = "Settings.secretKey", Label = "Secret key", Kind = NodeFieldKinds.Password, ShowWhen = new("From", S3) },
        new() { Key = "Settings.container", Label = "Container", Required = true, ShowWhen = new("From", Azure) },
        new() { Key = "Settings.connectionString", Label = "Connection string", Kind = NodeFieldKinds.Password, ShowWhen = new("From", Azure) },
        new() { Key = "Settings.uncPath", Label = "UNC path", Required = true, Placeholder = @"\\server\share", ShowWhen = new("From", Share) },
        new() { Key = "Settings.subFolder", Label = "Sub folder", ShowWhen = new("From", Share) },
        new() { Key = "Settings.domain", Label = "Domain", ShowWhen = new("From", Share) },
        new() { Key = "Pattern", Label = "File pattern", Placeholder = "*.csv", ShowWhen = new("From", Files) },
        new() { Key = "Pick", Label = "Pick", Kind = NodeFieldKinds.Select, Default = "Latest", ShowWhen = new("From", Files),
            Options = [new("Latest", "Latest file"), new("Oldest", "Oldest file"), new("All", "All matching files"), new("Exact", "Exact path")] },
        new() { Key = "Archive", Label = "Archive after the run", Kind = NodeFieldKinds.Bool, ShowWhen = new("From", Files),
            Tooltip = "Moves the fetched files once the whole run succeeds" },
        new() { Key = "ArchiveFolder", Label = "Archive folder", ShowWhen = new("Archive", True) }
    ];

    private static List<NodeField> ParseFields() =>
    [
        new() { Key = "Format", Label = "Format", Kind = NodeFieldKinds.Select, Required = true, Default = "CSV", Options =
        [
            new("CSV", "CSV"), new("TSV", "TSV"), new("JSON", "JSON"), new("JSONL", "JSON Lines"), new("XML", "XML"), new("YAML", "YAML")
        ] },
        new() { Key = "Options.delimiter", Label = "Delimiter", Placeholder = ",", ShowWhen = new("Format", ["CSV"]) },
        new() { Key = "Options.quoteChar", Label = "Quote", Placeholder = "\"", ShowWhen = new("Format", Delimited) },
        new() { Key = "Options.encoding", Label = "Encoding", Kind = NodeFieldKinds.Select, Default = "UTF-8", ShowWhen = new("Format", Delimited),
            Options = [new("UTF-8", "UTF-8"), new("UTF-8-BOM", "UTF-8 with BOM"), new("Latin1", "Latin-1"), new("Windows1252", "Windows-1252")] },
        new() { Key = "Options.hasHeader", Label = "First row is a header", Kind = NodeFieldKinds.Bool, Default = true, ShowWhen = new("Format", Delimited) },
        new() { Key = "Options.trimWhitespace", Label = "Trim whitespace", Kind = NodeFieldKinds.Bool, ShowWhen = new("Format", Delimited) },
        new() { Key = "Options.skipRows", Label = "Skip leading rows", Kind = NodeFieldKinds.Number, ShowWhen = new("Format", Delimited) },
        new() { Key = "Options.nullValue", Label = "Treat as empty", Placeholder = "NULL", ShowWhen = new("Format", Delimited) },
        new() { Key = "Options.dataRootPath", Label = "Records at path", Placeholder = "data.items", ShowWhen = new("Format", JsonFormats) },
        new() { Key = "Options.recordElement", Label = "Record element", Required = true, Placeholder = "Order", ShowWhen = new("Format", ["XML"]) }
    ];

    private static WorkflowNodeDefinition Trigger(string type, string label, string icon, string tooltip, List<NodeField> fields) =>
        new() { Type = type, Label = label, Icon = icon, Tooltip = tooltip, Group = WorkflowNodeGroups.Triggers, Inputs = WorkflowPorts.None, Fields = fields, RunsWhenEmpty = true };

    private static WorkflowNodeDefinition Node(string type, string label, string icon, string group, string tooltip, List<NodeField> fields) =>
        new() { Type = type, Label = label, Icon = icon, Group = group, Tooltip = tooltip, Fields = fields, RunsWhenEmpty = group == WorkflowNodeGroups.Sources || type == "command" };

    private static string[] SwitchOutputs(JsonObject config) =>
        [.. (config["Cases"] as JsonArray ?? []).OfType<JsonObject>().Select(c => ConfigPath.ToText(c["Value"])).Where(v => v.Length > 0).Distinct(), Fallback];
}
