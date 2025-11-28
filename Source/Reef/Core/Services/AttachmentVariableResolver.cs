using System.Text.RegularExpressions;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Resolves variables in attachment filenames (e.g., {profile_name}, {timestamp})
/// Supports both predefined variables and row data variables with safety validation
/// </summary>
public class AttachmentVariableResolver
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<AttachmentVariableResolver>();

    // Pattern to match {variable} placeholders
    private static readonly Regex VariablePattern = new(@"\{([a-zA-Z_][a-zA-Z0-9_]*)\}", RegexOptions.Compiled);

    // Valid variable names (alphanumeric and underscore only)
    private static readonly Regex ValidVariableName = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    // Invalid filename characters to prevent directory traversal and OS issues
    private static readonly Regex InvalidFilenameChars = new(@"[<>:""/\\|?*\.](?<!\.[\w]+$)|\.\.|\n|\r|\0", RegexOptions.Compiled);

    /// <summary>
    /// Predefined variables that are always available
    /// </summary>
    private readonly Dictionary<string, Func<string>> _predefinedVariables;

    public AttachmentVariableResolver()
    {
        _predefinedVariables = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "profile_name", () => "" },     // Set by caller
            { "profile_id", () => "" },       // Set by caller
            { "timestamp", () => DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") },
            { "timestamp_iso", () => DateTime.UtcNow.ToString("O") },
            { "date", () => DateTime.UtcNow.ToString("yyyyMMdd") },
            { "time", () => DateTime.UtcNow.ToString("HHmmss") },
            { "execution_id", () => "" },     // Set by caller
            { "batch_number", () => "" }      // Set by caller
        };
    }

    /// <summary>
    /// Resolves template variables and returns a safe filename
    /// </summary>
    /// <param name="template">Template string with {variable} placeholders</param>
    /// <param name="rowData">Row data from query result (optional)</param>
    /// <param name="profileName">Profile name</param>
    /// <param name="profileId">Profile ID</param>
    /// <param name="executionId">Execution ID</param>
    /// <param name="batchNumber">Batch/split number</param>
    /// <returns>Resolved and sanitized filename</returns>
    /// <exception cref="InvalidOperationException">If template contains invalid variables</exception>
    public string ResolveTemplate(
        string template,
        Dictionary<string, object>? rowData = null,
        string? profileName = null,
        string? profileId = null,
        string? executionId = null,
        int? batchNumber = null)
    {
        if (string.IsNullOrWhiteSpace(template))
            return GenerateDefaultFilename();

        rowData ??= new Dictionary<string, object>();

        // Build the variables dictionary for this resolution
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "profile_name", profileName ?? "export" },
            { "profile_id", profileId ?? "0" },
            { "timestamp", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") },
            { "timestamp_iso", DateTime.UtcNow.ToString("O") },
            { "date", DateTime.UtcNow.ToString("yyyyMMdd") },
            { "time", DateTime.UtcNow.ToString("HHmmss") },
            { "execution_id", executionId ?? "unknown" },
            { "batch_number", batchNumber?.ToString() ?? "0" }
        };

        // Add row data variables (only if column name is valid)
        foreach (var kvp in rowData)
        {
            if (ValidVariableName.IsMatch(kvp.Key) && kvp.Value != null)
            {
                variables[kvp.Key] = kvp.Value.ToString() ?? "";
            }
        }

        // Replace all {variable} patterns
        string result = VariablePattern.Replace(template, match =>
        {
            string varName = match.Groups[1].Value;

            if (variables.TryGetValue(varName, out var value))
            {
                return SanitizeVariableValue(value);
            }

            throw new InvalidOperationException($"Variable '{varName}' is not available. " +
                $"Available: profile_name, profile_id, timestamp, date, time, execution_id, batch_number");
        });

        // Sanitize the entire filename
        return SanitizeFilename(result);
    }

    /// <summary>
    /// Generates a default filename if template is empty
    /// </summary>
    private string GenerateDefaultFilename()
    {
        return $"export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.bin";
    }

    /// <summary>
    /// Sanitizes a variable value before substitution (basic cleanup)
    /// </summary>
    private string SanitizeVariableValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        // Remove any path separators and dangerous characters
        value = value.Replace("\\", "_")
                     .Replace("/", "_")
                     .Replace(":", "_");

        // Trim whitespace
        value = value.Trim();

        return value;
    }

    /// <summary>
    /// Sanitizes a filename to prevent directory traversal and invalid characters
    /// </summary>
    public string SanitizeFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return GenerateDefaultFilename();

        // Remove invalid characters
        filename = InvalidFilenameChars.Replace(filename, "_");

        // Remove leading/trailing spaces and dots
        filename = filename.Trim().TrimStart('.').TrimEnd('.');

        // Prevent directory traversal
        filename = filename.Replace("..", "_");

        // Ensure we have at least some filename
        if (string.IsNullOrWhiteSpace(filename) || filename == "_")
            return GenerateDefaultFilename();

        // Limit length (Windows max is 255, but be conservative)
        if (filename.Length > 200)
        {
            filename = filename.Substring(0, 200);
        }

        return filename;
    }

    /// <summary>
    /// Validates a template for syntax errors without executing it
    /// </summary>
    /// <param name="template">Template string to validate</param>
    /// <param name="availableColumns">Column names available in the row data</param>
    /// <returns>List of validation errors (empty if valid)</returns>
    public List<string> ValidateTemplate(string template, IEnumerable<string>? availableColumns = null)
    {
        var errors = new List<string>();
        availableColumns ??= new List<string>();

        var matches = VariablePattern.Matches(template);
        foreach (Match match in matches)
        {
            string varName = match.Groups[1].Value;

            // Check if it's a predefined variable
            if (_predefinedVariables.ContainsKey(varName))
                continue;

            // Check if it's a column name
            if (availableColumns.Any(c => c.Equals(varName, StringComparison.OrdinalIgnoreCase)))
                continue;

            // Variable not found
            errors.Add($"Variable '{varName}' is not available");
        }

        return errors;
    }

    /// <summary>
    /// Gets list of available variables for documentation/UI
    /// </summary>
    public static List<string> GetAvailableVariables()
    {
        return new List<string>
        {
            "profile_name - Profile name",
            "profile_id - Profile ID",
            "timestamp - Current UTC timestamp (yyyyMMdd_HHmmss)",
            "timestamp_iso - ISO 8601 timestamp",
            "date - Date only (yyyyMMdd)",
            "time - Time only (HHmmss)",
            "execution_id - Unique execution ID",
            "batch_number - Current batch/split number",
            "{column_name} - Any column from query result"
        };
    }
}
