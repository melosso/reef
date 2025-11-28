using Serilog;
using System.Xml.Linq;

namespace Reef.Core.Formatters;

/// <summary>
/// XML formatter for query results
/// Outputs data as XML with row and column structure
/// </summary>
public class XmlFormatter : IFormatter
{
    /// <summary>
    /// Format data as XML and write to file
    /// Format: <rows><row><columnName>value</columnName></row></rows>
    /// </summary>
    public async Task<(bool Success, long FileSizeBytes, string? ErrorMessage)> FormatAsync(
        List<Dictionary<string, object>> data, 
        string outputPath)
    {
        try
        {
            Log.Debug("Formatting {RowCount} rows as XML to {OutputPath}", data.Count, outputPath);

            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create XML document
            var root = new XElement("rows");

            foreach (var rowData in data)
            {
                var rowElement = new XElement("row");

                foreach (var kvp in rowData)
                {
                    var columnName = SanitizeXmlName(kvp.Key);
                    var columnValue = ConvertValueToXmlString(kvp.Value);

                    var columnElement = new XElement(columnName, columnValue);
                    rowElement.Add(columnElement);
                }

                root.Add(rowElement);
            }

            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                root
            );

            // Save to file
            await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await document.SaveAsync(fileStream, SaveOptions.None, default);
            await fileStream.FlushAsync();

            var fileSize = fileStream.Length;

            Log.Debug("XML formatting completed. File size: {FileSizeBytes} bytes", fileSize);
            return (true, fileSize, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error formatting data as XML");
            return (false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Convert value to string for XML
    /// </summary>
    private string ConvertValueToXmlString(object value)
    {
        if (value == null || value == DBNull.Value)
        {
            return string.Empty;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        // XElement will automatically escape special characters (< > & " ')
        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Sanitize column name to be valid XML element name
    /// XML names must start with letter or underscore, and contain only letters, digits, hyphens, underscores, and periods
    /// </summary>
    private string SanitizeXmlName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "column";
        }

        // Replace invalid characters with underscore
        var sanitized = new System.Text.StringBuilder();

        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];

            // First character must be letter or underscore
            if (i == 0)
            {
                if (char.IsLetter(c) || c == '_')
                {
                    sanitized.Append(c);
                }
                else
                {
                    sanitized.Append('_');
                    if (char.IsLetterOrDigit(c))
                    {
                        sanitized.Append(c);
                    }
                }
            }
            else
            {
                // Subsequent characters can be letter, digit, hyphen, underscore, or period
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                {
                    sanitized.Append(c);
                }
                else
                {
                    sanitized.Append('_');
                }
            }
        }

        var result = sanitized.ToString();
        
        // Ensure we have a valid name
        if (string.IsNullOrEmpty(result) || result == "_")
        {
            return "column";
        }

        return result;
    }
}