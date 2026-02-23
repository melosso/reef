using Reef.Core.Models;
using Renci.SshNet;
using Serilog;

namespace Reef.Core.Sources;

/// <summary>
/// Reads files from an SFTP server using SSH.NET.
/// Reuses credential patterns from SftpDestination.
/// </summary>
public class SftpImportSource : IImportSource
{
    public async Task<List<ImportSourceFile>> FetchAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var config = ParseConfig(profile);
        var files = await ResolveRemoteFilesAsync(config, profile.SourceFileSelection, profile.SourceFilePattern, ct);
        var result = new List<ImportSourceFile>();

        foreach (var remotePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var stream = await DownloadFileAsync(config, remotePath, ct);
                result.Add(new ImportSourceFile
                {
                    Identifier = remotePath,
                    Content = stream
                });
                Log.Debug("SftpImportSource: downloaded {RemotePath}", remotePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SftpImportSource: failed to download {RemotePath}", remotePath);
                throw new InvalidOperationException($"SFTP download failed for '{remotePath}': {ex.Message}", ex);
            }
        }

        return result;
    }

    public async Task<List<SourceFileInfo>> ListFilesAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var config = ParseConfig(profile);
        var remotePath = config.RemotePath ?? "/";

        return await Task.Run(() =>
        {
            using var client = BuildClient(config);
            client.Connect();
            try
            {
                if (!client.Exists(remotePath)) return new List<SourceFileInfo>();

                var pattern = string.IsNullOrWhiteSpace(profile.SourceFilePattern) ? "*" : profile.SourceFilePattern;
                return client.ListDirectory(remotePath)
                    .Where(f => !f.IsDirectory && MatchesPattern(f.Name, pattern))
                    .Select(f => new SourceFileInfo
                    {
                        Name = f.Name,
                        Path = f.FullName,
                        SizeBytes = f.Length,
                        LastModified = f.LastWriteTime.ToUniversalTime()
                    })
                    .OrderByDescending(f => f.LastModified)
                    .ToList();
            }
            finally
            {
                if (client.IsConnected) client.Disconnect();
            }
        }, ct);
    }

    public async Task<bool> ArchiveAsync(
        ImportProfile profile,
        string fileIdentifier,
        CancellationToken ct = default)
    {
        var config = ParseConfig(profile);
        var archivePath = string.IsNullOrWhiteSpace(profile.ArchivePath)
            ? Path.Combine(Path.GetDirectoryName(fileIdentifier)!, "archive").Replace('\\', '/')
            : profile.ArchivePath.Replace('\\', '/');

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var newName = Path.GetFileNameWithoutExtension(fileIdentifier)
                      + $"_{timestamp}"
                      + Path.GetExtension(fileIdentifier);
        var destPath = $"{archivePath.TrimEnd('/')}/{newName}";

        return await Task.Run(() =>
        {
            using var client = BuildClient(config);
            client.Connect();
            try
            {
                if (!client.Exists(archivePath))
                    client.CreateDirectory(archivePath);

                client.RenameFile(fileIdentifier, destPath);
                Log.Information("SftpImportSource: archived {Source} → {Dest}", fileIdentifier, destPath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SftpImportSource: archive failed for {Source}", fileIdentifier);
                return false;
            }
            finally
            {
                if (client.IsConnected) client.Disconnect();
            }
        }, ct);
    }

    public async Task<(bool Success, string? Message)> TestAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var config = ParseConfig(profile);

        return await Task.Run(() =>
        {
            try
            {
                using var client = BuildClient(config);
                client.Connect();
                var connected = client.IsConnected;
                if (connected)
                {
                    var remotePath = config.RemotePath ?? "/";
                    var exists = client.Exists(remotePath);
                    client.Disconnect();
                    return exists
                        ? (true, $"Connected. Remote path '{remotePath}' exists.")
                        : (true, $"Connected but remote path '{remotePath}' not found.");
                }
                return (false, "Failed to connect");
            }
            catch (Exception ex)
            {
                return (false, $"SFTP test failed: {ex.Message}");
            }
        }, ct);
    }

    // ── Private helpers ──────────────────────────────────────

    private async Task<MemoryStream> DownloadFileAsync(SftpConfig config, string remotePath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var client = BuildClient(config);
            client.Connect();
            try
            {
                var ms = new MemoryStream();
                client.DownloadFile(remotePath, ms);
                ms.Position = 0;
                return ms;
            }
            finally
            {
                if (client.IsConnected) client.Disconnect();
            }
        }, ct);
    }

    private async Task<List<string>> ResolveRemoteFilesAsync(
        SftpConfig config,
        string fileSelection,
        string? pattern,
        CancellationToken ct)
    {
        var remotePath = config.RemotePath ?? "/";
        if (!string.IsNullOrWhiteSpace(config.ExactFile))
            return new List<string> { config.ExactFile };

        return await Task.Run(() =>
        {
            using var client = BuildClient(config);
            client.Connect();
            try
            {
                if (!client.Exists(remotePath)) return new List<string>();

                var glob = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
                var files = client.ListDirectory(remotePath)
                    .Where(f => !f.IsDirectory && MatchesPattern(f.Name, glob))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                if (files.Count == 0) return new List<string>();

                return fileSelection switch
                {
                    "All" => files.Select(f => f.FullName).ToList(),
                    "Oldest" => new List<string> { files.Last().FullName },
                    _ => new List<string> { files.First().FullName } // Latest
                };
            }
            finally
            {
                if (client.IsConnected) client.Disconnect();
            }
        }, ct);
    }

    private static SftpClient BuildClient(SftpConfig config)
    {
        Renci.SshNet.ConnectionInfo connectionInfo;

        if (!string.IsNullOrWhiteSpace(config.PrivateKey))
        {
            var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(config.PrivateKey));
            var keyFile = !string.IsNullOrWhiteSpace(config.PrivateKeyPassphrase)
                ? new PrivateKeyFile(keyStream, config.PrivateKeyPassphrase)
                : new PrivateKeyFile(keyStream);

            connectionInfo = new Renci.SshNet.ConnectionInfo(config.Host!, config.Port, config.Username!,
                new PrivateKeyAuthenticationMethod(config.Username!, keyFile));
        }
        else
        {
            connectionInfo = new Renci.SshNet.ConnectionInfo(config.Host!, config.Port, config.Username!,
                new PasswordAuthenticationMethod(config.Username!, config.Password));
        }

        connectionInfo.Timeout = TimeSpan.FromSeconds(30);
        return new SftpClient(connectionInfo);
    }

    private SftpConfig ParseConfig(ImportProfile profile)
    {
        // Try inline SourceConfig first, then fall back to stored destination config
        var json = profile.SourceConfig ?? "{}";
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? Get(string key) =>
            root.TryGetProperty(key, out var el) ? el.GetString() : null;

        int port = 22;
        if (root.TryGetProperty("port", out var portEl) && portEl.TryGetInt32(out int p)) port = p;

        return new SftpConfig
        {
            Host = Get("host"),
            Port = port,
            Username = Get("username"),
            Password = Get("password"),
            PrivateKey = Get("privateKey"),
            PrivateKeyPassphrase = Get("privateKeyPassphrase"),
            RemotePath = Get("path") ?? Get("remotePath"),
            ExactFile = profile.SourceFilePath
        };
    }

    private static bool MatchesPattern(string filename, string pattern)
    {
        if (pattern == "*") return true;
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(filename, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private sealed class SftpConfig
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 22;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? PrivateKey { get; set; }
        public string? PrivateKeyPassphrase { get; set; }
        public string? RemotePath { get; set; }
        public string? ExactFile { get; set; }
    }
}
