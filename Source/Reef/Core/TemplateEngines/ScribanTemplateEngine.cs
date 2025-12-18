using Scriban;
using Scriban.Runtime;
using Serilog;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace Reef.Core.TemplateEngines;

/// <summary>
/// Scriban-based template engine for flexible data transformation
/// Uses native Scriban syntax for full feature support including whitespace control
/// </summary>
public class ScribanTemplateEngine : ITemplateEngine
{
    private readonly IConfiguration _configuration;
    
    public string EngineName => "Scriban";

    public ScribanTemplateEngine(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Transform data using Scriban template
    /// </summary>
    public async Task<string> TransformAsync(
        List<Dictionary<string, object>> data,
        string template,
        Dictionary<string, object>? context = null)
    {
        try
        {
            Log.Debug("Transforming {RowCount} rows using Scriban template", data.Count);
            
            //Log.Debug("Template content (first 200 chars): {Template}", 
            //    template.Length > 200 ? template.Substring(0, 200) + "..." : template);
            
            // Log first row keys if any data exists
            if (data.Any())
            {
                var firstRowKeys = string.Join(", ", data[0].Keys);
                Log.Debug("First row has {KeyCount} keys: {Keys}", data[0].Count, firstRowKeys);
                
                // Log a sample of values
                // var sampleData = data[0].Take(5).Select(kvp => $"{kvp.Key}={kvp.Value ?? "(null)"}");
                // Log.Debug("Sample data: {SampleData}", string.Join(", ", sampleData));
            }

            var scribanTemplate = Template.Parse(template);

            if (scribanTemplate.HasErrors)
            {
                var errors = string.Join("; ", scribanTemplate.Messages.Select(m => m.Message));
                throw new InvalidOperationException($"Template parsing error: {errors}");
            }

            // Filter out any null entries and ensure all rows have data
            var cleanData = data
                .Where(dict => dict != null && dict.Count > 0)
                .ToList();

            if (cleanData.Count == 0)
            {
                Log.Warning("No valid data rows to render in template");
            }
            else
            {
                // Log available columns from first row for debugging
                var firstRowKeys = string.Join(", ", cleanData[0].Keys);
                Log.Debug("Template rendering {Count} rows with columns: {Keys}", cleanData.Count, firstRowKeys);
            }

            // Convert dictionaries to ScriptObject for Scriban
            // Dictionary keys need to be added as ScriptObject properties for dot notation access
            var scriptData = new ScriptArray();
            foreach (var dict in cleanData)
            {
                var rowObject = new ScriptObject();
                // Add each key-value pair directly to the ScriptObject
                foreach (var kvp in dict)
                {
                    rowObject[kvp.Key] = kvp.Value;
                }
                
                // DIAGNOSTIC: Verify the ScriptObject actually has the keys
                // if (scriptData.Count == 0) // Only log first row
                // {
                //     Log.Debug("Showing ScriptObject for first row:");
                //     Log.Debug("  ScriptObject members: {Members}", string.Join(", ", rowObject.GetMembers()));
                //     Log.Debug("  ScriptObject.Count: {Count}", rowObject.Count);
                //     var hasItemCode = rowObject.TryGetValue("ItemCode", out var testValue);
                //     Log.Debug("  TryGetValue('ItemCode'): {HasValue}, Value={Value}", hasItemCode, testValue);
                //     Log.Debug("  Direct indexer rowObject['ItemCode']: {Value}", rowObject["ItemCode"]);
                // }
                
                scriptData.Add(rowObject);
            }

            var scriptObject = new ScriptObject
            {
                { "data", scriptData },
                { "rows", scriptData },
                { "row_count", scriptData.Count }
            };

            // Import helpers
            scriptObject.Import(new TemplateHelpers());

            // Add first row's columns directly to root context for convenience
            // This allows {{ column_name }} syntax in addition to {{ rows[0].column_name }}
            if (cleanData.Count > 0)
            {
                foreach (var kvp in cleanData[0])
                {
                    // Only add if not already in scriptObject (avoid conflicts with built-in properties)
                    if (!scriptObject.ContainsKey(kvp.Key))
                    {
                        scriptObject.Add(kvp.Key, kvp.Value);
                    }
                }
            }

            // Add any additional context
            if (context != null)
            {
                foreach (var kvp in context)
                    scriptObject.Add(kvp.Key, kvp.Value);
            }

            // Calculate dynamic loop limit based on data size
            // Account for nested loops in templates (e.g., row loop × field loops)
            // Use a safety multiplier: rows × columns × reasonable nesting factor (100)
            var columnCount = cleanData.Count > 0 ? cleanData[0].Count : 1;
            var dynamicLoopLimit = Math.Max(10000, cleanData.Count * columnCount * 100);
            Log.Debug("Setting dynamic loop limit to {LoopLimit} for {RowCount} rows with {ColumnCount} columns", 
                dynamicLoopLimit, cleanData.Count, columnCount);

            var templateContext = new TemplateContext
            {
                StrictVariables = false, // Don't fail on missing variables
                EnableRelaxedMemberAccess = true, // Allow accessing dictionary keys as properties
                EnableRelaxedIndexerAccess = true, // Allow flexible indexer access
                EnableRelaxedTargetAccess = true,   // Allow relaxed target access
                LoopLimit = dynamicLoopLimit, // Dynamic limit based on data size and complexity
                RecursiveLimit = 100, // Protect against infinite recursion
                MemberRenamer = StandardMemberRenamer.Default // Convert CamelCase to snake_case for .NET objects (safe - only affects methods, not ScriptObject keys)
            };
            
            templateContext.PushGlobal(scriptObject);
            
            // Import all Scriban built-in functions
            templateContext.PushGlobal(templateContext.BuiltinObject);

            // Render template ONCE with all data
            string output;
            try
            {
                // Test with a simple debug template first
                if (cleanData.Count > 0)
                {
                    var testTemplate = Template.Parse(@"TEST Direct: {{ data[0].ItemCode }}
TEST Loop: {% for row in data limit:1 %}{{ row.ItemCode }}{% endfor %}
TEST Loop Type: {% for row in data limit:1 %}{{ row | object.typeof }}{% endfor %}");
                    var testOutput = await testTemplate.RenderAsync(templateContext);

                    // Log.Debug("Template Test:\n{Output}", testOutput);
                }
                
                output = await scribanTemplate.RenderAsync(templateContext);
                
                // Log.Debug ("Template rendered output (first 500 chars): {Output}", 
                //    output.Length > 500 ? output.Substring(0, 500) + "..." : output);
            }
            catch (Exception renderEx)
            {
                // Log the error but provide helpful context about missing columns
                Log.Error(renderEx, "Template rendering error. Available columns: {Columns}. " +
                    "Ensure your template only references columns that exist in the query result.",
                    cleanData.Count > 0 ? string.Join(", ", cleanData[0].Keys) : "none");
                throw new InvalidOperationException(
                    $"Template rendering failed: {renderEx.Message}. " +
                    $"Check that template references match query columns.", renderEx);
            }
            
            // Apply prettification if enabled in configuration
            output = PrettifyOutput(output);
            
            return output;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error transforming data with Scriban template");
            throw;
        }
    }

    /// <summary>
    /// Prettify XML or JSON output based on configuration
    /// </summary>
    private string PrettifyOutput(string output)
    {
        try
        {
            var trimmedOutput = output.TrimStart();
            
            // Detect XML
            if (trimmedOutput.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) || 
                trimmedOutput.StartsWith("<"))
            {
                var prettifyXml = _configuration.GetValue<bool>("Reef:OutputFormatting:PrettifyXml", true);
                if (prettifyXml)
                {
                    var indentSize = _configuration.GetValue<int>("Reef:OutputFormatting:XmlIndentSize", 2);
                    return PrettifyXml(output, indentSize);
                }
            }
            // Detect JSON
            else if (trimmedOutput.StartsWith("{") || trimmedOutput.StartsWith("["))
            {
                var prettifyJson = _configuration.GetValue<bool>("Reef:OutputFormatting:PrettifyJson", true);
                if (prettifyJson)
                {
                    var indentSize = _configuration.GetValue<int>("Reef:OutputFormatting:JsonIndentSize", 2);
                    return PrettifyJson(output, indentSize);
                }
            }
            
            return output;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to prettify output, returning original");
            return output;
        }
    }

    /// <summary>
    /// Prettify XML with proper indentation
    /// </summary>
    private string PrettifyXml(string xml, int indentSize)
    {
        try
        {
            var doc = XDocument.Parse(xml);

            // Manually remove all nodes that are just whitespace
            var whitespaceNodes = doc.DescendantNodes()
                .OfType<XText>()
                .Where(t => string.IsNullOrWhiteSpace(t.Value))
                .ToList();

            foreach (var node in whitespaceNodes)
            {
                node.Remove();
            }           
            
            // Configure XML writer settings
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = new string(' ', indentSize),
                OmitXmlDeclaration = false,
                NewLineOnAttributes = false
            };

            using var stringWriter = new StringWriter();
            using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
            {
                doc.Save(xmlWriter);
            }

            return stringWriter.ToString();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to prettify XML, returning original");
            return xml;
        }
    }

    /// <summary>
    /// Prettify JSON with proper indentation
    /// </summary>
    private string PrettifyJson(string json, int indentSize)
    {
        try
        {
            using var jsonDoc = JsonDocument.Parse(json);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                // Note: Can't control indent size directly in System.Text.Json
                // It always uses 2 spaces
            };

            return JsonSerializer.Serialize(jsonDoc.RootElement, options);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to prettify JSON, returning original");
            return json;
        }
    }

    /// <summary>
    /// Validate template syntax
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateTemplate(string template)
    {
        try
        {
            var scribanTemplate = Template.Parse(template);

            if (scribanTemplate.HasErrors)
            {
                var errors = string.Join("; ", scribanTemplate.Messages.Select(m => m.Message));
                return (false, errors);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

/// <summary>
/// Helper functions available in Scriban templates
/// Helps prevent empty structures in output
/// </summary>
public class TemplateHelpers : ScriptObject
{
    /// <summary>
    /// Convert any object to JSON string
    /// Usage in template: {{ row | to_json }}
    /// </summary>
    public static string ToJson(object obj)
    {
        if (obj == null) return "{}";
        return JsonSerializer.Serialize(obj);
    }

    public static object ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, object>();

        using var doc = JsonDocument.Parse(json);
        return ConvertJsonElement(doc.RootElement);
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                                        .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                                        .Select(ConvertJsonElement)
                                        .ToList(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// Check if a value is not null, empty, or whitespace
    /// Usage: {{ if has_value row.ColumnName }}...{{ end }}
    /// </summary>
    public static bool HasValue(object? value)
    {
        if (value == null) return false;
        if (value is string str) return !string.IsNullOrWhiteSpace(str);
        if (value is DBNull) return false;
        return true;
    }

    /// <summary>
    /// Get a value or return a default if null/empty
    /// Usage: {{ row.ColumnName | default "N/A" }}
    /// </summary>
    public static object Default(object? value, object defaultValue)
    {
        return HasValue(value) ? value! : defaultValue;
    }

    /// <summary>
    /// Check if any row in data has a non-empty value for a specific key
    /// Usage: {{ if any_has_value data "ColumnName" }}...{{ end }}
    /// </summary>
    public static bool AnyHasValue(List<Dictionary<string, object>> data, string key)
    {
        return data.Any(row => row.ContainsKey(key) && HasValue(row[key]));
    }

    /// <summary>
    /// Filter rows to only those where a specific key has a value
    /// Usage: {{ filtered = data | filter_by_value "ColumnName" }}
    /// </summary>
    public static List<Dictionary<string, object>> FilterByValue(List<Dictionary<string, object>> data, string key)
    {
        return data.Where(row => row.ContainsKey(key) && HasValue(row[key])).ToList();
    }

    /// <summary>
    /// Check if a dictionary has any non-empty values
    /// Usage: {{ if row_has_any_value row }}...{{ end }}
    /// </summary>
    public static bool RowHasAnyValue(Dictionary<string, object> row)
    {
        return row.Values.Any(HasValue);
    }

    /// <summary>
    /// Get only non-empty key-value pairs from a dictionary
    /// Usage: {% for pair in row | non_empty_pairs %}...{% endfor %}
    /// </summary>
    public static Dictionary<string, object> NonEmptyPairs(Dictionary<string, object> row)
    {
        return row.Where(kvp => HasValue(kvp.Value))
                  .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Safe string conversion that handles nulls
    /// Usage: {{ row.ColumnName | safe_string }}
    /// </summary>
    public static string SafeString(object? value)
    {
        if (value == null || value is DBNull) return string.Empty;
        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// XML escape a value
    /// Usage: {{ row.ColumnName | xml_escape }}
    /// </summary>
    public static string XmlEscape(object? value)
    {
        if (!HasValue(value)) return string.Empty;
        var str = value?.ToString() ?? string.Empty;
        return System.Security.SecurityElement.Escape(str);
    }

    /// <summary>
    /// HTML escape a value
    /// Usage: {{ row.ColumnName | html_escape }}
    /// </summary>
    public static string HtmlEscape(object? value)
    {
        if (!HasValue(value)) return string.Empty;
        var str = value?.ToString() ?? string.Empty;
        return System.Net.WebUtility.HtmlEncode(str);
    }

    /// <summary>
    /// CSV escape a value (handles quotes and commas)
    /// Usage: {{ row.ColumnName | csv_escape }}
    /// </summary>
    public static string CsvEscape(object? value)
    {
        if (!HasValue(value)) return string.Empty;
        var str = value?.ToString() ?? string.Empty;
        
        // If contains comma, quote, or newline, wrap in quotes and escape inner quotes
        if (str.Contains(',') || str.Contains('"') || str.Contains('\n') || str.Contains('\r'))
        {
            return $"\"{str.Replace("\"", "\"\"")}\"";
        }
        
        return str;
    }

    /// <summary>
    /// Check if data array is empty
    /// Usage: {% if is_empty data %}No data available{% endif %}
    /// </summary>
    public static bool IsEmpty(List<Dictionary<string, object>>? data)
    {
        return data == null || data.Count == 0;
    }

    /// <summary>
    /// Check if data array has items
    /// Usage: {% if has_data data %}...{% endif %}
    /// </summary>
    public static bool HasData(List<Dictionary<string, object>>? data)
    {
        return !IsEmpty(data);
    }
}
