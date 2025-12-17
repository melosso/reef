using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using FluentFTP;
using System.IO;
using Serilog;

namespace Reef.Core.Services;

public class DestinationService
{
    /// <summary>
    /// Saves a file to the specified destination type using the provided configuration.
    /// Returns (success, finalPath, errorMessage)
    /// IMPORTANT: This method now includes retry logic with exponential backoff for resilience
    /// </summary>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveToDestinationAsync(
        string sourceFilePath,
        DestinationType destinationType,
        string destinationConfigJson,
        int maxRetries = 3)
    {
        return await SaveToDestinationWithRetryAsync(sourceFilePath, destinationType, destinationConfigJson, maxRetries);
    }

    /// <summary>
    /// Internal method with retry logic and exponential backoff for destination uploads
    /// Handles transient network failures automatically
    /// </summary>
    private async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveToDestinationWithRetryAsync(
        string sourceFilePath,
        DestinationType destinationType,
        string destinationConfigJson,
        int maxRetries = 3)
    {
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                var result = await SaveToDestinationCoreAsync(sourceFilePath, destinationType, destinationConfigJson);

                if (result.Success)
                {
                    if (attempt > 0)
                    {
                        Log.Information("Destination upload succeeded on attempt {Attempt} for {DestinationType}",
                            attempt + 1, destinationType);
                    }
                    return result;
                }

                // If upload returned false but no exception, treat as retriable error
                lastException = new Exception(result.ErrorMessage ?? "Upload failed");
                Log.Warning("Destination upload attempt {Attempt} failed: {Error}",
                    attempt + 1, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                lastException = ex;
                Log.Warning(ex, "Destination upload attempt {Attempt} threw exception for {DestinationType}",
                    attempt + 1, destinationType);
            }

            attempt++;

            // If we have more retries, wait with exponential backoff
            if (attempt < maxRetries)
            {
                var delayMs = (int)Math.Pow(2, attempt) * 1000; // 2s, 4s, 8s...
                Log.Information("Retrying destination upload in {DelayMs}ms (attempt {Attempt}/{MaxRetries})",
                    delayMs, attempt + 1, maxRetries);
                await Task.Delay(delayMs);
            }
        }

        // All retries exhausted
        var finalError = $"Upload failed after {maxRetries} attempts: {lastException?.Message}";
        Log.Error(lastException, finalError);
        return (false, null, finalError);
    }

    /// <summary>
    /// Core destination save logic (single attempt, no retry)
    /// NOTE: destinationConfigJson should already be decrypted when passed from GetByIdForExecutionAsync
    /// </summary>
    private async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveToDestinationCoreAsync(
        string sourceFilePath,
        DestinationType destinationType,
        string destinationConfigJson)
    {
        try
        {
            if (!File.Exists(sourceFilePath))
                return (false, null, $"Source file not found: {sourceFilePath}");

            // Config should already be decrypted from GetByIdForExecutionAsync
            // Just use it directly
            var config = System.Text.Json.JsonSerializer.Deserialize<DestinationConfiguration>(destinationConfigJson);
            if (config == null)
                return (false, null, "Invalid destination configuration");

            // Extract relative path from source (preserves subdirectories from filename template)
            var fileRelativePath = ExtractRelativePathFromTemp(sourceFilePath);
            Log.Debug("DestinationService: Extracted relative path from temp: {RelativePath}", fileRelativePath);

            var fileName = Path.GetFileName(sourceFilePath);
            
            // Read file with FileShare.Read to allow other processes to read concurrently
            byte[] fileBytes;
            using (var fs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var ms = new MemoryStream())
            {
                await fs.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }
            
            string? finalPath = null;

            switch (destinationType)
            {
                case DestinationType.Local:
                    // Use BasePath or default to "exports" if not set
                    var basePath = config.BasePath ?? "exports";

                    // Handle relative path resolution
                    if (config.UseRelativePath)
                    {
                        // Resolve relative path relative to application base directory
                        var appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                        basePath = Path.Combine(appBaseDirectory, basePath.TrimStart('.', '/', '\\'));
                        Log.Debug("Resolved relative path {OriginalPath} to {ResolvedPath}",
                            config.BasePath, basePath);
                    }

                    // Allow config to specify a full path via CustomProperties["path"]
                    if (config.CustomProperties != null && config.CustomProperties.TryGetValue("path", out var customPath) && !string.IsNullOrWhiteSpace(customPath))
                    {
                        finalPath = customPath;
                    }
                    else
                    {
                        // Use the relative path (includes subdirectories) instead of just filename
                        finalPath = Path.Combine(basePath, fileRelativePath);
                        Log.Debug("DestinationService: Combined base path {BasePath} with relative path {RelativePath} = {FinalPath}",
                            basePath, fileRelativePath, finalPath);
                    }

                    var directory = Path.GetDirectoryName(finalPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        Log.Debug("DestinationService: Created directory: {Directory}", directory);
                    }

                    // Use async stream copy for production readiness
                    using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                    using (var destStream = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                    {
                        await sourceStream.CopyToAsync(destStream);
                    }
                    Log.Information("File saved to local destination: {FinalPath}", finalPath);
                    return (true, finalPath, null);

                case DestinationType.Ftp:
                    try
                    {
                        // Use FluentFTP for FTP upload
                        string host = config.Host ?? throw new ArgumentNullException(nameof(config.Host), "FTP host cannot be null.");
                        int port = config.Port ?? 21;
                        string username = config.Username ?? throw new ArgumentNullException(nameof(config.Username), "FTP username cannot be null.");
                        string password = config.Password ?? throw new ArgumentNullException(nameof(config.Password), "FTP password cannot be null.");
                        string remotePath = config.RemotePath ?? "/";
                        bool useSsl = config.UseSsl;
                        bool usePassive = config.UsePassiveMode;
                        int timeoutSeconds = config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 60;
                        int timeoutMs = timeoutSeconds * 1000;

                        if (!remotePath.EndsWith("/"))
                            remotePath += "/";

                        // Use the relative path (includes subdirectories) instead of just filename
                        var normalizedRelativePath = fileRelativePath.Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
                        var fullPath = remotePath.TrimStart('/') + normalizedRelativePath;
                        Log.Debug("DestinationService FTP: Full path = {FullPath}", fullPath);

                        var ftp = new FluentFTP.AsyncFtpClient();
                        ftp.Host = host;
                        ftp.Port = port;
                        ftp.Credentials = new System.Net.NetworkCredential(username, password);

                        // Configure data connection type (passive vs active mode)
                        // Use AutoPassive which will try EPSV, then PASV, then fallback to PORT (active)
                        if (usePassive)
                        {
                            ftp.Config.DataConnectionType = FluentFTP.FtpDataConnectionType.AutoPassive;
                            Log.Debug("FTP using AutoPassive mode (will try EPSV, then PASV, then Active fallback)");
                        }
                        else
                        {
                            ftp.Config.DataConnectionType = FluentFTP.FtpDataConnectionType.AutoActive;
                            Log.Debug("FTP using Active mode");
                        }

                        // Workaround for servers behind NAT/Docker returning wrong IP in PASV
                        ftp.Config.PassiveMaxAttempts = 3;
                        ftp.Config.ConnectTimeout = timeoutMs;
                        ftp.Config.DataConnectionConnectTimeout = timeoutMs;
                        ftp.Config.DataConnectionReadTimeout = timeoutMs;

                        // Don't block any passive ports (allow server to use any port in its range)
                        ftp.Config.PassiveBlockedPorts = null;

                        if (useSsl)
                        {
                            ftp.Config.EncryptionMode = FluentFTP.FtpEncryptionMode.Explicit;
                            ftp.Config.ValidateAnyCertificate = true; // Accept all certs if SSL (for demo; production should validate)
                        }

                        // Enable verbose logging for troubleshooting
                        ftp.Config.LogToConsole = true; // Temporarily enable console logging for debugging
                        ftp.Config.LogHost = true;
                        ftp.Config.LogDurations = true;

                        Log.Information("FTP connecting to {Host}:{Port} (Passive: {Passive}, SSL: {SSL}, Encryption: {Encryption})",
                            host, port, usePassive, useSsl, useSsl ? "Explicit" : "None");

                        await ftp.Connect();
                        Log.Information("FTP control connection established successfully");
                        Log.Information("FTP server type: {ServerType}, OS: {ServerOS}",
                            ftp.ServerType, ftp.ServerOS);

                        // Create directory (including subdirectories from filename)
                        var ftpDirectory = remotePath.TrimEnd('/');
                        var filenameDirectory = System.IO.Path.GetDirectoryName(normalizedRelativePath)?.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(filenameDirectory))
                        {
                            ftpDirectory = ftpDirectory + "/" + filenameDirectory;
                        }
                        Log.Debug("FTP creating directory: {Directory}", ftpDirectory);
                        await ftp.CreateDirectory(ftpDirectory);
                        Log.Debug("FTP directory created successfully");

                        // Validate source file exists before upload
                        if (!File.Exists(sourceFilePath))
                        {
                            var error = $"Source file not found for FTP upload: {sourceFilePath}";
                            Log.Error(error);
                            await ftp.Disconnect();
                            return (false, null, error);
                        }

                        var fileInfo = new FileInfo(sourceFilePath);
                        Log.Information("FTP uploading file: {Source} ({Size} bytes) to remote: {Destination}",
                            sourceFilePath, fileInfo.Length, fullPath);

                        var uploadResult = await ftp.UploadFile(sourceFilePath, fullPath, FluentFTP.FtpRemoteExists.Overwrite, true);
                        Log.Information("FTP upload completed with status: {Status}", uploadResult);

                        if (uploadResult != FluentFTP.FtpStatus.Success)
                        {
                            var error = $"FTP upload returned non-success status: {uploadResult}";
                            Log.Warning(error);
                            await ftp.Disconnect();
                            return (false, null, error);
                        }

                        await ftp.Disconnect();
                        finalPath = $"ftp://{host}:{port}/{fullPath}";
                        return (true, finalPath, null);
                    }
                    catch (Exception ftpEx)
                    {
                        // Include InnerException details for better diagnostics
                        var errorMessage = $"FTP upload failed: {ftpEx.Message}";
                        if (ftpEx.InnerException != null)
                        {
                            errorMessage += $" | Inner: {ftpEx.InnerException.Message}";
                        }
                        Log.Error(ftpEx, "FTP upload error details");
                        return (false, null, errorMessage);
                    }

                case DestinationType.Sftp:
                    var sftpDest = new Reef.Core.Destinations.SftpDestination();
                    return await sftpDest.SaveAsync(sourceFilePath, destinationConfigJson);

                case DestinationType.S3:
                    var s3Dest = new Reef.Core.Destinations.S3Destination(_encryption);
                    return await s3Dest.SaveAsync(sourceFilePath, destinationConfigJson);

                case DestinationType.AzureBlob:
                    var azureDest = new Reef.Core.Destinations.AzureBlobDestination(_encryption);
                    return await azureDest.SaveAsync(sourceFilePath, destinationConfigJson);

                case DestinationType.Http:
                    var httpDest = new Reef.Core.Destinations.HttpDestination();
                    return await httpDest.SaveAsync(sourceFilePath, destinationConfigJson);

                case DestinationType.Email:
                    var emailDest = new Reef.Core.Destinations.EmailDestination();
                    return await emailDest.SaveAsync(sourceFilePath, destinationConfigJson);

                case DestinationType.NetworkShare:
                    var networkShareDest = new Reef.Core.Destinations.NetworkShareDestination();
                    return await networkShareDest.SaveAsync(sourceFilePath, destinationConfigJson);

                case DestinationType.WebDav:
                    return (false, null, $"Destination type '{destinationType}' is not implemented yet.");

                default:
                    return (false, null, $"Unknown destination type: {destinationType}");
            }
        }
        catch (Exception ex)
        {
            return (false, null, $"SaveToDestinationAsync error: {ex.Message}");
        }
    }
    private readonly string _connectionString;
    private readonly EncryptionService _encryption;
    private readonly DestinationConfigEncryption _configEncryption;

    public DestinationService(string connectionString, EncryptionService encryption)
    {
        _connectionString = connectionString;
        _encryption = encryption;
        _configEncryption = new DestinationConfigEncryption(encryption);
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
    /// Compensation logic: Delete/rollback an exported file from destination
    /// Implements saga pattern for distributed transaction compensation
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> CompensateExportAsync(
        string filePath,
        DestinationType destinationType,
        string destinationConfigJson)
    {
        try
        {
            Log.Information("Attempting export compensation (delete) for {DestinationType}: {FilePath}",
                destinationType, filePath);

            var config = System.Text.Json.JsonSerializer.Deserialize<DestinationConfiguration>(destinationConfigJson);
            if (config == null)
            {
                return (false, "Invalid destination configuration");
            }

            switch (destinationType)
            {
                case DestinationType.Local:
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Log.Information("Compensation: Deleted local file {FilePath}", filePath);
                        return (true, null);
                    }
                    return (true, "File already deleted or does not exist");

                case DestinationType.Ftp:
                    return await CompensateFtpAsync(filePath, config);

                case DestinationType.S3:
                    return await CompensateS3Async(filePath, config);

                case DestinationType.AzureBlob:
                    return await CompensateAzureBlobAsync(filePath, config);

                case DestinationType.Sftp:
                case DestinationType.Http:
                case DestinationType.Email:
                case DestinationType.NetworkShare:
                case DestinationType.WebDav:
                    // These destination types don't support compensation or it's not practical
                    Log.Warning("Compensation not supported for destination type: {DestinationType}", destinationType);
                    return (false, $"Compensation not supported for {destinationType}");

                default:
                    return (false, $"Unknown destination type: {destinationType}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Compensation failed for {DestinationType}: {FilePath}", destinationType, filePath);
            return (false, $"Compensation error: {ex.Message}");
        }
    }

    private async Task<(bool Success, string? ErrorMessage)> CompensateFtpAsync(string remotePath, DestinationConfiguration config)
    {
        try
        {
            string host = config.Host ?? throw new ArgumentNullException(nameof(config.Host));
            int port = config.Port ?? 21;
            string username = config.Username ?? throw new ArgumentNullException(nameof(config.Username));
            string password = config.Password ?? throw new ArgumentNullException(nameof(config.Password));
            bool useSsl = config.UseSsl;
            bool usePassive = config.UsePassiveMode;

            var ftp = new FluentFTP.AsyncFtpClient();
            ftp.Host = host;
            ftp.Port = port;
            ftp.Credentials = new System.Net.NetworkCredential(username, password);

            // Configure data connection type (passive vs active mode)
            ftp.Config.DataConnectionType = usePassive
                ? FluentFTP.FtpDataConnectionType.AutoPassive
                : FluentFTP.FtpDataConnectionType.AutoActive;

            if (useSsl)
            {
                ftp.Config.EncryptionMode = FluentFTP.FtpEncryptionMode.Explicit;
                ftp.Config.ValidateAnyCertificate = true;
            }

            await ftp.Connect();
            await ftp.DeleteFile(remotePath);
            await ftp.Disconnect();

            Log.Information("Compensation: Deleted FTP file {RemotePath}", remotePath);
            return (true, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FTP compensation failed for {RemotePath}", remotePath);
            return (false, ex.Message);
        }
    }

    private Task<(bool Success, string? ErrorMessage)> CompensateS3Async(string s3Key, DestinationConfiguration config)
    {
        try
        {
            // S3 compensation would use AWS SDK to delete the object
            // For now, return not implemented
            Log.Warning("S3 compensation not yet implemented for {S3Key}", s3Key);
            return Task.FromResult<(bool Success, string? ErrorMessage)>((false, "S3 compensation not yet implemented"));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool Success, string? ErrorMessage)>((false, ex.Message));
        }
    }

    private Task<(bool Success, string? ErrorMessage)> CompensateAzureBlobAsync(string blobName, DestinationConfiguration config)
    {
        try
        {
            // Azure Blob compensation would use Azure SDK to delete the blob
            // For now, return not implemented
            Log.Warning("Azure Blob compensation not yet implemented for {BlobName}", blobName);
            return Task.FromResult<(bool Success, string? ErrorMessage)>((false, "Azure Blob compensation not yet implemented"));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool Success, string? ErrorMessage)>((false, ex.Message));
        }
    }

    public async Task<IEnumerable<Destination>> GetAllAsync(bool activeOnly = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sql = activeOnly
            ? "SELECT * FROM Destinations WHERE IsActive = 1 ORDER BY Name"
            : "SELECT * FROM Destinations ORDER BY Name";

        var destinations = await conn.QueryAsync<Destination>(sql);

        // Decrypt and mask configs for API responses
        foreach (var dest in destinations)
        {
            if (!string.IsNullOrEmpty(dest.ConfigurationJson))
            {
                try
                {
                    // Decrypt first
                    var decrypted = dest.ConfigurationJson;
                    if (_encryption.IsEncrypted(dest.ConfigurationJson))
                    {
                        // Fully encrypted with PWENC:
                        decrypted = _encryption.Decrypt(dest.ConfigurationJson);
                    }
                    else
                    {
                        // Legacy format: plaintext or field-level encrypted
                        decrypted = _configEncryption.DecryptSecretFields(dest.ConfigurationJson, dest.Type);
                    }

                    // Mask secrets for API response (never send plaintext passwords to frontend)
                    dest.ConfigurationJson = _configEncryption.MaskSecretFields(decrypted, dest.Type);
                }
                catch (Exception ex)
                {
                    // Log decryption error but continue processing other destinations
                    Log.Warning(ex, "Failed to decrypt configuration for destination {DestinationId} ({DestinationName}). Returning masked configuration.", dest.Id, dest.Name);
                    // Mask the configuration as [ENCRYPTED] to indicate it's unavailable
                    dest.ConfigurationJson = "{\"_error\": \"Failed to decrypt configuration\"}";
                }
            }
        }

        return destinations;
    }

    public async Task<Destination?> GetByIdAsync(int id)
    {
        return await GetByIdAsync(id, maskSecrets: true);
    }

    /// <summary>
    /// Get destination by ID for execution (without masking secrets)
    /// Used internally when destinations need to be used for actual operations
    /// </summary>
    public async Task<Destination?> GetByIdForExecutionAsync(int id)
    {
        return await GetByIdAsync(id, maskSecrets: false);
    }

    /// <summary>
    /// Internal method to get destination
    /// maskSecrets=true: For API responses (decrypt then mask secrets)
    /// maskSecrets=false: For internal execution (fully decrypted, in-memory only)
    /// </summary>
    private async Task<Destination?> GetByIdAsync(int id, bool maskSecrets)
    {
        using var conn = new SqliteConnection(_connectionString);
        var dest = await conn.QuerySingleOrDefaultAsync<Destination>(
            "SELECT * FROM Destinations WHERE Id = @Id", new { Id = id });

        if (dest != null && !string.IsNullOrEmpty(dest.ConfigurationJson))
        {
            Log.Debug("GetByIdAsync (maskSecrets={MaskSecrets}) for destination {Id} ({Type})", maskSecrets, id, dest.Type);

            try
            {
                // Decrypt the config
                var decrypted = dest.ConfigurationJson;
                if (_encryption.IsEncrypted(dest.ConfigurationJson))
                {
                    // Fully encrypted - decrypt it
                    decrypted = _encryption.Decrypt(dest.ConfigurationJson);
                }
                else
                {
                    // Legacy format: plaintext or field-level encrypted
                    // Decrypt any field-level PWENC: fields
                    decrypted = _configEncryption.DecryptSecretFields(dest.ConfigurationJson, dest.Type);
                }

                // For API responses: mask secret fields so frontend never sees passwords
                if (maskSecrets)
                {
                    dest.ConfigurationJson = _configEncryption.MaskSecretFields(decrypted, dest.Type);
                }
                else
                {
                    // For execution: keep fully decrypted (in-memory only, never logged/exposed)
                    dest.ConfigurationJson = decrypted;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to decrypt configuration for destination {Id} ({Type}). Configuration may be corrupted or encrypted with different key.", id, dest.Type);
                dest.ConfigurationJson = "{\"_error\": \"Failed to decrypt configuration\"}";
            }
        }

        return dest;
    }

    public async Task<Destination> CreateAsync(Destination destination)
    {
        using var conn = new SqliteConnection(_connectionString);

        // Encrypt entire ConfigurationJson
        var encryptedConfig = destination.ConfigurationJson;
        if (!string.IsNullOrEmpty(destination.ConfigurationJson))
        {
            encryptedConfig = _encryption.Encrypt(destination.ConfigurationJson);
        }

        // Generate hash using encrypted config
        destination.Hash = Reef.Helpers.HashHelper.ComputeDestinationHash(
            destination.Name,
            destination.Type.ToString(),
            encryptedConfig);

        destination.CreatedAt = DateTime.UtcNow;

        var sql = @"
            INSERT INTO Destinations (Name, Description, Type, ConfigurationJson, IsActive, Tags, CreatedAt, Hash)
            VALUES (@Name, @Description, @Type, @ConfigurationJson, @IsActive, @Tags, @CreatedAt, @Hash);
            SELECT last_insert_rowid();";

        // Create a copy for database insertion with encrypted config
        var dbDestination = new Destination
        {
            Name = destination.Name,
            Description = destination.Description,
            Type = destination.Type,
            ConfigurationJson = encryptedConfig,
            IsActive = destination.IsActive,
            Tags = destination.Tags,
            CreatedAt = destination.CreatedAt,
            Hash = destination.Hash
        };

        destination.Id = await conn.ExecuteScalarAsync<int>(sql, dbDestination);

        // Return original plaintext config so frontend can display it
        // (The encrypted version is stored in database)
        // Note: destination.ConfigurationJson already contains the plaintext from the request

        return destination;
    }

    public async Task<bool> UpdateAsync(Destination destination)
    {
        using var conn = new SqliteConnection(_connectionString);

        // Get existing destination
        var existing = await conn.QuerySingleOrDefaultAsync<Destination>(
            "SELECT * FROM Destinations WHERE Id = @Id", new { destination.Id });

        if (existing == null)
            return false;

        // Decrypt existing to merge with incoming
        var existingDecrypted = existing.ConfigurationJson;
        if (!string.IsNullOrEmpty(existing.ConfigurationJson))
        {
            if (_encryption.IsEncrypted(existing.ConfigurationJson))
            {
                // Fully encrypted with PWENC: (entire config)
                existingDecrypted = _encryption.Decrypt(existing.ConfigurationJson);
            }
            else
            {
                // Legacy format: might have field-level PWENC: encryption in plaintext JSON
                existingDecrypted = _configEncryption.DecryptSecretFields(existing.ConfigurationJson, existing.Type);
            }
        }

        // Merge incoming config with existing (preserves encrypted values for masked fields)
        var mergedConfig = destination.ConfigurationJson;
        if (!string.IsNullOrEmpty(destination.ConfigurationJson) && !string.IsNullOrEmpty(existingDecrypted))
        {
            mergedConfig = _configEncryption.MergeWithExisting(
                destination.ConfigurationJson,
                existingDecrypted,
                destination.Type);
        }

        // Encrypt entire merged configuration
        var encryptedConfig = mergedConfig;
        if (!string.IsNullOrEmpty(mergedConfig))
        {
            encryptedConfig = _encryption.Encrypt(mergedConfig);
        }

        // Generate new hash using encrypted config
        destination.Hash = Reef.Helpers.HashHelper.ComputeDestinationHash(
            destination.Name,
            destination.Type.ToString(),
            encryptedConfig);

        destination.ModifiedAt = DateTime.UtcNow;

        var sql = @"
            UPDATE Destinations
            SET Name = @Name, Description = @Description, Type = @Type,
                ConfigurationJson = @ConfigurationJson, IsActive = @IsActive,
                Tags = @Tags, ModifiedAt = @ModifiedAt, Hash = @Hash
            WHERE Id = @Id";

        // Create a copy for database update with encrypted config
        var dbDestination = new Destination
        {
            Id = destination.Id,
            Name = destination.Name,
            Description = destination.Description,
            Type = destination.Type,
            ConfigurationJson = encryptedConfig,
            IsActive = destination.IsActive,
            Tags = destination.Tags,
            ModifiedAt = destination.ModifiedAt,
            Hash = destination.Hash
        };

        var rows = await conn.ExecuteAsync(sql, dbDestination);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        // Check if destination is in use
        var inUse = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Profiles WHERE OutputDestinationId = @Id", new { Id = id });
        
        if (inUse > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete destination. It is used by {inUse} profile(s).");
        }
        
        var sql = "DELETE FROM Destinations WHERE Id = @Id";
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }

    public async Task<DestinationTestResult> TestConnectionAsync(int destinationId, string? testFileName = null, string? testContent = null)
    {
        var destination = await GetByIdForExecutionAsync(destinationId); 
        if (destination == null)
        {
            return new DestinationTestResult 
            { 
                Success = false, 
                Message = "Destination not found" 
            };
        }

        var startTime = DateTime.UtcNow;
        
        try
        {
            var config = JsonSerializer.Deserialize<DestinationConfiguration>(destination.ConfigurationJson);
            if (config == null)
            {
                return new DestinationTestResult 
                { 
                    Success = false, 
                    Message = "Invalid configuration" 
                };
            }

            testFileName ??= $"test_{DateTime.UtcNow:yyyyMMddHHmmss}.txt";
            testContent ??= $"Reef test file - {DateTime.UtcNow:O}";
            
            var testBytes = System.Text.Encoding.UTF8.GetBytes(testContent);
            string filePath = "";

            switch (destination.Type)
            {
                case DestinationType.Local:
                    filePath = await TestLocalDestination(config, testFileName, testBytes);
                    break;
                    
                case DestinationType.Ftp:
                    filePath = await TestFtpDestination(config, testFileName, testBytes);
                    break;
                    
                case DestinationType.Sftp:
                    filePath = await TestSftpDestination(config, testFileName, testBytes);
                    break;
                    
                case DestinationType.S3:
                    filePath = await TestS3Destination(config, testFileName, testBytes);
                    break;
                    
                case DestinationType.AzureBlob:
                    filePath = await TestAzureBlobDestination(config, testFileName, testBytes);
                    break;
                    
                case DestinationType.Http:
                    filePath = await TestHttpDestination(config, testFileName, testBytes);
                    break;
                    
                default:
                    throw new NotImplementedException($"Test not implemented for {destination.Type}");
            }

            var duration = DateTime.UtcNow - startTime;
            
            return new DestinationTestResult
            {
                Success = true,
                Message = "Connection test successful",
                FilePath = filePath,
                BytesWritten = testBytes.Length,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            return new DestinationTestResult
            {
                Success = false,
                Message = $"Test failed: {ex.Message}",
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// Test a destination configuration without saving it to the database
    /// Note: configurationJson comes from frontend as plaintext (not yet encrypted)
    /// </summary>
    public async Task<DestinationTestResult> TestDestinationConfigurationAsync(
        DestinationType type,
        string configurationJson,
        string? testFileName = null,
        string? testContent = null)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Parse configuration (plaintext from frontend)
            var config = JsonSerializer.Deserialize<DestinationConfiguration>(configurationJson);
            if (config == null)
            {
                return new DestinationTestResult 
                { 
                    Success = false, 
                    Message = "Invalid configuration JSON",
                    ResponseTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                };
            }

            // Generate test file
            testFileName ??= $"reef_test_{DateTime.UtcNow:yyyyMMddHHmmss}.txt";
            testContent ??= $"Reef destination test at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
            var content = Encoding.UTF8.GetBytes(testContent);

            // Test based on type
            string finalPath;
            switch (type)
            {
                case DestinationType.Local:
                    finalPath = await TestLocalDestination(config, testFileName, content);
                    break;
                    
                case DestinationType.Ftp:
                    finalPath = await TestFtpDestination(config, testFileName, content);
                    break;
                    
                case DestinationType.Sftp:
                    finalPath = await TestSftpDestination(config, testFileName, content);
                    break;
                    
                case DestinationType.S3:
                    finalPath = await TestS3Destination(config, testFileName, content);
                    break;
                    
                case DestinationType.AzureBlob:
                    finalPath = await TestAzureBlobDestination(config, testFileName, content);
                    break;
                    
                case DestinationType.Http:
                    finalPath = await TestHttpDestination(config, testFileName, content);
                    break;
                    
                case DestinationType.Email:
                    // Email doesn't write to a path, create temp file for testing
                    var tempPath = Path.Combine(Path.GetTempPath(), testFileName);
                    await File.WriteAllBytesAsync(tempPath, content);
                    finalPath = tempPath;
                    break;
                    
                case DestinationType.NetworkShare:
                    finalPath = await TestNetworkShareDestination(config, testFileName, content);
                    break;
                    
                case DestinationType.WebDav:
                    return new DestinationTestResult
                    {
                        Success = false,
                        Message = $"Destination type '{type}' is not yet implemented",
                        ResponseTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                    };
                    
                default:
                    return new DestinationTestResult
                    {
                        Success = false,
                        Message = $"Unknown destination type: {type}",
                        ResponseTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                    };
            }

            var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            
            return new DestinationTestResult
            {
                Success = true,
                Message = $"Successfully connected to {type} destination",
                TestFilePath = finalPath,
                FilePath = finalPath,
                BytesWritten = content.Length,
                ResponseTimeMs = duration,
                Duration = TimeSpan.FromMilliseconds(duration)
            };
        }
        catch (Exception ex)
        {
            var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return new DestinationTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}",
                ResponseTimeMs = duration,
                Duration = TimeSpan.FromMilliseconds(duration)
            };
        }
    }

    private async Task<string> TestLocalDestination(DestinationConfiguration config, string fileName, byte[] content)
    {
        var basePath = config.BasePath ?? "exports";

        // Handle relative path resolution
        if (config.UseRelativePath)
        {
            // Resolve relative path relative to application base directory
            var appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            basePath = Path.Combine(appBaseDirectory, basePath.TrimStart('.', '/', '\\'));
            Log.Debug("Resolved relative test path {OriginalPath} to {ResolvedPath}",
                config.BasePath, basePath);
        }

        var fullPath = Path.Combine(basePath, fileName);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(fullPath, content);
        return fullPath;
    }

    private async Task<string> TestFtpDestination(DestinationConfiguration config, string fileName, byte[] content)
    {
        // Production-ready FTP test using FluentFTP (v53+)
        string host = config.Host ?? throw new ArgumentNullException(nameof(config.Host), "FTP host cannot be null.");
        int port = config.Port ?? 21;
        string username = config.Username ?? throw new ArgumentNullException(nameof(config.Username), "FTP username cannot be null.");
        string password = config.Password ?? throw new ArgumentNullException(nameof(config.Password), "FTP password cannot be null.");
        string remotePath = config.RemotePath ?? "/";
        bool useSsl = config.UseSsl;
        bool usePassive = config.UsePassiveMode;

        if (!remotePath.EndsWith("/"))
            remotePath += "/";
        var fullPath = remotePath.TrimStart('/') + fileName;

        var ftp = new AsyncFtpClient();
        ftp.Host = host;
        ftp.Port = port;
        ftp.Credentials = new System.Net.NetworkCredential(username, password);

        // Configure data connection type (passive vs active mode)
        ftp.Config.DataConnectionType = usePassive
            ? FtpDataConnectionType.AutoPassive
            : FtpDataConnectionType.AutoActive;

        if (useSsl)
        {
            ftp.Config.EncryptionMode = FtpEncryptionMode.Explicit;
            ftp.Config.ValidateAnyCertificate = true; // Accept all certs if SSL (for demo; production should validate)
        }
        await ftp.Connect();
        await ftp.CreateDirectory(remotePath);
        await ftp.UploadBytes(content, fullPath, FtpRemoteExists.Overwrite, true);
        await ftp.Disconnect();
        return $"ftp://{host}:{port}/{fullPath}";
    }

    private async Task<string> TestSftpDestination(DestinationConfiguration config, string fileName, byte[] content)
    {
        try
        {
            // Validate required fields
            string host = config.Host ?? throw new ArgumentNullException(nameof(config.Host), "SFTP host cannot be null.");
            int port = config.Port ?? 22;
            string username = config.Username ?? throw new ArgumentNullException(nameof(config.Username), "SFTP username cannot be null.");

            // Build SFTP config dictionary for SftpDestination
            var sftpConfig = new Dictionary<string, object>
            {
                ["host"] = host,
                ["port"] = port,
                ["username"] = username
            };

            // Add authentication (password or private key)
            if (!string.IsNullOrWhiteSpace(config.PrivateKey))
            {
                sftpConfig["privateKey"] = config.PrivateKey;
                if (!string.IsNullOrWhiteSpace(config.PrivateKeyPassphrase))
                {
                    sftpConfig["privateKeyPassphrase"] = config.PrivateKeyPassphrase;
                }
                Log.Information("Testing SFTP connection with private key authentication to {Host}:{Port}", host, port);
            }
            else if (!string.IsNullOrWhiteSpace(config.Password))
            {
                sftpConfig["password"] = config.Password;
                Log.Information("Testing SFTP connection with password authentication to {Host}:{Port}", host, port);
            }
            else
            {
                throw new ArgumentException("SFTP requires either password or private key authentication");
            }

            // Use SftpDestination's TestConnectionAsync method
            var sftpDestination = new Destinations.SftpDestination();
            bool connected = await sftpDestination.TestConnectionAsync(sftpConfig);

            if (connected)
            {
                return $"Successfully connected to SFTP server {host}:{port}";
            }
            else
            {
                throw new Exception($"Failed to connect to SFTP server {host}:{port}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SFTP connection test failed");
            throw new Exception($"SFTP test failed: {ex.Message}", ex);
        }
    }

    private async Task<string> TestS3Destination(DestinationConfiguration config, string fileName, byte[] content)
    {
        try
        {
            // Validate required fields
            string bucketName = config.BucketName ?? throw new ArgumentNullException(nameof(config.BucketName), "S3 bucket name cannot be null.");
            string region = config.Region ?? throw new ArgumentNullException(nameof(config.Region), "S3 region cannot be null.");
            string accessKey = config.AccessKey ?? throw new ArgumentNullException(nameof(config.AccessKey), "S3 access key cannot be null.");
            string secretKey = config.SecretKey ?? throw new ArgumentNullException(nameof(config.SecretKey), "S3 secret key cannot be null.");

            // Decrypt credentials if encrypted
            if (accessKey.StartsWith("PWENC:"))
            {
                accessKey = _encryption.Decrypt(accessKey.Substring(6));
            }
            if (secretKey.StartsWith("PWENC:"))
            {
                secretKey = _encryption.Decrypt(secretKey.Substring(6));
            }

            Log.Information("Testing S3 connection to bucket {Bucket} in region {Region}", bucketName, region);

            // Create S3 client
            var s3Config = new Amazon.S3.AmazonS3Config
            {
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : 60)
            };

            // Use custom endpoint if provided (MinIO, Wasabi, DigitalOcean Spaces, etc.)
            if (!string.IsNullOrWhiteSpace(config.ServiceUrl))
            {
                s3Config.ServiceURL = config.ServiceUrl;
                s3Config.ForcePathStyle = true;  // Required for MinIO and most S3-compatible storage
                Log.Debug("Using custom S3 endpoint: {ServiceUrl}", config.ServiceUrl);
            }
            else
            {
                // Use AWS region endpoint
                s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region);
            }

            using var client = new Amazon.S3.AmazonS3Client(accessKey, secretKey, s3Config);

            // Test 1: Check if bucket exists and is accessible
            try
            {
                var headRequest = new Amazon.S3.Model.GetBucketLocationRequest
                {
                    BucketName = bucketName
                };
                var locationResponse = await client.GetBucketLocationAsync(headRequest);
                Log.Debug("S3 bucket {Bucket} found, location: {Location}", bucketName, locationResponse.Location);
            }
            catch (Amazon.S3.AmazonS3Exception s3Ex) when (s3Ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new Exception($"S3 bucket '{bucketName}' does not exist or you don't have access to it");
            }
            catch (Amazon.S3.AmazonS3Exception s3Ex) when (s3Ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new Exception($"Access denied to S3 bucket '{bucketName}'. Check your IAM permissions.");
            }

            // Test 2: Verify write permissions by attempting to upload the test file
            var testKey = $"reef-test/{fileName}";
            var putRequest = new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = bucketName,
                Key = testKey,
                ContentBody = System.Text.Encoding.UTF8.GetString(content)
            };

            // Apply advanced settings if configured
            if (!string.IsNullOrWhiteSpace(config.StorageClass))
            {
                putRequest.StorageClass = config.StorageClass.ToUpper() switch
                {
                    "STANDARD" => Amazon.S3.S3StorageClass.Standard,
                    "STANDARD_IA" => Amazon.S3.S3StorageClass.StandardInfrequentAccess,
                    "ONEZONE_IA" => Amazon.S3.S3StorageClass.OneZoneInfrequentAccess,
                    "INTELLIGENT_TIERING" => Amazon.S3.S3StorageClass.IntelligentTiering,
                    "GLACIER" => Amazon.S3.S3StorageClass.Glacier,
                    "GLACIER_IR" => Amazon.S3.S3StorageClass.GlacierInstantRetrieval,
                    "DEEP_ARCHIVE" => Amazon.S3.S3StorageClass.DeepArchive,
                    _ => Amazon.S3.S3StorageClass.Standard
                };
            }

            var putResponse = await client.PutObjectAsync(putRequest);

            if ((int)putResponse.HttpStatusCode >= 200 && (int)putResponse.HttpStatusCode < 300)
            {
                // Clean up test file
                try
                {
                    await client.DeleteObjectAsync(bucketName, testKey);
                    Log.Debug("Cleaned up S3 test file: {Key}", testKey);
                }
                catch (Exception cleanupEx)
                {
                    Log.Warning(cleanupEx, "Could not clean up S3 test file: {Key}", testKey);
                }

                // Generate appropriate success message based on endpoint type
                string locationInfo = !string.IsNullOrWhiteSpace(config.ServiceUrl)
                    ? $"at {config.ServiceUrl}"
                    : $"in {region}";

                return $"Successfully connected to S3 bucket '{bucketName}' {locationInfo} and verified write permissions";
            }
            else
            {
                throw new Exception($"S3 upload test failed with status code: {putResponse.HttpStatusCode}");
            }
        }
        catch (Amazon.S3.AmazonS3Exception s3Ex)
        {
            Log.Error(s3Ex, "S3 connection test failed");
            throw new Exception($"S3 test failed: {s3Ex.Message} (Error Code: {s3Ex.ErrorCode})", s3Ex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "S3 connection test failed");
            throw new Exception($"S3 test failed: {ex.Message}", ex);
        }
    }

    private async Task<string> TestAzureBlobDestination(DestinationConfiguration config, string fileName, byte[] content)
    {
        try
        {
            // Validate required fields
            string connectionString = config.ConnectionString ?? throw new ArgumentNullException(nameof(config.ConnectionString), "Azure Blob connection string cannot be null.");
            string containerName = config.ContainerName ?? throw new ArgumentNullException(nameof(config.ContainerName), "Azure Blob container name cannot be null.");

            // Decrypt connection string if encrypted
            if (connectionString.StartsWith("PWENC:"))
            {
                connectionString = _encryption.Decrypt(connectionString.Substring(6));
            }

            Log.Information("Testing Azure Blob connection to container {Container}", containerName);

            // Create blob service client
            var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(connectionString);

            // Test 1: Verify connection by getting account info
            try
            {
                var accountInfo = await blobServiceClient.GetAccountInfoAsync();
                Log.Debug("Azure Storage account verified, SKU: {Sku}", accountInfo.Value.SkuName);
            }
            catch (Azure.RequestFailedException azEx) when (azEx.Status == 403)
            {
                throw new Exception("Authentication failed. Check your connection string.");
            }

            // Test 2: Get or create container
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

            try
            {
                // Check if container exists
                bool exists = await containerClient.ExistsAsync();
                if (!exists)
                {
                    throw new Exception($"Container '{containerName}' does not exist. Please create it first or check the name.");
                }
                Log.Debug("Azure Blob container {Container} found", containerName);
            }
            catch (Azure.RequestFailedException azEx)
            {
                throw new Exception($"Error accessing container '{containerName}': {azEx.Message}");
            }

            // Test 3: Verify write permissions by uploading test file
            var testBlobName = $"reef-test/{fileName}";
            var blobClient = containerClient.GetBlobClient(testBlobName);

            using (var stream = new MemoryStream(content))
            {
                await blobClient.UploadAsync(stream, overwrite: true);
                Log.Debug("Successfully uploaded test file to Azure Blob: {BlobName}", testBlobName);
            }

            // Clean up test file
            try
            {
                await blobClient.DeleteAsync();
                Log.Debug("Cleaned up Azure Blob test file: {BlobName}", testBlobName);
            }
            catch (Exception cleanupEx)
            {
                Log.Warning(cleanupEx, "Could not clean up Azure Blob test file: {BlobName}", testBlobName);
            }

            return $"Successfully connected to Azure Blob container '{containerName}' and verified write permissions";
        }
        catch (Azure.RequestFailedException azEx)
        {
            Log.Error(azEx, "Azure Blob connection test failed");
            throw new Exception($"Azure Blob test failed: {azEx.Message} (Status: {azEx.Status}, Error Code: {azEx.ErrorCode})", azEx);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Azure Blob connection test failed");
            throw new Exception($"Azure Blob test failed: {ex.Message}", ex);
        }
    }

    private async Task<string> TestHttpDestination(DestinationConfiguration config, string fileName, byte[] content)
    {
        // Create HttpConfig from DestinationConfiguration
        var httpConfig = new Reef.Core.Destinations.HttpConfig
        {
            Url = config.Url ?? throw new ArgumentNullException(nameof(config.Url), "HTTP URL cannot be null."),
            UploadFormat = config.CustomProperties?.GetValueOrDefault("uploadFormat", "raw"),
            ContentType = config.CustomProperties?.GetValueOrDefault("contentType"),
            AuthType = config.AuthType,
            AuthToken = config.AuthToken,
            Username = config.Username,
            Password = config.Password,
            ApiKeyHeader = config.CustomProperties?.GetValueOrDefault("apiKeyHeader"),
            Headers = config.Headers,
            FileFieldName = config.CustomProperties?.GetValueOrDefault("fileFieldName"),
            FileNameHeader = config.CustomProperties?.GetValueOrDefault("fileNameHeader"),
            TimeoutSeconds = config.TimeoutSeconds
        };

        // Create temporary file for testing
        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        try
        {
            await File.WriteAllBytesAsync(tempFile, content);

            var httpDest = new Reef.Core.Destinations.HttpDestination();
            var result = await httpDest.SaveAsync(tempFile, System.Text.Json.JsonSerializer.Serialize(httpConfig));

            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "HTTP test failed");
            }

            return config.Url;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private async Task<string> TestNetworkShareDestination(DestinationConfiguration config, string fileName, byte[] content)
    {
        // For network share, we need to use the actual NetworkShare destination class
        var networkShareConfig = new
        {
            UncPath = config.BasePath ?? config.Host ?? config.UncPath ?? throw new ArgumentNullException("UncPath/BasePath is required"),
            SubFolder = config.SubFolder ?? config.CustomProperties?.GetValueOrDefault("subfolder"),
            UseRelativePath = config.UseRelativePath,
            Username = config.Username,
            Password = config.Password,
            Domain = config.Domain ?? config.CustomProperties?.GetValueOrDefault("domain"),
            RetryCount = 3,
            RetryDelayMs = 1000
        };

        // Create temporary file for testing
        var tempFile = Path.Combine(Path.GetTempPath(), fileName);
        try
        {
            await File.WriteAllBytesAsync(tempFile, content);

            var networkShareDest = new Reef.Core.Destinations.NetworkShareDestination();
            var result = await networkShareDest.SaveAsync(tempFile, System.Text.Json.JsonSerializer.Serialize(networkShareConfig));

            if (!result.Success)
            {
                throw new InvalidOperationException(result.ErrorMessage ?? "Network share test failed");
            }

            return result.FinalPath ?? networkShareConfig.UncPath;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public async Task<IEnumerable<Destination>> GetByTagAsync(string tag)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sql = "SELECT * FROM Destinations WHERE Tags LIKE @Tag ORDER BY Name";
        var destinations = await conn.QueryAsync<Destination>(sql, new { Tag = $"%{tag}%" });

        // Decrypt and mask configs for API responses
        foreach (var dest in destinations)
        {
            if (!string.IsNullOrEmpty(dest.ConfigurationJson))
            {
                // Decrypt first
                var decrypted = dest.ConfigurationJson;
                if (_encryption.IsEncrypted(dest.ConfigurationJson))
                {
                    decrypted = _encryption.Decrypt(dest.ConfigurationJson);
                }
                else
                {
                    decrypted = _configEncryption.DecryptSecretFields(dest.ConfigurationJson, dest.Type);
                }

                // Mask secrets for API response
                dest.ConfigurationJson = _configEncryption.MaskSecretFields(decrypted, dest.Type);
            }
        }

        return destinations;
    }
}