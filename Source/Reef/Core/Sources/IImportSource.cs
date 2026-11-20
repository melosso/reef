using Reef.Core.Models;

namespace Reef.Core.Sources;

/// <summary>
/// Abstraction for reading data from an import source (Local, SFTP, HTTP, etc.)
/// </summary>
public interface IImportSource
{
    /// <summary>
    /// Fetch one or more files from the source.
    /// The caller is responsible for disposing each ImportSourceFile.Content stream.
    /// </summary>
    Task<List<ImportSourceFile>> FetchAsync(
        ImportSourceOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// List files available at the source (for the UI "browse" button).
    /// </summary>
    Task<List<SourceFileInfo>> ListFilesAsync(
        ImportSourceOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// Move or rename a processed file to the archive location.
    /// </summary>
    Task<bool> ArchiveAsync(
        ImportSourceOptions options,
        string fileIdentifier,
        CancellationToken ct = default);

    /// <summary>
    /// Test connectivity and accessibility of the source.
    /// </summary>
    Task<(bool Success, string? Message)> TestAsync(
        ImportSourceOptions options,
        CancellationToken ct = default);
}
