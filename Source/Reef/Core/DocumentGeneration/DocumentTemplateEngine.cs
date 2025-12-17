using Reef.Core.TemplateEngines;
using Serilog;
using System.Text.RegularExpressions;

namespace Reef.Core.DocumentGeneration;

/// <summary>
/// Document template engine that orchestrates hybrid Scriban + layout document generation
/// Parses template metadata, renders data bindings with Scriban, and routes to appropriate generator
/// </summary>
public class DocumentTemplateEngine
{
    private readonly ITemplateEngine _scribanEngine;
    private readonly IDocumentGeneratorFactory _generatorFactory;
    private readonly string _exportsPath;

    // Regex patterns for parsing template directives
    private static readonly Regex DirectiveRegex = new(@"\{\{!\s*(\w+):\s*([^\}]+)\}\}", RegexOptions.Compiled);
    private static readonly Regex SectionRegex = new(@"\{\{#\s*(\w+)\s*\}\}(.*?)\{\{/\s*\1\s*\}\}", RegexOptions.Compiled | RegexOptions.Singleline);

    public DocumentTemplateEngine(
        ITemplateEngine scribanEngine,
        IDocumentGeneratorFactory generatorFactory,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _scribanEngine = scribanEngine;
        _generatorFactory = generatorFactory;
        _exportsPath = configuration["ExportsPath"] ?? "exports";

        // Ensure exports directory exists
        Directory.CreateDirectory(_exportsPath);
    }

    /// <summary>
    /// Transform data into a document (PDF, DOCX, ODT)
    /// </summary>
    /// <param name="data">Query results as list of dictionaries</param>
    /// <param name="template">Hybrid template string (directives + Scriban sections)</param>
    /// <param name="context">Additional context variables (optional)</param>
    /// <param name="filenameTemplate">Optional filename template with placeholders like {timestamp}, {profile}, etc.</param>
    /// <returns>Full path to generated document file</returns>
    public async Task<string> TransformAsync(
        List<Dictionary<string, object>> data,
        string template,
        Dictionary<string, object>? context = null,
        string? filenameTemplate = null,
        bool useTemporaryDirectory = false)
    {
        try
        {
            // Step 1: Parse template metadata and sections
            var layout = ParseTemplate(template);

            Log.Debug("Parsed document template: Format={Format}, PageSize={PageSize}, Sections={SectionCount}",
                layout.OutputFormat, layout.PageSetup.Size, layout.Sections.Count);

            // Step 2: Render each section with Scriban data binding
            foreach (var section in layout.Sections)
            {
                section.RenderedContent = await _scribanEngine.TransformAsync(data, section.RenderedContent, context);
            }

            Log.Debug("Rendered {SectionCount} sections with Scriban data binding", layout.Sections.Count);

            // Step 3: Generate output file path
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = GenerateFilename(filenameTemplate, layout.OutputFormat, timestamp, context);
            
            // Use temp directory for email attachments, exports directory for regular exports
            var outputDirectory = useTemporaryDirectory 
                ? Path.Combine(Path.GetTempPath(), "reef-temp-docs")
                : _exportsPath;
            
            if (useTemporaryDirectory && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            
            var outputPath = Path.Combine(outputDirectory, fileName);

            // Step 4: Get appropriate generator and generate document
            var generator = _generatorFactory.GetGenerator(layout.OutputFormat);
            var (success, fileSize, errorMessage) = await generator.GenerateAsync(data, layout, outputPath);

            if (!success)
            {
                throw new InvalidOperationException($"Document generation failed: {errorMessage}");
            }

            Log.Debug("Document generated successfully: {FileName}, Format: {Format}, Size: {FileSize} bytes",
                fileName, layout.OutputFormat, fileSize);

            return outputPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Document template transformation failed");
            throw;
        }
    }

    /// <summary>
    /// Generate filename from template with placeholder replacement
    /// </summary>
    private string GenerateFilename(string? template, string outputFormat, string timestamp, Dictionary<string, object>? context)
    {
        var extension = outputFormat.ToLowerInvariant();
        
        // Default filename if no template provided
        if (string.IsNullOrWhiteSpace(template))
        {
            return $"document_{timestamp}.{extension}";
        }

        var now = DateTime.UtcNow;
        var profileName = context?.GetValueOrDefault("profile_name")?.ToString() ?? "export";
        
        return template
            .Replace("{profile}", SanitizeFilename(profileName), StringComparison.OrdinalIgnoreCase)
            .Replace("{timestamp}", timestamp, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", now.ToString("yyyyMMdd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{guid}", Guid.NewGuid().ToString("N"), StringComparison.OrdinalIgnoreCase)
            .Replace("{format}", extension, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sanitize filename by removing invalid characters
    /// </summary>
    private static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", filename.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
    }

    /// <summary>
    /// Validate document template syntax
    /// </summary>
    /// <param name="template">Template string to validate</param>
    /// <returns>Validation result and error message if invalid</returns>
    public (bool IsValid, string? ErrorMessage) ValidateTemplate(string template)
    {
        try
        {
            // Validate directive syntax
            var layout = ParseTemplate(template);

            // Validate output format
            if (!new[] { "PDF", "DOCX", "ODT" }.Contains(layout.OutputFormat.ToUpperInvariant()))
            {
                return (false, $"Invalid output format: {layout.OutputFormat}. Supported: PDF, DOCX, ODT");
            }

            // Validate page size
            if (!new[] { "A4", "LETTER", "LEGAL" }.Contains(layout.PageSetup.Size.ToUpperInvariant()))
            {
                return (false, $"Invalid page size: {layout.PageSetup.Size}. Supported: A4, Letter, Legal");
            }

            // Validate orientation
            if (!new[] { "PORTRAIT", "LANDSCAPE" }.Contains(layout.PageSetup.Orientation.ToUpperInvariant()))
            {
                return (false, $"Invalid orientation: {layout.PageSetup.Orientation}. Supported: Portrait, Landscape");
            }

            // Validate at least one section exists
            if (layout.Sections.Count == 0)
            {
                return (false, "Template must contain at least one section ({{# content }} ... {{/ content }})");
            }

            // Validate Scriban syntax in each section
            foreach (var section in layout.Sections)
            {
                var (isValid, errorMessage) = _scribanEngine.ValidateTemplate(section.RenderedContent);
                if (!isValid)
                {
                    return (false, $"Scriban syntax error in {section.Name} section: {errorMessage}");
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Template parsing error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse template string into DocumentLayout
    /// Extracts directives and sections
    /// </summary>
    private DocumentLayout ParseTemplate(string template)
    {
        var layout = new DocumentLayout();

        // Parse directives ({{! key: value }})
        var directiveMatches = DirectiveRegex.Matches(template);
        foreach (Match match in directiveMatches)
        {
            var key = match.Groups[1].Value.Trim();
            var value = match.Groups[2].Value.Trim();

            switch (key.ToLowerInvariant())
            {
                case "format":
                    layout.OutputFormat = value.ToUpperInvariant();
                    break;
                case "pagesize":
                    layout.PageSetup.Size = value;
                    break;
                case "orientation":
                    layout.PageSetup.Orientation = value;
                    break;
                case "margintop":
                    layout.PageSetup.Margins.Top = float.Parse(value);
                    break;
                case "marginbottom":
                    layout.PageSetup.Margins.Bottom = float.Parse(value);
                    break;
                case "marginleft":
                    layout.PageSetup.Margins.Left = float.Parse(value);
                    break;
                case "marginright":
                    layout.PageSetup.Margins.Right = float.Parse(value);
                    break;
            }
        }

        // Parse sections ({{# name }} ... {{/ name }})
        var sectionMatches = SectionRegex.Matches(template);
        foreach (Match match in sectionMatches)
        {
            var sectionName = match.Groups[1].Value.Trim();
            var sectionContent = match.Groups[2].Value.Trim();

            layout.Sections.Add(new DocumentSection
            {
                Name = sectionName,
                RenderedContent = sectionContent
            });
        }

        return layout;
    }
}
