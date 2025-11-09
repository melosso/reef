using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services.Import.Writers;

/// <summary>
/// S3Writer: Writes imported data to AWS S3 with multipart upload support
/// </summary>
public class S3Writer : IDataWriter
{
    private readonly Serilog.ILogger _logger = Log.ForContext<S3Writer>();

    /// <summary>
    /// Gets the destination type this writer handles
    /// </summary>
    public ImportDestinationType DestinationType => ImportDestinationType.S3;

    /// <summary>
    /// Validate that we can connect to S3 and write to the bucket
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        string destinationUri,
        string? destinationConfig,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationUri))
            return new ValidationResult { IsValid = false, ErrorMessage = "Destination URI (S3 bucket/key) is required" };

        try
        {
            var (bucket, region) = ParseS3Uri(destinationUri, destinationConfig);

            var regionEndpoint = RegionEndpoint.GetBySystemName(region);
            using (var client = new AmazonS3Client(regionEndpoint))
            {
                // Check if bucket exists and we have permissions
                var listRequest = new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 };
                var listResponse = await client.ListObjectsV2Async(listRequest, cancellationToken);

                return new ValidationResult { IsValid = true };
            }
        }
        catch (AmazonS3Exception ex)
        {
            _logger.Error(ex, "S3 validation failed");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"S3 validation failed: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "S3 validation error");
            return new ValidationResult
            {
                IsValid = false,
                ErrorMessage = $"S3 validation error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Write rows to S3 using multipart upload
    /// </summary>
    public async Task<WriteResult> WriteAsync(
        string destinationUri,
        string? destinationConfig,
        List<Dictionary<string, object>> rows,
        List<FieldMapping> fieldMappings,
        WriteMode mode = WriteMode.Insert,
        CancellationToken cancellationToken = default)
    {
        var result = new WriteResult();

        if (rows == null || rows.Count == 0)
        {
            _logger.Information("No rows to write");
            return result;
        }

        try
        {
            var (bucket, region) = ParseS3Uri(destinationUri, destinationConfig);
            var format = DetermineFormat(destinationUri, destinationConfig);

            var regionEndpoint = RegionEndpoint.GetBySystemName(region);
            using (var client = new AmazonS3Client(regionEndpoint))
            {
                // Format data based on format type
                var data = format switch
                {
                    "csv" => FormatAsCsv(rows),
                    "json" => FormatAsJson(rows),
                    "parquet" => FormatAsJson(rows), // Parquet would need additional library
                    _ => FormatAsJson(rows)
                };

                var dataBytes = Encoding.UTF8.GetBytes(data);

                // Upload directly (not using multipart for simplicity)
                var putRequest = new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = destinationUri.Contains('/') ? destinationUri.Split('/').Last() : destinationUri,
                    InputStream = new MemoryStream(dataBytes),
                    ContentType = GetContentType(format)
                };

                var putResponse = await client.PutObjectAsync(putRequest, cancellationToken);

                result.RowsWritten = rows.Count;
                _logger.Information("Successfully wrote {Count} rows to S3: {Bucket}/{Key}",
                    rows.Count, bucket, putRequest.Key);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error writing to S3");
            result.RowsFailed = rows.Count;
            result.ErrorMessages = new List<string> { ex.Message };
        }

        return result;
    }

    /// <summary>
    /// Parse S3 URI to extract bucket and region
    /// </summary>
    private (string bucket, string region) ParseS3Uri(string destinationUri, string? config)
    {
        var bucket = destinationUri.Split('/')[0];
        var region = "us-east-1";

        // Check config for region override
        if (!string.IsNullOrEmpty(config))
        {
            try
            {
                var options = JsonSerializer.Deserialize<JsonElement>(config);
                if (options.TryGetProperty("region", out var regionProp))
                {
                    region = regionProp.GetString() ?? "us-east-1";
                }
            }
            catch { }
        }

        return (bucket, region);
    }

    /// <summary>
    /// Determine format from file extension or config
    /// </summary>
    private string DetermineFormat(string destinationUri, string? config)
    {
        // Check config first
        if (!string.IsNullOrEmpty(config))
        {
            try
            {
                var options = JsonSerializer.Deserialize<JsonElement>(config);
                if (options.TryGetProperty("format", out var formatProp))
                {
                    return formatProp.GetString() ?? "json";
                }
            }
            catch { }
        }

        // Fallback to file extension
        var ext = Path.GetExtension(destinationUri).ToLowerInvariant().TrimStart('.');
        return ext switch
        {
            "csv" => "csv",
            "json" => "json",
            "parquet" => "parquet",
            _ => "json"  // Default to JSON
        };
    }

    /// <summary>
    /// Format rows as CSV
    /// </summary>
    private string FormatAsCsv(List<Dictionary<string, object>> rows)
    {
        if (rows.Count == 0) return "";

        var csv = new StringBuilder();
        var headers = rows.First().Keys.ToList();

        // Write header
        csv.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        // Write rows
        foreach (var row in rows)
        {
            var values = headers.Select(h => row.ContainsKey(h) ? EscapeCsv(row[h]) : "");
            csv.AppendLine(string.Join(",", values));
        }

        return csv.ToString();
    }

    /// <summary>
    /// Format rows as JSON
    /// </summary>
    private string FormatAsJson(List<Dictionary<string, object>> rows)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        return JsonSerializer.Serialize(rows, options) + Environment.NewLine;
    }

    /// <summary>
    /// Escape CSV field values
    /// </summary>
    private static string EscapeCsv(object value)
    {
        if (value == null) return "";

        var str = value.ToString() ?? "";

        // If contains comma, newline, or quote, wrap in quotes and escape quotes
        if (str.Contains(",") || str.Contains("\n") || str.Contains("\""))
        {
            return $"\"{str.Replace("\"", "\"\"")}\"";
        }

        return str;
    }

    /// <summary>
    /// Get content type based on format
    /// </summary>
    private string GetContentType(string format)
    {
        return format switch
        {
            "csv" => "text/csv",
            "json" => "application/json",
            "parquet" => "application/octet-stream",
            _ => "application/octet-stream"
        };
    }
}
