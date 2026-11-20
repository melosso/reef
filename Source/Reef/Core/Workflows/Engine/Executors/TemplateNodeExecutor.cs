using Reef.Core.DocumentGeneration;
using Reef.Core.Formatters;
using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.TemplateEngines;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;
using Reef.Helpers;

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

        var extension = OutputFormats.Extension(template.OutputFormat);
        var files = new List<WorkflowFile>();
        foreach (var batch in input.Batches())
        {
            var name = batch.SplitKey is null
                ? $"{context.Node.Id}_{Guid.NewGuid():N}.{extension}"
                : FileNames.Generate(input.Split!.FilenameTemplate, context.Workflow.Name, extension, context.ExecutionId, batch.SplitKey);
            var path = SafePath.Combine(Path.Combine(context.WorkFolder, context.Node.Id), name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (template.Type == QueryTemplateType.DocumentTemplate)
            {
                var generated = await documents.TransformAsync(batch.Rows, template.Template,
                    new Dictionary<string, object> { ["profile_name"] = context.Workflow.Name, ["execution_id"] = context.ExecutionId.ToString() }, null, useTemporaryDirectory: true);
                File.Move(generated, path, overwrite: true);
            }
            else
            {
                var content = template.Type is >= QueryTemplateType.ForXmlRaw and <= QueryTemplateType.ForJsonPath
                    ? await NativeAsync(context, template)
                    : await scriban.TransformAsync(batch.Rows, template.Template);
                await File.WriteAllTextAsync(path, content, ct);
            }
            files.Add(new WorkflowFile(path, Path.GetFileName(path), OutputFormats.MimeType(extension), new FileInfo(path).Length));
        }
        return NodeResult.Main(input.WithFiles(files));
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
