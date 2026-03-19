using Reef.Core.Models;

namespace Reef.Core.Sources;

/// <summary>
/// Creates the appropriate IImportSource implementation for a given source type.
/// </summary>
public static class ImportSourceFactory
{
    public static IImportSource Create(string sourceType) => sourceType.ToUpperInvariant() switch
    {
        "LOCAL" or "DISK" or "FILE" => new LocalFileSource(),
        "SFTP" or "FTP" => new SftpImportSource(),
        "HTTP" or "HTTPS" or "REST" or "RESTAPI" => new HttpApiSource(),
        "S3" or "AWSS3" => new S3ImportSource(),
        "AZUREBLOB" or "AZURE" => new AzureBlobImportSource(),
        "NETWORKSHARE" or "SMB" or "UNC" => new NetworkShareImportSource(),
        _ => throw new NotSupportedException($"Import source type '{sourceType}' is not supported. Supported: Local, Sftp, Http, S3, AzureBlob, NetworkShare")
    };

    public static string[] SupportedTypes => new[] { "Local", "Sftp", "Http", "S3", "AzureBlob", "NetworkShare" };
}
