using System.Collections;
using System.Text.RegularExpressions;
using Serilog;

namespace Reef.Helpers;

/// <summary>
/// Short environment variable names for every Reef setting, DATABASE_PATH and REEF_DATABASE_PATH set Reef:DatabasePath and Reef__ names still work
/// </summary>
public static partial class EnvAliases
{
    private const string Section = "Reef";
    private const string Prefix = "REEF_";
    private const string LegacyPrefix = "Reef__";

    private enum Rank { Legacy, Bare, Prefixed }

    /// <summary>
    /// Adds short name values on top of the current configuration, call right after the builder is created
    /// </summary>
    public static void ApplyConfigAliases(ConfigurationManager config)
    {
        var overrides = Resolve(config, ReadEnvironment());
        if (overrides.Count > 0)
            config.AddInMemoryCollection(overrides);
    }

    internal static Dictionary<string, string?> Resolve(IConfiguration current, IReadOnlyDictionary<string, string> env)
    {
        var settings = Settings(current);
        var chosen = new Dictionary<string, (Rank Rank, string Value)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in env.Where(e => !string.IsNullOrWhiteSpace(e.Value)).OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (Classify(name) is not var (normalized, rank) || !settings.TryGetValue(normalized, out var setting)) continue;
            // the environment provider already applies legacy scalars, only legacy lists still need splitting
            if (rank == Rank.Legacy && setting.Count == 0) continue;
            if (chosen.TryGetValue(setting.Key, out var existing) && existing.Rank >= rank) continue;
            chosen[setting.Key] = (rank, value);
        }

        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, (_, value)) in chosen)
        {
            var setting = settings.Values.First(s => s.Key == key);
            if (setting.Count == 0)
            {
                overrides[key] = value;
                continue;
            }

            var items = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < Math.Max(items.Length, setting.Count); i++)
                overrides[$"{key}:{i}"] = i < items.Length ? items[i] : null;
        }
        return overrides;
    }

    /// <summary>
    /// Reads a setting that lives outside the configuration, preferring REEF_NAME over NAME
    /// </summary>
    public static string? GetDirect(string name) => GetDirect(name, Environment.GetEnvironmentVariable);

    public static string? GetDirect(string name, Func<string, string?> readEnv) =>
        new[] { Prefix + name, name }.Select(readEnv).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>
    /// The short name that replaces a Reef__ name, Reef__Jwt__SecretKey becomes JWT_SECRET_KEY
    /// </summary>
    public static string ShortNameFor(string legacyName)
    {
        var segments = legacyName[LegacyPrefix.Length..].Split("__", StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 1 && segments[^1].All(char.IsAsciiDigit)) segments.RemoveAt(segments.Count - 1);
        return string.Join('_', segments.Select(s => WordBoundary().Replace(s, "_").ToUpperInvariant()));
    }

    /// <summary>
    /// Logs one warning per Reef__ setting still in use, call once Serilog is up
    /// </summary>
    public static void WarnOnLegacyUsage()
    {
        foreach (var group in ReadEnvironment().Keys.Where(IsLegacy).GroupBy(ShortNameFor))
            Log.Warning("Environment variable {OldName} is deprecated, use {NewName} instead", group.First(), group.Key);
    }

    // normalized name to setting, lists keep the number of entries the configuration holds
    private static Dictionary<string, (string Key, int Count)> Settings(IConfiguration current)
    {
        var lists = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var leaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, value) in current.GetSection(Section).AsEnumerable(makePathsRelative: true))
        {
            var cut = path.LastIndexOf(':');
            if (cut > 0 && path[(cut + 1)..].All(char.IsAsciiDigit))
                lists[path[..cut]] = lists.GetValueOrDefault(path[..cut]) + 1;
            else if (value is not null)
                leaves.Add(path);
        }

        var settings = new Dictionary<string, (string Key, int Count)>();
        var ambiguous = new HashSet<string>();
        foreach (var path in leaves.Where(l => !lists.ContainsKey(l)).Concat(lists.Keys))
        {
            var normalized = path.Replace(":", "").ToLowerInvariant();
            if (!settings.TryAdd(normalized, ($"{Section}:{path}", lists.GetValueOrDefault(path)))) ambiguous.Add(normalized);
        }
        foreach (var name in ambiguous) settings.Remove(name);
        return settings;
    }

    private static (string Normalized, Rank Rank)? Classify(string name)
    {
        if (IsLegacy(name)) return (Normalize(name[LegacyPrefix.Length..].Replace("__", "")), Rank.Legacy);
        if (name.Contains("__")) return null;
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? (Normalize(name[Prefix.Length..]), Rank.Prefixed)
            : (Normalize(name), Rank.Bare);
    }

    private static string Normalize(string name) => name.Replace("_", "").ToLowerInvariant();

    private static bool IsLegacy(string name) => name.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ReadEnvironment() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => e.Value as string ?? "", StringComparer.Ordinal);

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}
