// Source/Reef/Core/Destinations/SftpDestination.cs
// SFTP file upload destination using SSH.NET

using Renci.SshNet;
using Serilog;
using System.Text;
using System.IO;
using System.Linq;

namespace Reef.Core.Destinations;

/// <summary>
/// Upload files to SFTP servers using SSH.NET library
/// Provides secure file transfer over SSH
/// </summary>
public class SftpDestination : IDestination
{
    /// <summary>
    /// Save file to SFTP destination (implements IDestination)
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

            // Compose final SFTP path/URL
            string? host = config.ContainsKey("host") ? config["host"]?.ToString() : null;
            int port = 22;
            if (config.ContainsKey("port") && int.TryParse(config["port"]?.ToString(), out var p))
                port = p;
            string? remotePath = config.ContainsKey("path") ? config["path"]?.ToString() : null;
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(remotePath))
                return (true, null, null); // uploaded, but can't build URL
            if (!remotePath.EndsWith("/")) remotePath += "/";
            var fullPath = remotePath + fileRelativePath.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
            var sftpUri = $"sftp://{host}:{port}{fullPath}";
            return (true, sftpUri, null);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SFTP SaveAsync failed");
            return (false, null, ex.Message);
        }
    }
    public string Type => "SFTP";

    /// <summary>
    /// Upload file to SFTP server
    /// Config must contain: host, port (optional, default 22), username, password or privateKey, path
    /// </summary>
    public async Task UploadAsync(byte[] data, string filename, Dictionary<string, object> config)
    {
        string? host = null;
        int port = 22;
        string? username = null;
        string? password = null;
        string? privateKey = null;
        string? privateKeyPassphrase = null;
        string? remotePath = null;

        try
        {
            // Extract configuration
            host = config.ContainsKey("host") ? config["host"]?.ToString() : null;
            if (config.ContainsKey("port") && int.TryParse(config["port"]?.ToString(), out var p))
            {
                port = p;
            }
            username = config.ContainsKey("username") ? config["username"]?.ToString() : null;
            password = config.ContainsKey("password") ? config["password"]?.ToString() : null;
            privateKey = config.ContainsKey("privateKey") ? config["privateKey"]?.ToString() : null;
            privateKeyPassphrase = config.ContainsKey("privateKeyPassphrase") ? config["privateKeyPassphrase"]?.ToString() : null;
            remotePath = config.ContainsKey("path") ? config["path"]?.ToString() : null;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException("SFTP host is required");
            }
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new InvalidOperationException("SFTP username is required");
            }
            if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(privateKey))
            {
                throw new InvalidOperationException("SFTP password or private key is required");
            }
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                throw new InvalidOperationException("SFTP path is required");
            }

            // Ensure remote path starts with /
            if (!remotePath.StartsWith("/"))
            {
                remotePath = "/" + remotePath;
            }

            // Ensure remote path ends with /
            if (!remotePath.EndsWith("/"))
            {
                remotePath += "/";
            }

            // Normalize path separators to forward slashes for SFTP
            var normalizedFilename = filename.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');

            // Combine path with filename
            var fullPath = remotePath + normalizedFilename;

            Log.Information("Uploading to SFTP: {Host}:{Port}{Path}", host, port, fullPath);

            // Create connection info
            Renci.SshNet.ConnectionInfo connectionInfo;
            if (!string.IsNullOrWhiteSpace(privateKey))
            {
                // Use private key authentication
                Renci.SshNet.PrivateKeyFile keyFile;
                if (!string.IsNullOrWhiteSpace(privateKeyPassphrase))
                {
                    // Encrypted private key with passphrase
                    keyFile = new Renci.SshNet.PrivateKeyFile(
                        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey)),
                        privateKeyPassphrase);
                    Log.Debug("Using passphrase-protected private key for SFTP authentication");
                }
                else
                {
                    // Unencrypted private key
                    keyFile = new Renci.SshNet.PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey)));
                    Log.Debug("Using private key for SFTP authentication");
                }

                var keyFiles = new[] { keyFile };
                connectionInfo = new Renci.SshNet.ConnectionInfo(host, port, username,
                    new Renci.SshNet.PrivateKeyAuthenticationMethod(username, keyFiles));
            }
            else
            {
                // Use password authentication
                Log.Debug("Using password authentication for SFTP");
                connectionInfo = new Renci.SshNet.ConnectionInfo(host, port, username,
                    new Renci.SshNet.PasswordAuthenticationMethod(username, password));
            }

            // Set connection timeout
            connectionInfo.Timeout = TimeSpan.FromSeconds(30);

            // Create SFTP client and upload
            using var client = new Renci.SshNet.SftpClient(connectionInfo);
            
            await Task.Run(() =>
            {
                client.Connect();

                try
                {
                    // Create directory if it doesn't exist
                    var directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        CreateDirectoryRecursive(client, directory);
                    }

                    // Upload file
                    using var stream = new MemoryStream(data);
                    client.UploadFile(stream, fullPath, true);

                    Log.Information("Successfully uploaded {Filename} to SFTP server {Host}", 
                        filename, host);
                }
                finally
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
            });
        }
        catch (Renci.SshNet.Common.SshException ex)
        {
            Log.Error(ex, "SFTP upload failed to {Host}: {Message}", host, ex.Message);
            throw new InvalidOperationException($"SFTP upload failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error uploading to SFTP server {Host}", host);
            throw;
        }
    }

    /// <summary>
    /// Test SFTP connection
    /// </summary>
    public async Task<bool> TestConnectionAsync(Dictionary<string, object> config)
    {
        try
        {
            var host = config.ContainsKey("host") ? config["host"]?.ToString() : null;
            var port = 22;
            if (config.ContainsKey("port") && int.TryParse(config["port"]?.ToString(), out var p))
            {
                port = p;
            }
            var username = config.ContainsKey("username") ? config["username"]?.ToString() : null;
            var password = config.ContainsKey("password") ? config["password"]?.ToString() : null;
            var privateKey = config.ContainsKey("privateKey") ? config["privateKey"]?.ToString() : null;
            var privateKeyPassphrase = config.ContainsKey("privateKeyPassphrase") ? config["privateKeyPassphrase"]?.ToString() : null;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(privateKey))
            {
                return false;
            }

            Renci.SshNet.ConnectionInfo connectionInfo;
            if (!string.IsNullOrWhiteSpace(privateKey))
            {
                Renci.SshNet.PrivateKeyFile keyFile;
                if (!string.IsNullOrWhiteSpace(privateKeyPassphrase))
                {
                    keyFile = new Renci.SshNet.PrivateKeyFile(
                        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey)),
                        privateKeyPassphrase);
                }
                else
                {
                    keyFile = new Renci.SshNet.PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey)));
                }

                var keyFiles = new[] { keyFile };
                connectionInfo = new Renci.SshNet.ConnectionInfo(host, port, username,
                    new Renci.SshNet.PrivateKeyAuthenticationMethod(username, keyFiles));
            }
            else
            {
                connectionInfo = new Renci.SshNet.ConnectionInfo(host, port, username,
                    new Renci.SshNet.PasswordAuthenticationMethod(username, password));
            }

            connectionInfo.Timeout = TimeSpan.FromSeconds(5);

            return await Task.Run(() =>
            {
                using var client = new Renci.SshNet.SftpClient(connectionInfo);
                try
                {
                    client.Connect();
                    var connected = client.IsConnected;
                    if (connected)
                    {
                        client.Disconnect();
                    }
                    return connected;
                }
                catch
                {
                    return false;
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SFTP connection test failed");
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
    /// Create directory recursively on SFTP server
    /// </summary>
    private void CreateDirectoryRecursive(SftpClient client, string path)
    {
        try
        {
            // Normalize path
            path = path.Replace("\\", "/");
            // Split path into parts
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";
            foreach (var part in parts)
            {
                currentPath += "/" + part;
                try
                {
                    // Check if directory exists
                    if (!client.Exists(currentPath))
                    {
                        client.CreateDirectory(currentPath);
                        Log.Debug("Created SFTP directory: {Directory}", currentPath);
                    }
                }
                catch (Renci.SshNet.Common.SshException ex)
                {
                    // Ignore if directory already exists
                    if (!ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warning(ex, "Could not create SFTP directory: {Directory}", currentPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error creating SFTP directory structure: {Path}", path);
        }
    }

    /// <summary>
    /// List files in SFTP directory
    /// </summary>
    public async Task<IEnumerable<string>> ListFilesAsync(Dictionary<string, object> config, string remotePath)
    {
        string? host = null;
        int port = 22;
        string? username = null;
        string? password = null;
        string? privateKey = null;

        try
        {
            host = config.ContainsKey("host") ? config["host"]?.ToString() : null;
            if (config.ContainsKey("port") && int.TryParse(config["port"]?.ToString(), out var p))
            {
                port = p;
            }
            username = config.ContainsKey("username") ? config["username"]?.ToString() : null;
            password = config.ContainsKey("password") ? config["password"]?.ToString() : null;
            privateKey = config.ContainsKey("privateKey") ? config["privateKey"]?.ToString() : null;
            var privateKeyPassphrase = config.ContainsKey("privateKeyPassphrase") ? config["privateKeyPassphrase"]?.ToString() : null;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
            {
                return Enumerable.Empty<string>();
            }

            Renci.SshNet.ConnectionInfo connectionInfo;
            if (!string.IsNullOrWhiteSpace(privateKey))
            {
                Renci.SshNet.PrivateKeyFile keyFile;
                if (!string.IsNullOrWhiteSpace(privateKeyPassphrase))
                {
                    keyFile = new Renci.SshNet.PrivateKeyFile(
                        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey)),
                        privateKeyPassphrase);
                }
                else
                {
                    keyFile = new Renci.SshNet.PrivateKeyFile(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(privateKey)));
                }

                var keyFiles = new[] { keyFile };
                connectionInfo = new Renci.SshNet.ConnectionInfo(host, port, username,
                    new Renci.SshNet.PrivateKeyAuthenticationMethod(username, keyFiles));
            }
            else
            {
                connectionInfo = new Renci.SshNet.ConnectionInfo(host, port, username,
                    new Renci.SshNet.PasswordAuthenticationMethod(username, password));
            }

            connectionInfo.Timeout = TimeSpan.FromSeconds(10);

            return await Task.Run(() =>
            {
                using var client = new SftpClient(connectionInfo);
                client.Connect();

                try
                {
                    if (!client.Exists(remotePath))
                    {
                        return Enumerable.Empty<string>();
                    }

                    var files = client.ListDirectory(remotePath)
                        .Where(f => !f.IsDirectory)
                        .Select(f => f.Name)
                        .ToList();

                    return files.AsEnumerable();
                }
                finally
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error listing SFTP files from {Host}:{Path}", host, remotePath);
            return Enumerable.Empty<string>();
        }
    }
}