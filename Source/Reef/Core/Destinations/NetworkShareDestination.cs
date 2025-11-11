using System.Text.Json;
using System.Runtime.InteropServices;
using Serilog;

namespace Reef.Core.Destinations;

/// <summary>
/// Network share destination - copies files to UNC paths (Windows SMB/CIFS shares)
/// Supports both Windows network shares and NFS mounts
/// </summary>
public class NetworkShareDestination : IDestination
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NetworkShareDestination>();

    /// <summary>
    /// Save file to network share destination
    /// </summary>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(
        string sourcePath, 
        string destinationConfig)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return (false, null, "Source file not found");
            }

            var config = JsonSerializer.Deserialize<NetworkShareConfiguration>(destinationConfig);
            if (config == null)
            {
                return (false, null, "Invalid network share configuration");
            }

            // Handle both UncPath and BasePath for backward compatibility
            if (string.IsNullOrEmpty(config.UncPath) && !string.IsNullOrEmpty(config.BasePath))
            {
                config.UncPath = config.BasePath;
                Log.Debug("Using BasePath as UncPath: {UncPath}", config.UncPath);
            }

            // Validate configuration
            if (string.IsNullOrEmpty(config.UncPath))
            {
                return (false, null, "UNC path is required (e.g., \\\\server\\share or /mnt/nfs)");
            }

            // Build full destination path
            var uncPath = config.UncPath.TrimEnd('\\', '/');

            // Extract relative path from temp file (preserves subdirectories from filename template)
            var fileRelativePath = ExtractRelativePathFromTemp(sourcePath);

            // Build base destination path
            string destinationPath;
            if (!string.IsNullOrEmpty(config.SubFolder))
            {
                var subFolder = config.SubFolder.TrimStart('\\', '/');

                // Handle relative path if UseRelativePath is true
                // (For network shares, relative paths are relative to the share root)
                if (config.UseRelativePath)
                {
                    // For relative paths, we may want to resolve based on date/time
                    // For now, just clean and use as-is relative to share root
                    subFolder = subFolder.TrimStart('.', '/', '\\');
                    Log.Debug("Using relative subfolder path: {SubFolder}", subFolder);
                }

                // Combine: UNC path + subfolder + relative file path (includes subdirectories from template)
                destinationPath = Path.Combine(uncPath, subFolder, fileRelativePath);
            }
            else
            {
                // Combine: UNC path + relative file path (includes subdirectories from template)
                destinationPath = Path.Combine(uncPath, fileRelativePath);
            }

            Log.Information("Copying file to network share: {DestinationPath}", destinationPath);

            // Authenticate if credentials provided (Windows only)
            if (!string.IsNullOrEmpty(config.Username) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Note: For production, consider using WNetAddConnection2 via P/Invoke
                // For now, we assume the network share is already accessible or credentials are cached
                Log.Debug("Network share authentication with username: {Username}", config.Username);
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                if (!Directory.Exists(directory))
                {
                    Log.Debug("Creating directory: {Directory}", directory);
                    Directory.CreateDirectory(directory);
                }
            }

            // Copy file with retry logic
            var maxRetries = config.RetryCount ?? 3;
            var retryDelay = config.RetryDelayMs ?? 1000;
            Exception? lastException = null;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        Log.Debug("Retry attempt {Attempt} of {MaxRetries}", attempt + 1, maxRetries);
                        await Task.Delay(retryDelay);
                    }

                    // Use async file copy with buffering
                    using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                    using (var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        await sourceStream.CopyToAsync(destStream);
                    }

                    // Verify file was written
                    var destFileInfo = new FileInfo(destinationPath);
                    if (!destFileInfo.Exists)
                    {
                        throw new IOException("File copy completed but destination file not found");
                    }

                    var sourceFileInfo = new FileInfo(sourcePath);
                    if (destFileInfo.Length != sourceFileInfo.Length)
                    {
                        throw new IOException($"File size mismatch: source={sourceFileInfo.Length}, destination={destFileInfo.Length}");
                    }

                    Log.Information("Successfully copied {Bytes} bytes to network share: {Destination}", 
                        destFileInfo.Length, destinationPath);

                    return (true, destinationPath, null);
                }
                catch (IOException ex) when (attempt < maxRetries - 1)
                {
                    Log.Warning(ex, "Network share copy failed, attempt {Attempt} of {MaxRetries}", 
                        attempt + 1, maxRetries);
                    lastException = ex;
                }
                catch (UnauthorizedAccessException ex) when (attempt < maxRetries - 1)
                {
                    Log.Warning(ex, "Access denied to network share, attempt {Attempt} of {MaxRetries}", 
                        attempt + 1, maxRetries);
                    lastException = ex;
                }
            }

            // All retries exhausted
            var errorMessage = lastException != null 
                ? $"Network share copy failed after {maxRetries} attempts: {lastException.Message}"
                : "Network share copy failed";

            Log.Error(lastException, errorMessage);
            return (false, null, errorMessage);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Network share copy failed: {Error}", ex.Message);
            return (false, null, $"Network share copy failed: {ex.Message}");
        }
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
}

/// <summary>
/// Network share destination configuration
/// </summary>
public class NetworkShareConfiguration
{
    /// <summary>
    /// UNC path to network share (e.g., \\server\share or /mnt/nfs for NFS mounts)
    /// </summary>
    public string UncPath { get; set; } = string.Empty;

    /// <summary>
    /// Alternative to UncPath for backward compatibility with DestinationConfiguration
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// Optional subfolder within the share
    /// </summary>
    public string? SubFolder { get; set; }

    /// <summary>
    /// If true, SubFolder is treated as relative path (relative to share root)
    /// </summary>
    public bool UseRelativePath { get; set; } = false;

    /// <summary>
    /// Username for authentication (Windows SMB only)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for authentication (Windows SMB only)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Domain for authentication (Windows SMB only)
    /// </summary>
    public string? Domain { get; set; }

    /// <summary>
    /// Number of retry attempts for network issues (default: 3)
    /// </summary>
    public int? RetryCount { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds (default: 1000)
    /// </summary>
    public int? RetryDelayMs { get; set; } = 1000;
}
