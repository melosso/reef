using Reef.Legacy.Models;

namespace Reef.Core.Models;

public record ImportSourceOptions
{
    public string Name { get; init; } = "";
    public string SourceType { get; init; } = "Local";
    public int? SourceDestinationId { get; init; }
    public string? SourceConfig { get; init; }
    public string? SourceFilePath { get; init; }
    public string? SourceFilePattern { get; init; }
    public string SourceFileSelection { get; init; } = "Latest";
    public bool ArchiveAfterImport { get; init; }
    public string? ArchivePath { get; init; }
    public string? HttpMethod { get; init; } = "GET";
    public string? HttpBodyTemplate { get; init; }
    public bool HttpPaginationEnabled { get; init; }
    public string? HttpPaginationConfig { get; init; }
    public string? HttpDataRootPath { get; init; }
    public string? FormatConfig { get; init; }

    public static ImportSourceOptions From(ImportProfile profile) => new()
    {
        Name = profile.Name,
        SourceType = profile.SourceType,
        SourceDestinationId = profile.SourceDestinationId,
        SourceConfig = profile.SourceConfig,
        SourceFilePath = profile.SourceFilePath,
        SourceFilePattern = profile.SourceFilePattern,
        SourceFileSelection = profile.SourceFileSelection,
        ArchiveAfterImport = profile.ArchiveAfterImport,
        ArchivePath = profile.ArchivePath,
        HttpMethod = profile.HttpMethod,
        HttpBodyTemplate = profile.HttpBodyTemplate,
        HttpPaginationEnabled = profile.HttpPaginationEnabled,
        HttpPaginationConfig = profile.HttpPaginationConfig,
        HttpDataRootPath = profile.HttpDataRootPath,
        FormatConfig = profile.FormatConfig
    };
}
