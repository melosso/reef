// Source/Reef/Core/Destinations/AzureBlobDestination.cs
// Azure Blob Storage destination for file uploads

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Reef.Core.Services;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Destinations;

/// <summary>
/// Destination for uploading files to Azure Blob Storage
/// </summary>
public class AzureBlobDestination : IDestination
{
    private readonly EncryptionService _encryptionService;

    public AzureBlobDestination(EncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(
        string sourcePath, string destinationConfig)
    {
        try
        {
            // Parse configuration
            var config = JsonSerializer.Deserialize<AzureBlobConfig>(destinationConfig);
            if (config == null)
            {
                return (false, null, "Invalid Azure Blob configuration");
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.ConnectionString) || string.IsNullOrWhiteSpace(config.Container))
            {
                return (false, null, "Azure connection string and container are required");
            }

            // Decrypt connection string
            var connectionString = config.ConnectionString.StartsWith("PWENC:")
                ? _encryptionService.Decrypt(config.ConnectionString.Substring(6))
                : config.ConnectionString;

            // Replace placeholders in path
            var blobName = ReplacePlaceholders(config.Path ?? "", Path.GetFileName(sourcePath));

            // Create blob service client
            var blobServiceClient = new BlobServiceClient(connectionString);

            // Get or create container
            var containerClient = blobServiceClient.GetBlobContainerClient(config.Container);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

            // Get blob client
            var blobClient = containerClient.GetBlobClient(blobName);

            // Set content type based on file extension
            var contentType = GetContentType(sourcePath);
            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            };

            // Upload file
            using var fileStream = File.OpenRead(sourcePath);
            await blobClient.UploadAsync(fileStream, new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders
            });

            var blobUrl = blobClient.Uri.ToString();
            Log.Information("Uploaded file to Azure Blob Storage: {BlobName}", blobName);
            return (true, blobUrl, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error uploading to Azure Blob Storage");
            return (false, null, ex.Message);
        }
    }

    private string ReplacePlaceholders(string path, string fileName)
    {
        var now = DateTime.Now;
        var blobName = path
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{timestamp}", now.ToString("yyyyMMdd_HHmmss"))
            .Replace("{filename}", fileName)
            .Replace("{guid}", Guid.NewGuid().ToString())
            .TrimEnd('/');

        return string.IsNullOrWhiteSpace(blobName) ? fileName : $"{blobName}/{fileName}";
    }

    private string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".html" => "text/html",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".yaml" => "application/x-yaml",
            ".yml" => "application/x-yaml",
            _ => "application/octet-stream"
        };
    }
}

public class AzureBlobConfig
{
    public required string ConnectionString { get; set; }
    public required string Container { get; set; }
    public string? Path { get; set; } = "";
}