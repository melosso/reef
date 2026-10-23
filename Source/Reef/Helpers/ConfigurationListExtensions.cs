namespace Reef.Helpers;

public static class ConfigurationListExtensions
{
    /// <summary>
    /// Reads a list setting from its indexed entries and from a comma separated value, without blanks or duplicates
    /// </summary>
    public static string[] GetList(this IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        var entries = section.GetChildren().Select(c => c.Value);
        var joined = section.Value?.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        return entries.Concat(joined).OfType<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
