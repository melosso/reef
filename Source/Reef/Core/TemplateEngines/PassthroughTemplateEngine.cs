using Serilog;

namespace Reef.Core.TemplateEngines;

/// <summary>
/// Pass-through template engine that returns data as-is
/// Used when no template transformation is needed
/// </summary>
public class PassthroughTemplateEngine : ITemplateEngine
{
    public string EngineName => "Passthrough";

    /// <summary>
    /// Returns empty string - actual formatting is done by IFormatter
    /// This engine is used when template is not specified
    /// </summary>
    public Task<string> TransformAsync(
        List<Dictionary<string, object>> data, 
        string template,
        Dictionary<string, object>? context = null)
    {
        Log.Debug("Using passthrough template engine - no transformation applied");
        return Task.FromResult(string.Empty);
    }

    /// <summary>
    /// Always valid since no actual template processing
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateTemplate(string template)
    {
        return (true, null);
    }
}
