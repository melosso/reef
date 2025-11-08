using Serilog;
using System.Text.Json;

namespace Reef.Core.Destinations;

/// <summary>
/// Local file system destination handler
/// Saves exported files to the local file system with configurable paths
/// </summary>
public class LocalDestination : IDestination
{
    /// <summary>
    /// Save file to local file system
    /// </summary>
    /// <param name="sourcePath">Path to the source file</param>
    /// <param name="destinationConfig">JSON configuration with 'path' property</param>
    /// <returns>Success status, final file path, and error message</returns>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(
        string sourcePath, 
        string destinationConfig)
    {
        return await Task.Run<(bool, string?, string?)>(() =>
        {
            try
            {
                // Parse configuration
                var config = JsonDocument.Parse(destinationConfig);
                var root = config.RootElement;

                if (!root.TryGetProperty("path", out var pathElement))
                {
                    return (false, null, "Destination configuration missing 'path' property");
                }

                var destinationPath = pathElement.GetString();
                if (string.IsNullOrEmpty(destinationPath))
                {
                    return (false, null, "Destination path is empty");
                }

                // Extract relative path from source (preserves subdirectories from filename template)
                var sourceRelativePath = ExtractRelativePathFromTemp(sourcePath);
                Log.Debug("Extracted relative path from temp: {RelativePath}", sourceRelativePath);

                // Get file extension and filename from the full relative path
                var sourceExtension = Path.GetExtension(sourceRelativePath);
                var sourceFileName = Path.GetFileNameWithoutExtension(Path.GetFileName(sourceRelativePath));

                // Check if destination path is a directory or includes a filename
                bool isDirectory = !Path.HasExtension(destinationPath) &&
                                  (destinationPath.EndsWith("/") || destinationPath.EndsWith("\\") ||
                                   !destinationPath.Contains("{"));

                if (isDirectory)
                {
                    // Destination is just a directory - append the full relative path (includes subdirs)
                    destinationPath = Path.Combine(destinationPath, sourceRelativePath);
                    Log.Debug("Destination is directory, combined path: {DestinationPath}", destinationPath);
                }
                else
                {
                    // Destination includes filename template - replace placeholders but preserve subdirs from source
                    destinationPath = ReplacePlaceholders(destinationPath, sourceExtension, sourceFileName);

                    // If source has subdirectories, inject them into the destination path
                    var sourceDir = Path.GetDirectoryName(sourceRelativePath);
                    if (!string.IsNullOrEmpty(sourceDir))
                    {
                        var destDir = Path.GetDirectoryName(destinationPath);
                        var destFile = Path.GetFileName(destinationPath);
                        destinationPath = Path.Combine(destDir ?? "", sourceDir, destFile);
                    }
                    Log.Debug("Destination is file template, resolved path: {DestinationPath}", destinationPath);
                }

                Log.Debug("Final destination path: {DestinationPath}", destinationPath);

                // Ensure destination directory exists
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Log.Debug("Created directory: {Directory}", directory);
                }

                // Copy file to destination
                File.Copy(sourcePath, destinationPath, overwrite: true);

                // Get absolute path
                var fullPath = Path.GetFullPath(destinationPath);

                Log.Information("File saved to local destination: {FullPath}", fullPath);
                return (true, fullPath, null);
            }
            catch (JsonException ex)
            {
                Log.Error(ex, "Invalid destination configuration JSON");
                return (false, null, "Invalid destination configuration: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving to local destination");
                return (false, null, ex.Message);
            }
        });
    }

    /// <summary>
    /// Extract relative path from temp file path
    /// E.g., "C:\Temp\1\csv\file.csv" -> "csv\file.csv" (strips process folder)
    /// E.g., "C:\Temp\reef_splits\orders\order_A.csv" -> "orders\order_A.csv"
    /// </summary>
    private string ExtractRelativePathFromTemp(string sourcePath)
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var tempPathNormalized = Path.GetFullPath(tempPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Handle reef_splits subdirectory
            var reefSplitsPath = Path.Combine(tempPath, "reef_splits");
            if (sourcePath.StartsWith(reefSplitsPath, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath.Substring(reefSplitsPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            // Handle regular temp files
            if (sourcePath.StartsWith(tempPathNormalized, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = sourcePath.Substring(tempPathNormalized.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Windows .NET temp path often has process-specific folders like "\1\", "\2\", etc.
                // Strip the first folder if it's a single digit (process isolation folder)
                var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1 && parts[0].Length <= 2 && int.TryParse(parts[0], out _))
                {
                    // Skip the first part (process folder) and rejoin
                    return string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(1));
                }

                return relativePath;
            }

            // Fallback: just return the filename
            return Path.GetFileName(sourcePath);
        }
        catch
        {
            // If anything goes wrong, fall back to just the filename
            return Path.GetFileName(sourcePath);
        }
    }

    /// <summary>
    /// Replace placeholders in destination path
    /// Supported placeholders:
    /// - {date}: yyyy-MM-dd
    /// - {time}: HHmmss
    /// - {timestamp}: HHmmss
    /// - {datetime}: yyyyMMdd_HHmmss
    /// - {profile}: profile name (from source filename)
    /// - {format}: file extension
    /// - {guid}: new GUID
    /// - {year}, {month}, {day}, {hour}, {minute}, {second}
    /// </summary>
    private string ReplacePlaceholders(string path, string extension, string profileName)
    {
        var now = DateTime.Now;

        // Replace date/time placeholders
        path = path.Replace("{date}", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{time}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{timestamp}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{datetime}", now.ToString("yyyyMMdd_HHmmss"), StringComparison.OrdinalIgnoreCase);
        
        // Replace individual date/time components
        path = path.Replace("{year}", now.ToString("yyyy"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{month}", now.ToString("MM"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{day}", now.ToString("dd"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{hour}", now.ToString("HH"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{minute}", now.ToString("mm"), StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{second}", now.ToString("ss"), StringComparison.OrdinalIgnoreCase);

        // Replace profile name
        path = path.Replace("{profile}", profileName, StringComparison.OrdinalIgnoreCase);

        // Replace format/extension
        var formatExtension = extension.TrimStart('.');
        path = path.Replace("{format}", formatExtension, StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{extension}", formatExtension, StringComparison.OrdinalIgnoreCase);

        // Replace GUID
        if (path.Contains("{guid}", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Replace("{guid}", Guid.NewGuid().ToString("N"), StringComparison.OrdinalIgnoreCase);
        }

        // Normalize path separators
        path = path.Replace('/', Path.DirectorySeparatorChar);
        path = path.Replace('\\', Path.DirectorySeparatorChar);

        return path;
    }
}