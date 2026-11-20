using Reef.Core.Formatters;
using Reef.Core.Services;
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

        var format = NodeConfig.Text(context.Config, "OutputFormat");
        var extension = OutputFormats.Extension(format);
        var name = FileNames.Generate(NodeConfig.Text(context.Config, "FilenameTemplate"), context.Workflow.Name, extension, context.ExecutionId)
            .Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var temp = SafePath.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        try
        {
            var input = context.Input;
            var rows = input.Items.Select(r => r.ToDictionary(c => c.Key, c => c.Value ?? DBNull.Value)).ToList();
            var (formatted, size, formatError) = await OutputFormats.Formatter(format).FormatAsync(rows, temp);
            if (!formatted)
                throw new NodeExecutionException(formatError ?? "Formatting failed");

            var (saved, finalPath, saveError) = await destinations.SaveToDestinationAsync(temp, destination.Type, destination.ConfigurationJson);
            if (!saved)
                throw new NodeExecutionException($"Destination upload failed: {saveError}");

            var file = new WorkflowFile(finalPath ?? name, Path.GetFileName(name), OutputFormats.MimeType(extension), size);
            return NodeResult.Main(new WorkflowItems { Columns = input.Columns, Items = input.Items, Files = [.. input.Files, file] });
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
