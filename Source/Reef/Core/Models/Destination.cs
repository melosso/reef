namespace Reef.Core.Models;

public class Destination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DestinationType Type { get; set; }
    public string ConfigurationJson { get; set; } = string.Empty; // Encrypted
    public bool IsActive { get; set; } = true;
    public string Tags { get; set; } = string.Empty; // Comma-separated
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
    public string Hash { get; set; } = string.Empty;
}

public enum DestinationType
{
    Local = 1,
    Ftp = 2,
    Sftp = 3,
    S3 = 4,
    AzureBlob = 5,
    WebDav = 6,
    Email = 10,
    Http = 11,
    NetworkShare = 12
}

public class DestinationConfiguration
{
    // Local
    public string? BasePath { get; set; }
    public bool CreateDirectories { get; set; } = true;
    public bool UseRelativePath { get; set; } = false;

    // FTP/SFTP
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }  // Path to private key file (legacy)
    public string? PrivateKey { get; set; }  // Private key content (preferred for SFTP)
    public string? PrivateKeyPassphrase { get; set; }  // Passphrase for encrypted private keys
    public string? RemotePath { get; set; }
    public bool UseSsl { get; set; }
    public bool UsePassiveMode { get; set; } = true;
    
    // S3
    public string? BucketName { get; set; }
    public string? Region { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Prefix { get; set; }
    public string? StorageClass { get; set; }
    public string? ServiceUrl { get; set; }  // Custom endpoint for MinIO/S3-compatible storage
    public string? Acl { get; set; }
    public bool ServerSideEncryption { get; set; } = true;
    
    // Azure Blob
    public string? ConnectionString { get; set; }
    public string? ContainerName { get; set; }
    public string? BlobPrefix { get; set; }
    
    // Email
    public string? SmtpServer { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? FromAddress { get; set; }
    public string? ToAddresses { get; set; }
    public string? Subject { get; set; }
    public bool EnableSsl { get; set; } = true;
    public string? SmtpAuthType { get; set; } = "Basic"; // Basic, OAuth2, None
    public string? OauthToken { get; set; }
    public string? OauthUsername { get; set; }
    
    // HTTP
    public string? Url { get; set; }
    public string? Method { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? AuthType { get; set; }
    public string? AuthToken { get; set; }
    
    // Network Share
    public string? UncPath { get; set; }
    public string? Domain { get; set; }
    public string? SubFolder { get; set; }
    
    // Common
    public int TimeoutSeconds { get; set; } = 300;
    public int RetryCount { get; set; } = 3;
    public bool ValidateCertificate { get; set; } = true;
    public Dictionary<string, string>? CustomProperties { get; set; }
}

public class DestinationTestRequest
{
    public int DestinationId { get; set; }
    public string? TestFileName { get; set; }
    public string? TestContent { get; set; }
}

public class DestinationTestConfigRequest
{
    public DestinationType Type { get; set; }
    public string ConfigurationJson { get; set; } = string.Empty;
    public string? TestFileName { get; set; }
    public string? TestContent { get; set; }
}

public class DestinationTestResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? FilePath { get; set; }
    public string? TestFilePath { get; set; }
    public long? BytesWritten { get; set; }
    public TimeSpan Duration { get; set; }
    public int? ResponseTimeMs { get; set; }
}

public class DestinationEndpoint
{
    public int Id { get; set; }
    public int DestinationId { get; set; }
    public string Name { get; set; } = string.Empty;
    // HTTP
    public string? PathSuffix { get; set; }
    public string? Method { get; set; }
    public string? UploadFormat { get; set; }
    public string? ExtraHeaders { get; set; } // JSON {"X-Tenant":"600"}
    // SFTP / FTP
    public string? RemotePath { get; set; }
    // S3
    public string? KeyPrefix { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
}