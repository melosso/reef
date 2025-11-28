using System.Text.RegularExpressions;
using Serilog;
using Reef.Core.Models;

namespace Reef.Core.Services;

/// <summary>
/// Resolves email attachments from binary data embedded in query results
/// Handles filename validation, deduplication, and content type inference
/// </summary>
public class BinaryAttachmentResolver
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<BinaryAttachmentResolver>();

    private readonly AttachmentVariableResolver _variableResolver;

    // Pattern to match content types
    private static readonly Dictionary<string, string> CommonMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".pdf", "application/pdf" },
        { ".doc", "application/msword" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".xls", "application/vnd.ms-excel" },
        { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { ".ppt", "application/vnd.ms-powerpoint" },
        { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { ".txt", "text/plain" },
        { ".csv", "text/csv" },
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".zip", "application/zip" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".bmp", "image/bmp" },
        { ".svg", "image/svg+xml" }
    };

    public BinaryAttachmentResolver(AttachmentVariableResolver? variableResolver = null)
    {
        _variableResolver = variableResolver ?? new AttachmentVariableResolver();
    }

    /// <summary>
    /// Resolves attachments from a single row of query results
    /// </summary>
    /// <param name="row">Query result row as dictionary</param>
    /// <param name="config">Binary attachment configuration</param>
    /// <param name="profileName">Profile name (for logging)</param>
    /// <param name="profileId">Profile ID (for logging)</param>
    /// <returns>List of resolved attachments from this row</returns>
    /// <exception cref="InvalidOperationException">If required columns are missing or invalid</exception>
    public List<EmailAttachment> ResolveAttachmentsForRow(
        Dictionary<string, object> row,
        BinaryAttachmentMode config,
        string profileName = "unknown",
        int profileId = 0)
    {
        var attachments = new List<EmailAttachment>();

        // Validate required columns exist
        if (!row.ContainsKey(config.ContentColumnName))
            throw new InvalidOperationException(
                $"Content column '{config.ContentColumnName}' not found in query results. " +
                $"Available columns: {string.Join(", ", row.Keys)}");

        if (!row.ContainsKey(config.FilenameColumnName))
            throw new InvalidOperationException(
                $"Filename column '{config.FilenameColumnName}' not found in query results. " +
                $"Available columns: {string.Join(", ", row.Keys)}");

        // Extract content
        var contentValue = row[config.ContentColumnName];
        if (contentValue == null)
        {
            Log.Warning("Content column '{Column}' is NULL for profile '{Profile}' (ID: {ProfileId})",
                config.ContentColumnName, profileName, profileId);
            return attachments; // Skip this row
        }

        byte[] fileContent;
        if (contentValue is byte[] bytes)
        {
            fileContent = bytes;
        }
        else if (contentValue is string base64String && IsBase64(base64String))
        {
            try
            {
                fileContent = Convert.FromBase64String(base64String);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to decode Base64 content in '{Column}' for profile '{Profile}'",
                    config.ContentColumnName, profileName);
                return attachments; // Skip this row
            }
        }
        else
        {
            Log.Warning("Content column '{Column}' does not contain binary data or valid Base64 for profile '{Profile}'",
                config.ContentColumnName, profileName);
            return attachments; // Skip this row
        }

        // Extract filename
        var filenameValue = row[config.FilenameColumnName];
        if (filenameValue == null)
        {
            Log.Warning("Filename column '{Column}' is NULL for profile '{Profile}'",
                config.FilenameColumnName, profileName);
            return attachments; // Skip this row
        }

        string filename = filenameValue.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(filename))
        {
            Log.Warning("Filename column '{Column}' is empty for profile '{Profile}'",
                config.FilenameColumnName, profileName);
            return attachments; // Skip this row
        }

        // Sanitize filename
        string safeFilename = _variableResolver.SanitizeFilename(filename);
        if (string.IsNullOrWhiteSpace(safeFilename))
        {
            Log.Warning("Filename '{Filename}' could not be sanitized for profile '{Profile}'",
                filename, profileName);
            return attachments; // Skip this row
        }

        // Infer content type
        string contentType = InferContentType(safeFilename);

        attachments.Add(new EmailAttachment
        {
            Filename = safeFilename,
            Content = fileContent,
            ContentType = contentType
        });

        return attachments;
    }

    /// <summary>
    /// Infers MIME content type from filename extension
    /// </summary>
    private string InferContentType(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return "application/octet-stream";

        var extension = Path.GetExtension(filename).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
            return "application/octet-stream";

        // Try common types first
        if (CommonMimeTypes.TryGetValue(extension, out var mimeType))
            return mimeType;

        // Default fallback
        return "application/octet-stream";
    }

    /// <summary>
    /// Simple Base64 validation
    /// </summary>
    private bool IsBase64(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            // Remove whitespace and check if divisible by 4
            value = value.Trim();
            if (value.Length % 4 != 0)
                return false;

            Convert.FromBase64String(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates binary attachment configuration
    /// </summary>
    /// <param name="config">Configuration to validate</param>
    /// <param name="availableColumns">Columns available in query results</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    public List<string> ValidateConfiguration(
        BinaryAttachmentMode config,
        IEnumerable<string> availableColumns)
    {
        var errors = new List<string>();
        var columnList = availableColumns?.ToList() ?? new List<string>();

        // Check content column
        if (string.IsNullOrWhiteSpace(config.ContentColumnName))
        {
            errors.Add("Content column name is required");
        }
        else if (!columnList.Any(c => c.Equals(config.ContentColumnName, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Content column '{config.ContentColumnName}' not found in query results");
        }

        // Check filename column
        if (string.IsNullOrWhiteSpace(config.FilenameColumnName))
        {
            errors.Add("Filename column name is required");
        }
        else if (!columnList.Any(c => c.Equals(config.FilenameColumnName, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Filename column '{config.FilenameColumnName}' not found in query results");
        }

        return errors;
    }
}
