using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class EmailNodeExecutor(DestinationService destinations, QueryTemplateService templates, EmailExportService email, EmailApprovalService approvals) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["email"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var config = context.Config;
        var server = await destinations.GetByIdForExecutionAsync(NodeConfig.Id(config, "DestinationId"), ct);
        if (server is not { Type: DestinationType.Email })
            throw new NodeExecutionException("Mail server not found");
        var template = await templates.GetByIdAsync(NodeConfig.Id(config, "TemplateId"));
        if (template is not { Type: QueryTemplateType.ScribanTemplate })
            throw new NodeExecutionException("Email template not found or not a Scriban template");

        var groupBy = NodeConfig.Text(config, "GroupByColumn");
        var settings = new EmailExportConfig
        {
            Name = context.Workflow.Name,
            IsEmailExport = true,
            UseHardcodedRecipients = NodeConfig.Text(config, "RecipientsFrom") == "Fixed",
            EmailRecipientsColumn = NodeConfig.Text(config, "RecipientsColumn"),
            EmailRecipientsHardcoded = NodeConfig.Text(config, "Recipients"),
            UseHardcodedCc = NodeConfig.Text(config, "CcColumn").Length == 0,
            EmailCcColumn = NodeConfig.Text(config, "CcColumn"),
            EmailCcHardcoded = NodeConfig.Text(config, "Cc"),
            UseHardcodedSubject = NodeConfig.Text(config, "SubjectColumn").Length == 0,
            EmailSubjectColumn = NodeConfig.Text(config, "SubjectColumn"),
            EmailSubjectHardcoded = NodeConfig.Text(config, "Subject"),
            EmailAttachmentConfig = NodeConfig.Text(config, "EmailAttachmentConfig") is { Length: > 0 } attachments ? attachments : null,
            EmailGroupBySplitKey = groupBy.Length > 0,
            SplitKeyColumn = groupBy.Length > 0 ? groupBy : null
        };

        var input = context.Input;
        if (input.Items.Count == 0)
            return NodeResult.Main(input);
        var rows = input.Items.Select(r => r.ToDictionary(c => c.Key, c => c.Value ?? DBNull.Value)).ToList();
        if (NodeConfig.Flag(config, "RequireApproval"))
            return await HoldAsync(context, settings, server.Id, template, rows);

        var (sent, message, successCount, failureCount, _, failures) = await email.ExportToEmailAsync(settings, server, template, rows);

        var total = successCount + failureCount;
        var threshold = int.TryParse(NodeConfig.Text(config, "SuccessPercent"), out var percent) ? percent : 100;
        var failed = failures.Count == 0 ? null : $"{failureCount} of {total} emails failed, first {failures[0].Recipient}: {failures[0].Error}";
        if (!sent && (total == 0 || successCount * 100 / total < threshold))
            throw new NodeExecutionException(failed ?? message ?? "Sending failed");
        return new NodeResult { Outputs = { [WorkflowPorts.Main] = input }, Warning = failed };
    }

    // Later steps do not wait
    private async Task<NodeResult> HoldAsync(NodeContext context, EmailExportConfig settings, int serverId, QueryTemplate template, List<Dictionary<string, object>> rows)
    {
        var (rendered, errors) = await email.RenderEmailsForApprovalAsync(settings, template, rows, AttachmentConfig.Parse(settings.EmailAttachmentConfig));
        if (rendered.Count == 0)
            throw new NodeExecutionException(errors.FirstOrDefault() ?? "No emails could be rendered for approval");

        var source = new WorkflowApprovalSource(context.ExecutionId, context.Node.Id, serverId, NodeConfig.Text(context.Config, "ApproverRoles") is { Length: > 0 } roles ? roles : null);
        foreach (var (recipients, subject, htmlBody, cc, attachments, _, _) in rendered)
            await approvals.CreatePendingApprovalAsync(null, null, recipients, subject, htmlBody, cc, attachments, workflow: source);
        var failed = errors.Count == 0 ? "" : $" {errors.Count} could not be rendered, first: {errors[0]}";
        return new NodeResult { Outputs = { [WorkflowPorts.Main] = context.Input }, Warning = $"{rendered.Count} emails wait for approval.{failed}" };
    }
}
