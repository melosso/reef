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
        _ => throw new NotSupportedException($"Import source type '{sourceType}' is not supported. Supported: Local, Sftp, Http")
    };

    public static string[] SupportedTypes => new[] { "Local", "Sftp", "Http" };
}
