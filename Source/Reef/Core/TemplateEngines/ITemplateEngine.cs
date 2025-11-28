namespace Reef.Core.TemplateEngines;

/// <summary>
/// Interface for template engines that transform data using custom templates
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    /// Transform data using a template
    /// </summary>
    /// <param name="data">Query results as list of dictionaries</param>
    /// <param name="template">Template string (syntax depends on engine)</param>
    /// <param name="context">Additional context variables (optional)</param>
    /// <returns>Transformed output string</returns>
    Task<string> TransformAsync(
        List<Dictionary<string, object>> data, 
        string template,
        Dictionary<string, object>? context = null);

    /// <summary>
    /// Validate template syntax without executing
    /// </summary>
    /// <param name="template">Template to validate</param>
    /// <returns>Tuple with validation result and error message if invalid</returns>
    (bool IsValid, string? ErrorMessage) ValidateTemplate(string template);

    /// <summary>
    /// Get the name of this template engine
    /// </summary>
    string EngineName { get; }
}
