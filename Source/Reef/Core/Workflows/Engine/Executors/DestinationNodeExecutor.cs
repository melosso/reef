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
        var destination = await destinations.GetByIdForExecutionAsync(NodeConfig.Id(context.Config, "DestinationId"), ct)
            ?? throw new NodeExecutionException("Destination not found");
        if (!destination.IsActive)
            throw new NodeExecutionException($"Destination {destination.Name} is not active");

        var settings = destination.ConfigurationJson;
        if (NodeConfig.Text(context.Config, "EndpointId") is { Length: > 0 })
        {
            var endpoint = await destinations.GetEndpointByIdAsync(NodeConfig.Id(context.Config, "EndpointId"), ct)
                ?? throw new NodeExecutionException("Endpoint not found");
            if (endpoint.DestinationId != destination.Id)
                throw new NodeExecutionException($"Endpoint {endpoint.Name} belongs to another destination");
            settings = destinations.MergeWithEndpoint(destination, endpoint);
        }

        var input = context.Input;
        var format = NodeConfig.Text(context.Config, "Format");
        var template = NodeConfig.Text(context.Config, "FileName");
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
                uploads.Add(await UploadAsync(destination, settings, staged, file.MimeType));
            }
        }
        else
        {
            var extension = OutputFormats.Extension(format);
            foreach (var batch in input.Batches())
            {
                var name = FileNames.Generate(batch.SplitKey is null ? template : input.Split!.FilenameTemplate ?? template,
                    context.Workflow.Name, extension, context.ExecutionId, batch.SplitKey);
                var staged = Stage(context, name);
                var (formatted, _, formatError) = await OutputFormats.Formatter(format).FormatAsync(batch.Rows, staged);
                if (!formatted)
                    throw new NodeExecutionException(formatError ?? "Formatting failed");
                uploads.Add(await UploadAsync(destination, settings, staged, OutputFormats.MimeType(extension)));
            }
        }
        return new NodeResult
        {
            Outputs = { [WorkflowPorts.Main] = input.WithFiles(uploads) },
            OnRunFailure = NodeConfig.Flag(context.Config, "RemoveOnFailure") ? () => RemoveAsync(destination, settings, uploads) : null
        };
    }

    private async Task RemoveAsync(Destination destination, string settings, List<WorkflowFile> uploads)
    {
        foreach (var file in uploads)
        {
            var (removed, error) = await destinations.CompensateExportAsync(file.Path, destination.Type, settings);
            if (!removed)
                throw new NodeExecutionException($"Could not remove {file.Name}: {error}");
        }
    }

    private static string Stage(NodeContext context, string name)
    {
        var path = SafePath.Combine(Path.Combine(context.WorkFolder, context.Node.Id), name.Replace('\\', '/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private async Task<WorkflowFile> UploadAsync(Destination destination, string settings, string staged, string mimeType)
    {
        var size = new FileInfo(staged).Length;
        var (saved, finalPath, error) = await destinations.SaveToDestinationAsync(staged, destination.Type, settings);
        if (!saved)
            throw new NodeExecutionException($"Destination upload failed: {error}");
        return new WorkflowFile(finalPath ?? staged, Path.GetFileName(staged), mimeType, size);
    }
}
