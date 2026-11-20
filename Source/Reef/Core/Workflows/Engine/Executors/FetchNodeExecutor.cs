using Reef.Core.Models;
using Reef.Core.Sources;
using Reef.Core.Workflows.Models;
using Reef.Helpers;

namespace Reef.Core.Workflows.Engine.Executors;

public class FetchNodeExecutor(DestinationSourceAdapter sourceAdapter) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["fetch"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var config = context.Config;
        var profile = await sourceAdapter.ResolveSourceConfigAsync(new ImportSourceOptions
        {
            Name = context.Workflow.Name,
            SourceType = NodeConfig.Text(config, "From"),
            SourceConfig = config["Settings"]?.ToJsonString(),
            SourceFilePath = NodeConfig.Text(config, "Path"),
            SourceFilePattern = NodeConfig.Text(config, "Pattern") is { Length: > 0 } pattern ? pattern : null,
            SourceFileSelection = NodeConfig.Text(config, "Pick") is { Length: > 0 } pick ? pick : "Latest",
            ArchiveAfterImport = NodeConfig.Flag(config, "Archive"),
            ArchivePath = NodeConfig.Text(config, "ArchiveFolder"),
            HttpMethod = NodeConfig.Text(config, "Method") is { Length: > 0 } method ? method : "GET",
            HttpBodyTemplate = NodeConfig.Text(config, "Body"),
            HttpPaginationEnabled = NodeConfig.Flag(config, "Paginate"),
            HttpPaginationConfig = NodeConfig.Text(config, "Pagination") is { Length: > 0 } pagination ? pagination : null,
            HttpDataRootPath = NodeConfig.Text(config, "RecordsPath")
        }, ct);
        var source = ImportSourceFactory.Create(profile.SourceType);

        var files = new List<WorkflowFile>();
        var fetchedIds = new List<string>();
        var folder = Directory.CreateDirectory(Path.Combine(context.WorkFolder, context.Node.Id)).FullName;
        foreach (var fetched in await source.FetchAsync(profile, ct))
        {
            await using var content = fetched.Content;
            var name = Path.GetFileName(fetched.Identifier.TrimEnd('/', '\\')) is { Length: > 0 } file ? file : $"{Guid.NewGuid():N}.dat";
            var path = SafePath.Combine(folder, $"{files.Count}_{name}");
            await using (var target = File.Create(path))
                await content.CopyToAsync(target, ct);
            fetchedIds.Add(fetched.Identifier);
            files.Add(new WorkflowFile(path, name, fetched.ContentType ?? "application/octet-stream", new FileInfo(path).Length));
        }

        return new NodeResult
        {
            Outputs = { [WorkflowPorts.Main] = new WorkflowItems { Files = files } },
            OnRunSuccess = profile.ArchiveAfterImport ? () => ArchiveAsync(source, profile, fetchedIds) : null
        };
    }

    private static async Task ArchiveAsync(IImportSource source, ImportSourceOptions profile, List<string> identifiers)
    {
        foreach (var identifier in identifiers)
            await source.ArchiveAsync(profile, identifier);
    }
}
