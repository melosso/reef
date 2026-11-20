using Reef.Core.Formatters;
using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;
using Reef.Helpers;

namespace Reef.Core.Workflows.Engine.Executors;

public class DestinationNodeExecutor(DestinationService destinations) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["destination"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var destination = await destinations.GetByIdForExecutionAsync(NodeConfig.Id(context.Config, "OutputDestinationId"), ct)
            ?? throw new NodeExecutionException("Destination not found");
        if (!destination.IsActive)
            throw new NodeExecutionException($"Destination {destination.Name} is not active");

        var input = context.Input;
        var format = NodeConfig.Text(context.Config, "OutputFormat");
        var template = NodeConfig.Text(context.Config, "FilenameTemplate");
        var uploads = new List<WorkflowFile>();
        if (format == WorkflowNodeCatalog.FilesFormat)
        {
            if (input.Files.Count == 0)
                throw new NodeExecutionException("The previous step made no files");
            foreach (var file in input.Files)
            {
                var extension = Path.GetExtension(file.Name).TrimStart('.');
                var name = template.Length == 0 ? file.Name : FileNames.Generate(template, context.Workflow.Name, extension, context.ExecutionId);
                var staged = Stage(context, name);
                File.Copy(file.Path, staged, overwrite: true);
                uploads.Add(await UploadAsync(destination, staged, file.MimeType));
            }
        }
        else
        {
            var extension = OutputFormats.Extension(format);
            var staged = Stage(context, FileNames.Generate(template, context.Workflow.Name, extension, context.ExecutionId));
            var rows = input.Items.Select(r => r.ToDictionary(c => c.Key, c => c.Value ?? DBNull.Value)).ToList();
            var (formatted, _, formatError) = await OutputFormats.Formatter(format).FormatAsync(rows, staged);
            if (!formatted)
                throw new NodeExecutionException(formatError ?? "Formatting failed");
            uploads.Add(await UploadAsync(destination, staged, OutputFormats.MimeType(extension)));
        }
        return NodeResult.Main(new WorkflowItems { Columns = input.Columns, Items = input.Items, Files = uploads });
    }

    private static string Stage(NodeContext context, string name)
    {
        var path = SafePath.Combine(Path.Combine(context.WorkFolder, context.Node.Id), name.Replace('\\', '/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private async Task<WorkflowFile> UploadAsync(Destination destination, string staged, string mimeType)
    {
        var size = new FileInfo(staged).Length;
        var (saved, finalPath, error) = await destinations.SaveToDestinationAsync(staged, destination.Type, destination.ConfigurationJson);
        if (!saved)
            throw new NodeExecutionException($"Destination upload failed: {error}");
        return new WorkflowFile(finalPath ?? staged, Path.GetFileName(staged), mimeType, size);
    }
}
