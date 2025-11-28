using System.Data;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Xsl;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Security;
using Reef.Core.TemplateEngines;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing and applying query transformation templates
/// 
/// ARCHITECTURE OVERVIEW:
/// =====================
/// This service supports TWO distinct transformation paths for production-ready data exports:
/// 
/// 1. SQL SERVER NATIVE TRANSFORMATIONS (Types 4-9: ForXml*, ForJson*)
///    - Uses SQL Server's native FOR XML/JSON capabilities for maximum performance
///    - Configuration via strongly-typed Options (ForXmlOptions, ForJsonOptions)
///    - Template string field is for reference only - actual config from Profile.TransformationOptionsJson
///    - Executes directly on SQL Server before data leaves the database
///    - Ideal for: Standard JSON/XML outputs, high-volume exports, API integrations
///    
///    Priority chain for options:
///    a) Runtime Options (from ApplyTransformationAsync request.Options) - highest priority
///    b) Profile TransformationOptionsJson (parsed from Profile at execution time)
///    c) Template string parsing (fallback for backward compatibility)
///    d) Defaults (no configuration)
/// 
/// 2. CUSTOM SCRIBAN TEMPLATES (Types 1-3: XmlTemplate, JsonTemplate, XsltTransform)
///    - Uses Scriban template engine for complex custom formats
///    - Template string field contains actual transformation logic
///    - Ideal for: EDI files, SOAP XML, fixed-width formats, legacy system integrations
/// 
/// USAGE IN PROFILES:
/// ==================
/// - Profile.TemplateId: References QueryTemplates.Id
/// - Profile.TransformationOptionsJson: Stores serialized ForXmlOptions/ForJsonOptions for native types
/// - ExecutionService routes to correct path based on template type
/// 
/// BACKWARD COMPATIBILITY:
/// =======================
/// - Existing templates with populated Template field still work
/// - Parser extracts options from template strings as fallback
/// - New profiles should use TransformationOptionsJson for native types
/// 
/// MAINTAINABILITY:
/// ================
/// - Clear separation between native (performance) and custom (flexibility)
/// - Strongly typed options prevent runtime errors
/// - Fallback parsing ensures no breaking changes
/// - Comprehensive logging for production debugging
/// </summary>
public class QueryTemplateService
{
    private readonly string _connectionString;
    private readonly HashValidator _hashValidator;
    private readonly ScribanTemplateEngine _scribanEngine;

    public QueryTemplateService(string connectionString, HashValidator hashValidator, ScribanTemplateEngine scribanEngine)
    {
        _connectionString = connectionString;
        _hashValidator = hashValidator;
        _scribanEngine = scribanEngine;
    }

    public async Task<IEnumerable<QueryTemplate>> GetAllAsync(bool activeOnly = false)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sql = activeOnly 
            ? "SELECT * FROM QueryTemplates WHERE IsActive = 1 ORDER BY Name"
            : "SELECT * FROM QueryTemplates ORDER BY Name";
        
        return await conn.QueryAsync<QueryTemplate>(sql);
    }

    public async Task<QueryTemplate?> GetByIdAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        return await conn.QuerySingleOrDefaultAsync<QueryTemplate>(
            "SELECT * FROM QueryTemplates WHERE Id = @Id", new { Id = id });
    }

    public async Task<QueryTemplate> CreateAsync(QueryTemplate template)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        template.Hash =  Reef.Helpers.HashHelper.ComputeDestinationHash(
            template.Name, 
            template.Type.ToString(), 
            template.Template);
        
        template.CreatedAt = DateTime.UtcNow;
        
        var sql = @"
            INSERT INTO QueryTemplates (Name, Description, Type, Template, OutputFormat, IsActive, Tags, CreatedAt, Hash)
            VALUES (@Name, @Description, @Type, @Template, @OutputFormat, @IsActive, @Tags, @CreatedAt, @Hash);
            SELECT last_insert_rowid();";
        
        template.Id = await conn.ExecuteScalarAsync<int>(sql, template);
        return template;
    }

    public async Task<bool> UpdateAsync(QueryTemplate template)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        template.Hash =  Reef.Helpers.HashHelper.ComputeDestinationHash(
            template.Name, 
            template.Type.ToString(), 
            template.Template);
        
        template.ModifiedAt = DateTime.UtcNow;
        
        var sql = @"
            UPDATE QueryTemplates 
            SET Name = @Name, Description = @Description, Type = @Type,
                Template = @Template, OutputFormat = @OutputFormat, IsActive = @IsActive,
                Tags = @Tags, ModifiedAt = @ModifiedAt, Hash = @Hash
            WHERE Id = @Id";
        
        var rows = await conn.ExecuteAsync(sql, template);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        // Check if template is in use
        var inUse = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ProfileTransformations WHERE TemplateId = @Id", new { Id = id });
        
        if (inUse > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete template. It is used by {inUse} profile transformation(s).");
        }
        
        var sql = "DELETE FROM QueryTemplates WHERE Id = @Id";
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        return rows > 0;
    }

    public async Task<TransformationResult> ApplyTransformationAsync(
        IDbConnection sourceConnection, 
        TransformationRequest request)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            string? template = request.InlineTemplate;
            QueryTemplateType type = request.Type;
            
            // Load template if using template ID
            if (request.TemplateId.HasValue)
            {
                var queryTemplate = await GetByIdAsync(request.TemplateId.Value);
                if (queryTemplate == null)
                {
                    throw new InvalidOperationException($"Template {request.TemplateId} not found");
                }
                template = queryTemplate.Template;
                type = queryTemplate.Type;
            }

            string output;
            string format;
            int rowCount;

            switch (type)
            {
                case QueryTemplateType.ForXmlRaw:
                case QueryTemplateType.ForXmlAuto:
                case QueryTemplateType.ForXmlPath:
                case QueryTemplateType.ForXmlExplicit:
                    (output, rowCount) = await ExecuteForXmlQuery(sourceConnection, request.Query, type, request.Options, template);
                    format = "XML";
                    break;

                case QueryTemplateType.ForJson:
                case QueryTemplateType.ForJsonPath:
                    (output, rowCount) = await ExecuteForJsonQuery(sourceConnection, request.Query, type, request.Options, template);
                    format = "JSON";
                    break;

                case QueryTemplateType.ScribanTemplate:
                    (output, rowCount) = await ApplyScribanTemplate(sourceConnection, request.Query, template!);
                    format = "TXT"; // Scriban can output any format, default to TXT
                    break;

                case QueryTemplateType.XmlTemplate:
                    (output, rowCount) = await ApplyXmlTemplate(sourceConnection, request.Query, template!);
                    format = "XML";
                    break;

                case QueryTemplateType.JsonTemplate:
                    (output, rowCount) = await ApplyJsonTemplate(sourceConnection, request.Query, template!);
                    format = "JSON";
                    break;

                case QueryTemplateType.XsltTransform:
                    (output, rowCount) = await ApplyXsltTransform(sourceConnection, request.Query, template!);
                    format = "XML";
                    break;

                default:
                    throw new NotImplementedException($"Transformation type {type} not implemented");
            }

            return new TransformationResult
            {
                Success = true,
                Output = output,
                Format = format,
                RowCount = rowCount,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// Execute FOR XML query with options or template string fallback support
    /// </summary>
    private async Task<(string output, int rowCount)> ExecuteForXmlQuery(
        IDbConnection connection, 
        string baseQuery, 
        QueryTemplateType type, 
        Dictionary<string, object>? options,
        string? templateString = null)
    {
        ForXmlOptions forXmlOptions;
        
        if (options != null)
        {
            // Priority 1: Runtime options provided
            forXmlOptions = JsonSerializer.Deserialize<ForXmlOptions>(JsonSerializer.Serialize(options))!;
        }
        else if (!string.IsNullOrWhiteSpace(templateString))
        {
            // Priority 2: Parse template string (fallback for backward compatibility)
            forXmlOptions = ParseForXmlTemplate(templateString, type);
        }
        else
        {
            // Priority 3: Use defaults
            forXmlOptions = new ForXmlOptions();
        }

        // Build FOR XML clause
        var forXmlClause = type switch
        {
            QueryTemplateType.ForXmlRaw => BuildForXmlRaw(forXmlOptions),
            QueryTemplateType.ForXmlAuto => BuildForXmlAuto(forXmlOptions),
            QueryTemplateType.ForXmlPath => BuildForXmlPath(forXmlOptions),
            QueryTemplateType.ForXmlExplicit => "FOR XML EXPLICIT",
            _ => "FOR XML RAW"
        };

        // Wrap query in subquery and cast to NVARCHAR(MAX) to avoid truncation
        // SQL Server FOR XML can return very large strings that exceed default limits
        var fullQuery = $"SELECT CAST(({baseQuery.TrimEnd().TrimEnd(';')} {forXmlClause}) AS NVARCHAR(MAX))";
        
        // Use ExecuteScalarAsync to get the single string result
        // SQL Server's FOR XML returns a single string value, not a result set row
        var xmlResult = await connection.ExecuteScalarAsync<string>(fullQuery);
        
        if (string.IsNullOrEmpty(xmlResult))
        {
            return ("<root></root>", 0);
        }

        // Try to count rows in XML (best effort - not critical for transformation)
        int rowCount = 0;
        try
        {
            var doc = XDocument.Parse(xmlResult);
            rowCount = doc.Descendants().Count(e => e.Name.LocalName == (forXmlOptions?.RowElement ?? "row"));
        }
        catch (XmlException)
        {
            // XML parsing failed - estimate row count by counting row elements
            var rowElementName = forXmlOptions?.RowElement ?? "row";
            rowCount = System.Text.RegularExpressions.Regex.Matches(xmlResult, $"<{rowElementName}[> ]").Count;
            if (rowCount == 0) rowCount = 1; // At least one row if we have data
        }

        return (xmlResult, rowCount);
    }

    private string BuildForXmlRaw(ForXmlOptions? options)
    {
        var parts = new List<string> { "FOR XML RAW" };
        
        if (!string.IsNullOrEmpty(options?.RowElement))
        {
            parts.Add($"('{options.RowElement}')");
        }
        
        if (options?.ElementsMode == true)
        {
            parts.Add("ELEMENTS");
        }
        
        if (!string.IsNullOrEmpty(options?.RootElement))
        {
            parts.Add($"ROOT('{options.RootElement}')");
        }
        
        return string.Join(", ", parts);
    }

    private string BuildForXmlAuto(ForXmlOptions? options)
    {
        var parts = new List<string> { "FOR XML AUTO" };
        
        if (options?.ElementsMode == true)
        {
            parts.Add("ELEMENTS");
        }
        
        if (!string.IsNullOrEmpty(options?.RootElement))
        {
            parts.Add($"ROOT('{options.RootElement}')");
        }
        
        return string.Join(", ", parts);
    }

    private string BuildForXmlPath(ForXmlOptions? options)
    {
        var parts = new List<string> { "FOR XML PATH" };
        
        if (!string.IsNullOrEmpty(options?.RowElement))
        {
            parts.Add($"('{options.RowElement}')");
        }
        
        if (!string.IsNullOrEmpty(options?.RootElement))
        {
            parts.Add($"ROOT('{options.RootElement}')");
        }
        
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Execute FOR JSON query with options or template string fallback support
    /// </summary>
    private async Task<(string output, int rowCount)> ExecuteForJsonQuery(
        IDbConnection connection, 
        string baseQuery, 
        QueryTemplateType type, 
        Dictionary<string, object>? options,
        string? templateString = null)
    {
        ForJsonOptions forJsonOptions;
        
        if (options != null)
        {
            // Priority 1: Runtime options provided
            forJsonOptions = JsonSerializer.Deserialize<ForJsonOptions>(JsonSerializer.Serialize(options))!;
        }
        else if (!string.IsNullOrWhiteSpace(templateString))
        {
            // Priority 2: Parse template string (fallback for backward compatibility)
            forJsonOptions = ParseForJsonTemplate(templateString);
        }
        else
        {
            // Priority 3: Use defaults
            forJsonOptions = new ForJsonOptions();
        }

        var forJsonClause = type == QueryTemplateType.ForJsonPath
            ? BuildForJsonPath(forJsonOptions)
            : BuildForJsonAuto(forJsonOptions);

        // Wrap query in subquery and cast to NVARCHAR(MAX) to avoid truncation
        // SQL Server FOR JSON can return very large strings that exceed default limits
        var fullQuery = $"SELECT CAST(({baseQuery.TrimEnd().TrimEnd(';')} {forJsonClause}) AS NVARCHAR(MAX))";
        
        // Use ExecuteScalarAsync to get the single string result
        // SQL Server's FOR JSON returns a single string value, not a result set row
        var jsonResult = await connection.ExecuteScalarAsync<string>(fullQuery);
        
        if (string.IsNullOrEmpty(jsonResult))
        {
            return ("[]", 0);
        }

        // Try to count rows in JSON array (best effort - not critical for transformation)
        int rowCount = 0;
        try
        {
            var jsonDoc = JsonDocument.Parse(jsonResult);
            rowCount = jsonDoc.RootElement.ValueKind == JsonValueKind.Array 
                ? jsonDoc.RootElement.GetArrayLength() 
                : 1;
            jsonDoc.Dispose();
        }
        catch (JsonException)
        {
            // JSON parsing failed (possibly due to SQL Server numeric formatting issues)
            // Estimate row count by counting opening braces (rough approximation)
            rowCount = jsonResult.Count(c => c == '{');
            if (rowCount == 0) rowCount = 1; // At least one row if we have data
        }

        return (jsonResult, rowCount);
    }

    private string BuildForJsonAuto(ForJsonOptions? options)
    {
        var parts = new List<string> { "FOR JSON AUTO" };
        
        if (options?.IncludeNullValues == true)
        {
            parts.Add("INCLUDE_NULL_VALUES");
        }
        
        if (options?.WithoutArrayWrapper == true)
        {
            parts.Add("WITHOUT_ARRAY_WRAPPER");
        }
        
        return string.Join(", ", parts);
    }

    private string BuildForJsonPath(ForJsonOptions? options)
    {
        var parts = new List<string> { "FOR JSON PATH" };
        
        if (!string.IsNullOrEmpty(options?.RootElement))
        {
            parts.Add($"ROOT('{options.RootElement}')");
        }
        
        if (options?.IncludeNullValues == true)
        {
            parts.Add("INCLUDE_NULL_VALUES");
        }
        
        if (options?.WithoutArrayWrapper == true)
        {
            parts.Add("WITHOUT_ARRAY_WRAPPER");
        }
        
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Parse a FOR JSON template string to extract options (fallback for backward compatibility)
    /// </summary>
    private ForJsonOptions ParseForJsonTemplate(string templateString)
    {
        var options = new ForJsonOptions();
        
        if (string.IsNullOrWhiteSpace(templateString))
            return options;
        
        // Extract ROOT element
        var rootMatch = System.Text.RegularExpressions.Regex.Match(templateString, @"ROOT\s*\(\s*'([^']+)'\s*\)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (rootMatch.Success)
        {
            options.RootElement = rootMatch.Groups[1].Value;
        }
        
        // Check for INCLUDE_NULL_VALUES
        options.IncludeNullValues = System.Text.RegularExpressions.Regex.IsMatch(templateString, 
            @"\bINCLUDE_NULL_VALUES\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Check for WITHOUT_ARRAY_WRAPPER
        options.WithoutArrayWrapper = System.Text.RegularExpressions.Regex.IsMatch(templateString, 
            @"\bWITHOUT_ARRAY_WRAPPER\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        return options;
    }

    /// <summary>
    /// Parse a FOR XML template string to extract options (fallback for backward compatibility)
    /// </summary>
    private ForXmlOptions ParseForXmlTemplate(string templateString, QueryTemplateType type)
    {
        var options = new ForXmlOptions
        {
            Type = type switch
            {
                QueryTemplateType.ForXmlRaw => "RAW",
                QueryTemplateType.ForXmlAuto => "AUTO",
                QueryTemplateType.ForXmlPath => "PATH",
                QueryTemplateType.ForXmlExplicit => "EXPLICIT",
                _ => "RAW"
            }
        };
        
        if (string.IsNullOrWhiteSpace(templateString))
            return options;
        
        // Extract ROOT element
        var rootMatch = System.Text.RegularExpressions.Regex.Match(templateString, @"ROOT\s*\(\s*'([^']+)'\s*\)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (rootMatch.Success)
        {
            options.RootElement = rootMatch.Groups[1].Value;
        }
        
        // Extract ROW element (for RAW and PATH)
        var rowMatch = System.Text.RegularExpressions.Regex.Match(templateString, @"(?:RAW|PATH)\s*\(\s*'([^']+)'\s*\)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (rowMatch.Success)
        {
            options.RowElement = rowMatch.Groups[1].Value;
        }
        
        // Check for ELEMENTS mode
        options.ElementsMode = System.Text.RegularExpressions.Regex.IsMatch(templateString, 
            @"\bELEMENTS\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        // Check for BINARY BASE64
        options.BinaryBase64 = System.Text.RegularExpressions.Regex.IsMatch(templateString, 
            @"\bBINARY\s+BASE64\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        return options;
    }

    private async Task<(string output, int rowCount)> ApplyXmlTemplate(
        IDbConnection connection, 
        string query, 
        string template)
    {
        // Execute query and get data
        var results = await connection.QueryAsync(query);
        var resultsList = results.ToList();
        
        // Build XML from template
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        
        // Parse template to extract structure
        // This is a simple implementation - could be enhanced with proper templating engine
        xml.AppendLine("<data>");
        
        foreach (var row in resultsList)
        {
            xml.AppendLine("  <row>");
            var dict = (IDictionary<string, object>)row;
            foreach (var kvp in dict)
            {
                xml.AppendLine($"    <{kvp.Key}>{System.Security.SecurityElement.Escape(kvp.Value?.ToString() ?? "")}</{kvp.Key}>");
            }
            xml.AppendLine("  </row>");
        }
        
        xml.AppendLine("</data>");
        
        return (xml.ToString(), resultsList.Count);
    }

    private async Task<(string output, int rowCount)> ApplyScribanTemplate(
        IDbConnection connection, 
        string query, 
        string template)
    {
        // Execute query and get data as List<Dictionary<string, object>>
        var results = await connection.QueryAsync(query);
        var resultsList = results
            .Select(row => (IDictionary<string, object>)row)
            .Select(dict => new Dictionary<string, object>(dict))
            .ToList();
        
        Log.Information("Applying custom template '{TemplateName}' (ScribanTemplate)", "Custom");
        
        // Use Scriban template engine
        var output = await _scribanEngine.TransformAsync(resultsList, template);
        
        return (output, resultsList.Count);
    }

    private async Task<(string output, int rowCount)> ApplyJsonTemplate(
        IDbConnection connection, 
        string query, 
        string template)
    {
        // Execute query and get data
        var results = await connection.QueryAsync(query);
        var resultsList = results.ToList();
        
        // Convert to JSON
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true 
        };
        
        var json = JsonSerializer.Serialize(resultsList, options);
        
        return (json, resultsList.Count);
    }

    private async Task<(string output, int rowCount)> ApplyXsltTransform(
        IDbConnection connection, 
        string query, 
        string xsltTemplate)
    {
        // Execute query as XML
        var (xmlData, rowCount) = await ExecuteForXmlQuery(
            connection, 
            query, 
            QueryTemplateType.ForXmlAuto, 
            null);
        
        // Apply XSLT transformation
        var xslt = new XslCompiledTransform();
        using var xsltReader = XmlReader.Create(new StringReader(xsltTemplate));
        xslt.Load(xsltReader);
        
        using var xmlReader = XmlReader.Create(new StringReader(xmlData));
        var output = new StringBuilder();
        using var writer = XmlWriter.Create(output);
        
        xslt.Transform(xmlReader, writer);
        
        return (output.ToString(), rowCount);
    }

    public async Task<IEnumerable<QueryTemplate>> GetByTypeAsync(QueryTemplateType type)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sql = "SELECT * FROM QueryTemplates WHERE Type = @Type AND IsActive = 1 ORDER BY Name";
        return await conn.QueryAsync<QueryTemplate>(sql, new { Type = type });
    }
}