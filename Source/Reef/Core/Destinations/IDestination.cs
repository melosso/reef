namespace Reef.Core.Destinations;

/// <summary>
/// Interface for destination handlers
/// Implementations handle saving exported files to different destinations
/// </summary>
public interface IDestination
{
    /// <summary>
    /// Save file to destination
    /// </summary>
    /// <param name="sourcePath">Path to the source file to upload/copy</param>
    /// <param name="destinationConfig">JSON configuration for the destination</param>
    /// <returns>Tuple containing success status, final path/URL, and error message if failed</returns>
    Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(
        string sourcePath, 
        string destinationConfig);
}