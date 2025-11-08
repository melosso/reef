// Source/Reef/Core/Destinations/S3Destination.cs
// AWS S3 destination for file uploads

using Amazon.S3;
using Amazon.S3.Model;
using Reef.Core.Services;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Destinations;

/// <summary>
/// Destination for uploading files to AWS S3
/// </summary>
public class S3Destination : IDestination
{
    private readonly EncryptionService _encryptionService;

    public S3Destination(EncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(
        string sourcePath, string destinationConfig)
    {
        try
        {
            // Parse configuration
            var config = JsonSerializer.Deserialize<S3Config>(destinationConfig);
            if (config == null)
            {
                return (false, null, "Invalid S3 configuration");
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.BucketName) || string.IsNullOrWhiteSpace(config.Region))
            {
                return (false, null, "S3 bucket and region are required");
            }

            // Decrypt credentials
            var accessKey = config.AccessKey?.StartsWith("PWENC:") == true
                ? _encryptionService.Decrypt(config.AccessKey.Substring(6))
                : config.AccessKey;

            var secretKey = config.SecretKey?.StartsWith("PWENC:") == true
                ? _encryptionService.Decrypt(config.SecretKey.Substring(6))
                : config.SecretKey;

            // Replace placeholders in path
            var s3Key = ReplacePlaceholders(config.Prefix ?? "", Path.GetFileName(sourcePath));

            // Create S3 client
            var s3Config = new AmazonS3Config();

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
                s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config.Region);
            }

            using var client = new AmazonS3Client(accessKey, secretKey, s3Config);

            // Upload file
            var putRequest = new PutObjectRequest
            {
                BucketName = config.BucketName,
                Key = s3Key,
                FilePath = sourcePath
            };

            // Set ACL if specified
            if (!string.IsNullOrWhiteSpace(config.Acl))
            {
                putRequest.CannedACL = config.Acl.ToLower() switch
                {
                    "private" => S3CannedACL.Private,
                    "public-read" => S3CannedACL.PublicRead,
                    "public-read-write" => S3CannedACL.PublicReadWrite,
                    "authenticated-read" => S3CannedACL.AuthenticatedRead,
                    _ => S3CannedACL.Private
                };
            }

            // Set storage class if specified
            if (!string.IsNullOrWhiteSpace(config.StorageClass))
            {
                putRequest.StorageClass = config.StorageClass.ToUpper() switch
                {
                    "STANDARD" => S3StorageClass.Standard,
                    "STANDARD_IA" => S3StorageClass.StandardInfrequentAccess,
                    "ONEZONE_IA" => S3StorageClass.OneZoneInfrequentAccess,
                    "INTELLIGENT_TIERING" => S3StorageClass.IntelligentTiering,
                    "GLACIER" => S3StorageClass.Glacier,
                    "GLACIER_IR" => S3StorageClass.GlacierInstantRetrieval,
                    "DEEP_ARCHIVE" => S3StorageClass.DeepArchive,
                    _ => S3StorageClass.Standard
                };
                Log.Debug("S3 storage class set to: {StorageClass}", config.StorageClass);
            }

            // Enable server-side encryption if specified
            if (config.ServerSideEncryption)
            {
                putRequest.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
                Log.Debug("S3 server-side encryption enabled (AES-256)");
            }

            var response = await client.PutObjectAsync(putRequest);

            if ((int)response.HttpStatusCode >= 200 && (int)response.HttpStatusCode < 300)
            {
                // Generate appropriate URL based on endpoint type
                string s3Url;
                if (!string.IsNullOrWhiteSpace(config.ServiceUrl))
                {
                    // Custom endpoint (MinIO, Wasabi, etc.) - use path-style URL
                    var baseUrl = config.ServiceUrl.TrimEnd('/');
                    s3Url = $"{baseUrl}/{config.BucketName}/{s3Key}";
                }
                else
                {
                    // AWS S3 - use virtual-hosted-style URL
                    s3Url = $"https://{config.BucketName}.s3.{config.Region}.amazonaws.com/{s3Key}";
                }

                Log.Information("Uploaded file to S3: {S3Key}", s3Key);
                return (true, s3Url, null);
            }
            else
            {
                return (false, null, $"S3 upload failed with status code: {response.HttpStatusCode}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error uploading to S3");
            return (false, null, ex.Message);
        }
    }

    private string ReplacePlaceholders(string path, string fileName)
    {
        var now = DateTime.Now;
        var key = path
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{timestamp}", now.ToString("yyyyMMdd_HHmmss"))
            .Replace("{filename}", fileName)
            .Replace("{guid}", Guid.NewGuid().ToString())
            .TrimEnd('/');

        // Ensure key doesn't start with /
        key = key.TrimStart('/');
        
        return string.IsNullOrWhiteSpace(key) ? fileName : $"{key}/{fileName}";
    }
}

public class S3Config
{
    public required string BucketName { get; set; }  // Match DestinationConfiguration property name
    public required string Region { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Prefix { get; set; } = "";  // Match DestinationConfiguration property name
    public string? Acl { get; set; } = "private";
    public string? StorageClass { get; set; } = "STANDARD";
    public bool ServerSideEncryption { get; set; } = true;
    public string? ServiceUrl { get; set; }  // Custom endpoint for MinIO/S3-compatible storage (e.g., "https://minio.internal.domain:9000")
}