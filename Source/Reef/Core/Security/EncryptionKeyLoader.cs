using Serilog;
using Reef.Core.Services;
using Reef.Helpers;

namespace Reef.Core.Security;

/// <summary>
/// Resolves ENCRYPTION_KEY or REEF_ENCRYPTION_KEY from the machine environment, the process environment, the .env file or the fallback key.
/// </summary>
public static class EncryptionKeyLoader
{
    internal const string FallbackKey = "$REEF2.0_FallbackEncryptionKey_ChangeInProduction_MinLength32Chars#";

    private static readonly Lazy<string> Loaded = new(Load);

    public static string Key => Loaded.Value;

    // The fallback key is public. Secrets it protects are readable by anyone with the database and .core folder.
    public static void EnsureConfigured(bool isDevelopment, string resolvedKey)
    {
        if (!isDevelopment && resolvedKey == FallbackKey)
            throw new InvalidOperationException("ENCRYPTION_KEY is not set. Set it before starting Reef outside Development.");
    }

    private static string Load()
    {
        var envKey = EnvAliases.GetDirect("ENCRYPTION_KEY", name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine));
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            Log.Debug("Using encryption key from Windows system environment variable");
            return envKey;
        }

        envKey = EnvAliases.GetDirect("ENCRYPTION_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            Log.Debug("Using encryption key from process environment variable");
            return envKey;
        }

        var projectRoot = EncryptionService.FindProjectRoot(AppContext.BaseDirectory);
        var envFilePath = Path.Combine(projectRoot, ".env");

        if (File.Exists(envFilePath))
        {
            try
            {
                var values = File.ReadAllLines(envFilePath)
                    .Select(line => line.Trim())
                    .Where(line => !line.StartsWith('#') && line.Contains('='))
                    .Select(line => line.Split('=', 2))
                    .GroupBy(parts => parts[0].Trim())
                    .ToDictionary(g => g.Key, g => g.Last()[1].Trim().Trim('"', '\''));
                var key = EnvAliases.GetDirect("ENCRYPTION_KEY", name => values.GetValueOrDefault(name) is { Length: > 0 } v ? v : null);
                if (key is not null)
                {
                    Log.Debug("Using encryption key from .env file");
                    return key;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read environment file at {Path}", envFilePath);
            }
        }

        Log.Warning("ENCRYPTION_KEY is not set in the environment or .env file. The fallback key is in use.");
        Log.Warning("Set ENCRYPTION_KEY for production use.");

        return FallbackKey;
    }
}
