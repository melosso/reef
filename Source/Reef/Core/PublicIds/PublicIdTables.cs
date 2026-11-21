using Reef.Core.Models;
using Reef.Core.Models.Dashboard;
using Reef.Core.Services;
using Reef.Core.Workflows.Models;

namespace Reef.Core.PublicIds;

// Names the table behind every integer id the api exposes, so it can travel as that row's Ulid.
public static class PublicIdTables
{
    public const string Profiles = "Profiles";
    public const string ImportProfiles = "ImportProfiles";
    public const string ProfileExecutions = "ProfileExecutions";
    public const string ImportProfileExecutions = "ImportProfileExecutions";

    private static readonly HashSet<string> Tables = new(StringComparer.Ordinal)
    {
        "ApiKeys", "AuditLog", "Connections", "DestinationEndpoints", "Destinations",
        "JobDependencies", "JobExecutions", "Jobs",
        "NotificationEmailTemplate", "NotificationSettings", "OidcSettings", "PendingEmailApprovals", "QueryTemplates", "UsernameHistory", "Users", "WebhookTriggers", "Workflows", "WorkflowExecutions", "NodeRuns"
    };

    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProfileId"] = Profiles,
        ["DependsOnProfileId"] = Profiles,
        ["ConnectionId"] = "Connections",
        ["TargetConnectionId"] = "Connections",
        ["DestinationId"] = "Destinations",
        ["OutputDestinationId"] = "Destinations",
        ["SourceDestinationId"] = "Destinations",
        ["LocalDestinationId"] = "Destinations",
        ["DestinationEndpointId"] = "DestinationEndpoints",
        ["OutputDestinationEndpointId"] = "DestinationEndpoints",
        ["GroupId"] = "ProfileGroups",
        ["ParentId"] = "ProfileGroups",
        ["JobId"] = "Jobs",
        ["DependsOnJobId"] = "Jobs",
        ["ImportProfileId"] = ImportProfiles,
        ["TemplateId"] = "QueryTemplates",
        ["EmailTemplateId"] = "QueryTemplates",
        ["PendingEmailApprovalId"] = "PendingEmailApprovals",
        ["ProfileExecutionId"] = ProfileExecutions,
        ["ExecutionId"] = ProfileExecutions,
        ["UserId"] = "Users",
        ["ApprovedByUserId"] = "Users",
        ["SkippedByUserId"] = "Users",
        ["ChangedByUserId"] = "Users",
        ["CreatedBy"] = "Users",
        ["WorkflowId"] = "Workflows",
        ["WorkflowExecutionId"] = "WorkflowExecutions"
    };

    private static readonly Dictionary<Type, string> ById = new()
    {
        [typeof(User)] = "Users",
        [typeof(ApiKey)] = "ApiKeys",
        [typeof(ProfileGroup)] = "ProfileGroups",
        [typeof(Connection)] = "Connections",
        [typeof(Profile)] = Profiles,
        [typeof(EmailExportConfig)] = Profiles,
        [typeof(ProfileExecution)] = ProfileExecutions,
        [typeof(ProfileExecutionSplit)] = "ProfileExecutionSplits",
        [typeof(ProfileExecutionError)] = "ProfileExecutionErrors",
        [typeof(WebhookTrigger)] = "WebhookTriggers",
        [typeof(AuditLog)] = "AuditLog",
        [typeof(AuditLogEntry)] = "AuditLog",
        [typeof(ProfileDependency)] = "ProfileDependencies",
        [typeof(ScheduledTask)] = "ScheduledTasks",
        [typeof(PendingEmailApproval)] = "PendingEmailApprovals",
        [typeof(NotificationSettings)] = "NotificationSettings",
        [typeof(OidcSettings)] = "OidcSettings",
        [typeof(NotificationEmailTemplate)] = "NotificationEmailTemplate",
        [typeof(UsernameHistory)] = "UsernameHistory",
        [typeof(Destination)] = "Destinations",
        [typeof(DestinationEndpoint)] = "DestinationEndpoints",
        [typeof(ImportProfile)] = ImportProfiles,
        [typeof(ImportProfileExecution)] = ImportProfileExecutions,
        [typeof(ImportOutputTarget)] = "ImportOutputTargets",
        [typeof(ImportExecutionError)] = "ImportExecutionErrors",
        [typeof(Job)] = "Jobs",
        [typeof(JobExecution)] = "JobExecutions",
        [typeof(AttentionJob)] = "Jobs",
        [typeof(QueryTemplate)] = "QueryTemplates",
        [typeof(ProfileTransformation)] = "ProfileTransformations",
        [typeof(WorkflowDocument)] = "Workflows",
        [typeof(WorkflowExecution)] = "WorkflowExecutions",
        [typeof(NodeRun)] = "NodeRuns"
    };

    private static readonly Dictionary<string, string> AuditEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Profile"] = Profiles,
        ["User"] = "Users",
        ["WebhookTrigger"] = "WebhookTriggers",
        ["PendingEmailApproval"] = "PendingEmailApprovals",
        ["ProfileGroup"] = "ProfileGroups",
        ["Connection"] = "Connections",
        ["ProfileExecutions"] = ProfileExecutions,
        ["ApiKey"] = "ApiKeys",
        ["OidcSettings"] = "OidcSettings",
        ["NotificationSettings"] = "NotificationSettings",
        ["Destination"] = "Destinations",
        ["Job"] = "Jobs",
        ["ImportProfile"] = ImportProfiles,
        ["QueryTemplate"] = "QueryTemplates",
        ["Workflow"] = "Workflows"
    };

    public static string Known(string table) =>
        Tables.Contains(table) ? table : throw new ArgumentException($"{table} has no public ids", nameof(table));

    public static bool IsKnown(string table) => Tables.Contains(table);

    public static string? ForName(string property) => ByName.GetValueOrDefault(property);

    // the table for one property, resolved per object when the same property points at different tables
    public static Func<object, string?>? For(Type declaringType, string property)
    {
        if (declaringType == typeof(ImportExecutionError) && property == nameof(ImportExecutionError.ExecutionId))
            return _ => ImportProfileExecutions;
        if (typeof(AuditLog).IsAssignableFrom(declaringType) && property == nameof(AuditLog.EntityId))
            return o => AuditEntities.GetValueOrDefault(((AuditLog)o).EntityType);
        if (declaringType == typeof(AttentionRun) || declaringType == typeof(AttentionFailure) || declaringType == typeof(AttentionAnomaly))
        {
            if (property == "ExecutionId") return _ => "WorkflowExecutions";
        }

        if (property == "Id")
            return ById.TryGetValue(declaringType, out var own) ? _ => own : null;
        return ByName.TryGetValue(property, out var table) ? _ => table : null;
    }

    private static bool IsImport(object o) =>
        string.Equals((string?)o.GetType().GetProperty("Kind")?.GetValue(o), "import", StringComparison.OrdinalIgnoreCase);
}
