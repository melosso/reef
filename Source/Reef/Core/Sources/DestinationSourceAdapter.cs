using Reef.Core.Models;
using Reef.Core.Services;
using System.Text.Json;
using Serilog;

namespace Reef.Core.Sources;

/// <summary>
/// Bridges a saved Destination record to the source config required by IImportSource.
/// When a profile has SourceDestinationId set, this adapter loads the destination,
/// translates its config into the matching source JSON shape, and returns an updated profile.
/// Profile-level overrides (SourceFilePath, HttpMethod, etc.) take precedence.
/// </summary>
public class DestinationSourceAdapter(DestinationService destinationService)
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<DestinationSourceAdapter>();

    /// <summary>
    /// If profile.SourceDestinationId is set, resolve the destination and merge its config
    /// into the profile's SourceType and SourceConfig. Otherwise returns profile unchanged.
    /// </summary>
    public async Task<ImportProfile> ResolveSourceConfigAsync(
        ImportProfile profile,
        CancellationToken ct = default)
    {
        if (!profile.SourceDestinationId.HasValue)
            return profile;

        var dest = await destinationService.GetByIdForExecutionAsync(profile.SourceDestinationId.Value);
        if (dest is null)
        {
            Log.Warning("DestinationSourceAdapter: destination {Id} not found — using inline source config",
                profile.SourceDestinationId.Value);
            return profile;
        }

        var sourceType = MapDestinationTypeToSourceType(dest.Type);
        var mergedConfig = TranslateToSourceConfig(dest.Type, dest.ConfigurationJson, profile.SourceConfig);

        Log.Debug("DestinationSourceAdapter: resolved destination '{Name}' ({Type}) → sourceType={SourceType}",
            dest.Name, dest.Type, sourceType);

        // Return a copy of the profile with the translated source info.
        // Profile-level fields like SourceFilePath, HttpMethod, HttpDataRootPath etc. are preserved.
        return new ImportProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            GroupId = profile.GroupId,
            SourceType = sourceType,
            SourceDestinationId = profile.SourceDestinationId,
            SourceConfig = mergedConfig,
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
            SourceFormat = profile.SourceFormat,
            FormatConfig = profile.FormatConfig,
            ColumnMappingsJson = profile.ColumnMappingsJson,
            AutoMapColumns = profile.AutoMapColumns,
            SkipUnmappedColumns = profile.SkipUnmappedColumns,
            TargetType = profile.TargetType,
            TargetConnectionId = profile.TargetConnectionId,
            TargetTable = profile.TargetTable,
            LocalTargetPath = profile.LocalTargetPath,
            LocalTargetFormat = profile.LocalTargetFormat,
            LocalTargetWriteMode = profile.LocalTargetWriteMode,
            LoadStrategy = profile.LoadStrategy,
            UpsertKeyColumns = profile.UpsertKeyColumns,
            BatchSize = profile.BatchSize,
            CommandTimeoutSeconds = profile.CommandTimeoutSeconds,
            DeltaSyncEnabled = profile.DeltaSyncEnabled,
            DeltaSyncReefIdColumn = profile.DeltaSyncReefIdColumn,
            DeltaSyncHashAlgorithm = profile.DeltaSyncHashAlgorithm,
            DeltaSyncTrackDeletes = profile.DeltaSyncTrackDeletes,
            DeltaSyncDeleteStrategy = profile.DeltaSyncDeleteStrategy,
            DeltaSyncDeleteColumn = profile.DeltaSyncDeleteColumn,
            DeltaSyncDeleteValue = profile.DeltaSyncDeleteValue,
            DeltaSyncRetentionDays = profile.DeltaSyncRetentionDays,
            OnSourceFailure = profile.OnSourceFailure,
            OnParseFailure = profile.OnParseFailure,
            OnRowFailure = profile.OnRowFailure,
            OnConstraintViolation = profile.OnConstraintViolation,
            MaxFailedRowsBeforeAbort = profile.MaxFailedRowsBeforeAbort,
            MaxFailedRowsPercent = profile.MaxFailedRowsPercent,
            RollbackOnAbort = profile.RollbackOnAbort,
            RetryCount = profile.RetryCount,
            PreProcessType = profile.PreProcessType,
            PreProcessConfig = profile.PreProcessConfig,
            PreProcessRollbackOnFailure = profile.PreProcessRollbackOnFailure,
            PostProcessType = profile.PostProcessType,
            PostProcessConfig = profile.PostProcessConfig,
            PostProcessSkipOnFailure = profile.PostProcessSkipOnFailure,
            PostProcessRollbackOnFailure = profile.PostProcessRollbackOnFailure,
            NotificationConfig = profile.NotificationConfig,
            IsEnabled = profile.IsEnabled,
            Hash = profile.Hash,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt,
            CreatedBy = profile.CreatedBy,
            LastExecutedAt = profile.LastExecutedAt,
            Code = profile.Code
        };
    }

    // ── Translation logic ─────────────────────────────────────────────────

    private static string MapDestinationTypeToSourceType(DestinationType destType) => destType switch
    {
        DestinationType.Http => "Http",
        DestinationType.Sftp => "Sftp",
        DestinationType.Ftp => "Sftp",
        DestinationType.S3 => "S3",
        DestinationType.AzureBlob => "AzureBlob",
        DestinationType.NetworkShare => "NetworkShare",
        DestinationType.Local => "Local",
        _ => "Local"
    };

    /// <summary>
    /// Translate a Destination's ConfigurationJson into the JSON shape expected by the
    /// matching IImportSource.ParseConfig(). Profile-level overrides are merged on top.
    /// </summary>
    private static string TranslateToSourceConfig(
        DestinationType destType,
        string destConfigJson,
        string? profileOverrideJson)
    {
        try
        {
            using var destDoc = JsonDocument.Parse(destConfigJson);
            var destRoot = destDoc.RootElement;

            var merged = new Dictionary<string, object?>();

            string? GetStr(params string[] keys)
            {
                foreach (var key in keys)
                    if (destRoot.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
                        return el.GetString();
                return null;
            }

            int? GetInt(string key)
            {
                if (destRoot.TryGetProperty(key, out var el) && el.TryGetInt32(out int v)) return v;
                return null;
            }

            switch (destType)
            {
                case DestinationType.Http:
                    merged["url"] = GetStr("Url", "url");
                    merged["method"] = GetStr("Method", "method") ?? "GET";
                    merged["authType"] = GetStr("AuthType", "authType");
                    merged["authToken"] = GetStr("AuthToken", "authToken");
                    merged["timeoutSeconds"] = GetInt("TimeoutSeconds") ?? 300;
                    // Copy headers JSON if present
                    if (destRoot.TryGetProperty("Headers", out var hdrsEl) || destRoot.TryGetProperty("headers", out hdrsEl))
                        merged["headers"] = JsonSerializer.Deserialize<Dictionary<string, string>>(hdrsEl.GetRawText());
                    break;

                case DestinationType.Sftp:
                case DestinationType.Ftp:
                    merged["host"] = GetStr("Host", "host");
                    merged["port"] = GetInt("Port") ?? (destType == DestinationType.Ftp ? 21 : 22);
                    merged["username"] = GetStr("Username", "username");
                    merged["password"] = GetStr("Password", "password");
                    merged["privateKey"] = GetStr("PrivateKey", "privateKey");
                    merged["privateKeyPassphrase"] = GetStr("PrivateKeyPassphrase", "privateKeyPassphrase");
                    merged["path"] = GetStr("RemotePath", "remotePath");
                    break;

                case DestinationType.S3:
                    merged["bucketName"] = GetStr("BucketName", "bucketName");
                    merged["region"] = GetStr("Region", "region");
                    merged["accessKey"] = GetStr("AccessKey", "accessKey");
                    merged["secretKey"] = GetStr("SecretKey", "secretKey");
                    merged["prefix"] = GetStr("Prefix", "prefix") ?? "";
                    merged["serviceUrl"] = GetStr("ServiceUrl", "serviceUrl");
                    break;

                case DestinationType.AzureBlob:
                    merged["connectionString"] = GetStr("ConnectionString", "connectionString");
                    merged["container"] = GetStr("ContainerName", "containerName", "Container", "container");
                    merged["path"] = GetStr("BlobPrefix", "blobPrefix", "Path", "path") ?? "";
                    break;

                case DestinationType.NetworkShare:
                    merged["uncPath"] = GetStr("UncPath", "uncPath");
                    merged["basePath"] = GetStr("BasePath", "basePath");
                    merged["subFolder"] = GetStr("SubFolder", "subFolder");
                    merged["username"] = GetStr("Username", "username");
                    merged["password"] = GetStr("Password", "password");
                    merged["domain"] = GetStr("Domain", "domain");
                    break;

                case DestinationType.Local:
                    merged["basePath"] = GetStr("BasePath", "basePath");
                    merged["path"] = GetStr("BasePath", "basePath");
                    break;
            }

            // Merge profile-level overrides on top (profile fields win)
            if (!string.IsNullOrWhiteSpace(profileOverrideJson))
            {
                try
                {
                    using var overrideDoc = JsonDocument.Parse(profileOverrideJson);
                    foreach (var prop in overrideDoc.RootElement.EnumerateObject())
                    {
                        merged[prop.Name] = JsonSerializer.Deserialize<object?>(prop.Value.GetRawText());
                    }
                }
                catch { /* invalid override JSON — ignore */ }
            }

            return JsonSerializer.Serialize(merged);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DestinationSourceAdapter: failed to translate config for {Type}", destType);
            return profileOverrideJson ?? "{}";
        }
    }
}
