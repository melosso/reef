// Source/Reef/Core/Destinations/FtpDestination.cs
// FTP/FTPS file upload destination

using Serilog;
using System.Net;
using System.Text;
using FluentFTP;
using FluentFTP.Exceptions;

namespace Reef.Core.Destinations;

/// <summary>
/// Upload files to FTP/FTPS servers
/// Supports both FTP and FTPS (explicit SSL/TLS)
/// </summary>
public class FtpDestination : IDestination
{
    /// <summary>
    /// Save file to FTP/FTPS destination (implements IDestination)
    /// </summary>
    /// <param name="sourcePath">Path to the source file to upload</param>
    /// <param name="destinationConfig">JSON configuration for the destination</param>
    /// <returns>Tuple containing success status, final path/URL, and error message if failed</returns>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(string sourcePath, string destinationConfig)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return (false, null, $"Source file not found: {sourcePath}");
            }

            // Parse config (assume JSON to Dictionary<string, object>)
            var config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(destinationConfig);
            if (config == null)
            {
                return (false, null, "Invalid destination configuration");
            }

            var data = await File.ReadAllBytesAsync(sourcePath);

            // Extract relative path from temp file (preserves subdirectories from filename template)
            var fileRelativePath = ExtractRelativePathFromTemp(sourcePath);

            await UploadAsync(data, fileRelativePath, config);

            // Compose final FTP path/URL
            string? host = config.ContainsKey("host") ? config["host"]?.ToString() : null;
            int port = 21;
            if (config.ContainsKey("port") && int.TryParse(config["port"]?.ToString(), out var p))
                port = p;
            string? remotePath = config.ContainsKey("path") ? config["path"]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(remotePath))
                return (true, null, null); // uploaded, but can't build URL
            if (!remotePath.EndsWith("/")) remotePath += "/";
            var fullPath = remotePath + fileRelativePath.Replace(Path.DirectorySeparatorChar, '/');
            var ftpUri = $"ftp://{host}:{port}{fullPath}";
            return (true, ftpUri, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FTP SaveAsync failed");
            return (false, null, ex.Message);
        }
    }
    public string Type => "FTP";

    /// <summary>
    /// Upload file to FTP server
    /// Config must contain: host, port (optional, default 21), username, password, path, useSsl (optional, default false)
    /// </summary>
    public async Task UploadAsync(byte[] data, string filename, Dictionary<string, object> config)
    {
        string? host = config.ContainsKey("host") ? config["host"]?.ToString() : null;
        int port = 21;
        if (config.ContainsKey("port") && int.TryParse(config["port"]?.ToString(), out var p))
            port = p;
        string? username = config.ContainsKey("username") ? config["username"]?.ToString() : null;
        string? password = config.ContainsKey("password") ? config["password"]?.ToString() : null;
        string? remotePath = config.ContainsKey("path") ? config["path"]?.ToString() : null;
        bool useSsl = false;
        if (config.ContainsKey("useSsl") && bool.TryParse(config["useSsl"]?.ToString(), out var ssl))
            useSsl = ssl;

        // Validate required fields
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("FTP host is required");
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("FTP username is required");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("FTP password is required");
        if (string.IsNullOrWhiteSpace(remotePath))
            throw new InvalidOperationException("FTP path is required");

        if (!remotePath.EndsWith("/"))
            remotePath += "/";

        // Normalize path separators to forward slashes for FTP
        var normalizedFilename = filename.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
        var fullPath = remotePath + normalizedFilename;

        Log.Information("Uploading to FTP: {Host}:{Port}{Path} (SSL: {UseSsl})", host, port, fullPath, useSsl);

        var ftp = new AsyncFtpClient();
        ftp.Host = host;
        ftp.Port = port;
        ftp.Credentials = new System.Net.NetworkCredential(username, password);
        if (useSsl)
        {
            ftp.Config.EncryptionMode = FtpEncryptionMode.Explicit;
            ftp.Config.ValidateAnyCertificate = true; // Accept all certs if SSL (for demo; production should validate)
        }
        await ftp.Connect();

        // Ensure directory exists (including subdirectories from filename)
        var fullDirectory = remotePath.TrimEnd('/');
        var filenameDirectory = System.IO.Path.GetDirectoryName(normalizedFilename)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(filenameDirectory))
        {
            fullDirectory = fullDirectory + "/" + filenameDirectory;
        }
        await ftp.CreateDirectory(fullDirectory);

        // Upload file
        var status = await ftp.UploadBytes(data, fullPath, FtpRemoteExists.Overwrite, true);
        if (status == FtpStatus.Success)
        {
            Log.Information("Successfully uploaded {Filename} to FTP server {Host}", filename, host);
        }
        else
        {
            Log.Warning("FTP upload completed with status: {Status}", status);
        }
        await ftp.Disconnect();
    }

    /// <summary>
    /// Test FTP connection
    /// </summary>
    public async Task<bool> TestConnectionAsync(Dictionary<string, object> config)
    {
        try
        {
            var host = config.ContainsKey("host") ? config["host"]?.ToString() : null;
            var port = 21;
            if (config.ContainsKey("port") && int.TryParse(config["port"]?.ToString(), out var p))
                port = p;
            var username = config.ContainsKey("username") ? config["username"]?.ToString() : null;
            var password = config.ContainsKey("password") ? config["password"]?.ToString() : null;
            var useSsl = false;
            if (config.ContainsKey("useSsl") && bool.TryParse(config["useSsl"]?.ToString(), out var ssl))
                useSsl = ssl;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return false;

            var ftp = new AsyncFtpClient();
            ftp.Host = host;
            ftp.Port = port;
            ftp.Credentials = new System.Net.NetworkCredential(username, password);
            if (useSsl)
            {
                ftp.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                ftp.Config.ValidateAnyCertificate = true;
            }
            await ftp.Connect();
            bool success = await ftp.DirectoryExists("/");
            await ftp.Disconnect();
            return success;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FTP connection test failed");
            return false;
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

    /// <summary>
    /// Create directory on FTP server if it doesn't exist
    /// </summary>
    private async Task CreateDirectoryIfNotExistsAsync(string host, int port, string username,
        string password, string directory, bool useSsl)
    {
        try
        {
            var ftp = new AsyncFtpClient();
            ftp.Host = host;
            ftp.Port = port;
            ftp.Credentials = new System.Net.NetworkCredential(username, password);
            if (useSsl)
            {
                ftp.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                ftp.Config.ValidateAnyCertificate = true;
            }
            await ftp.Connect();
            await ftp.CreateDirectory(directory);
            Log.Debug("Created FTP directory: {Directory}", directory);
            await ftp.Disconnect();
        }
        catch (FtpCommandException ex) when (ex.CompletionCode == "550")
        {
            // Directory already exists
            Log.Debug("FTP directory already exists: {Directory}", directory);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not create FTP directory: {Directory}", directory);
        }
    }
}