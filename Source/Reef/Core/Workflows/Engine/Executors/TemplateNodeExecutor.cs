using Reef.Core.DocumentGeneration;
using Reef.Core.Formatters;
using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.TemplateEngines;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class TemplateNodeExecutor(QueryTemplateService templates, ScribanTemplateEngine scriban, DocumentTemplateEngine documents,
    ConnectionService connections) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["template"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var template = await templates.GetByIdAsync(NodeConfig.Id(context.Config, "TemplateId"))
            ?? throw new NodeExecutionException("Template not found");
        var input = context.Input;
        if (!template.IsActive)
            return new NodeResult { Outputs = { [WorkflowPorts.Main] = input }, Warning = $"Template {template.Name} is not active" };

        var rows = input.Items.Select(r => r.ToDictionary(c => c.Key, c => c.Value ?? DBNull.Value)).ToList();
        var extension = OutputFormats.Extension(template.OutputFormat);
        string path;
        if (template.Type == QueryTemplateType.DocumentTemplate)
        {
            var generated = await documents.TransformAsync(rows, template.Template,
                new Dictionary<string, object> { ["profile_name"] = context.Workflow.Name, ["execution_id"] = context.ExecutionId.ToString() }, null, useTemporaryDirectory: true);
            path = Path.Combine(context.WorkFolder, Path.GetFileName(generated));
            File.Move(generated, path, overwrite: true);
        }
        else
        {
            var content = template.Type is >= QueryTemplateType.ForXmlRaw and <= QueryTemplateType.ForJsonPath
                ? await NativeAsync(context, template)
                : await scriban.TransformAsync(rows, template.Template);
            path = Path.Combine(context.WorkFolder, $"{context.Node.Id}_{Guid.NewGuid():N}.{extension}");
            await File.WriteAllTextAsync(path, content, ct);
        }

        var file = new WorkflowFile(path, Path.GetFileName(path), OutputFormats.MimeType(extension), new FileInfo(path).Length);
        return NodeResult.Main(new WorkflowItems { Columns = input.Columns, Items = input.Items, Files = [file] });
    }

    // Native templates rerun the upstream query
    private async Task<string> NativeAsync(NodeContext context, QueryTemplate template)
    {
        var query = Upstream(context.Workflow, context.Node.Id, "query")
            ?? throw new NodeExecutionException("Native SQL Server templates need a query node before them");
        await using var db = await connections.CreateDatabaseConnectionAsync(NodeConfig.Id(query.Config ?? new(), "ConnectionId"));
        await db.OpenAsync();
        var result = await templates.ApplyTransformationAsync(db, new TransformationRequest
        {
            Query = NodeConfig.Text(query.Config ?? new(), "Query"), TemplateId = template.Id, Type = template.Type
        });
        return result.Success ? result.Output ?? "" : throw new NodeExecutionException($"Native template failed: {result.ErrorMessage}");
    }

    private static WorkflowNode? Upstream(WorkflowDocument workflow, string nodeId, string type)
    {
        var seen = new HashSet<string>();
        var queue = new Queue<string>([nodeId]);
        while (queue.TryDequeue(out var id))
        {
            foreach (var edge in workflow.Edges.Where(e => e.To == id && seen.Add(e.From)))
            {
                var node = workflow.Nodes.First(n => n.Id == edge.From);
                if (node.Type == type)
                    return node;
                queue.Enqueue(node.Id);
            }
        }
        return null;
    }
}
