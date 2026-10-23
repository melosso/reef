using System.Security.Cryptography;
using Serilog;

namespace Reef.Core.Security;

/// <summary>
/// Replaces a missing or published JWT signing secret with a random secret stored in the .core folder.
/// </summary>
public static class JwtSecretResolver
{
    public const string ShippedDefault = "ThisIsAVerySecretKeyThatIsAtLeast32CharactersLongForJwtTokenGeneration123!";
    private const string ShippedDevelopmentDefault = "your-secret-key-here-minimum-32-characters-change-in-production";
    private const string FileName = "jwt-secret";

    public static string Resolve(string? configured, string coreFolder)
    {
        if (!string.IsNullOrWhiteSpace(configured) && configured != ShippedDefault && configured != ShippedDevelopmentDefault)
            return configured;

        var path = Path.Combine(coreFolder, FileName);
        if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } stored)
            return stored;

        Directory.CreateDirectory(coreFolder);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)).TrimEnd('=');
        File.WriteAllText(path, secret);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Log.Warning("JWT_SECRET_KEY is not set or uses the published default. Reef generated a signing secret in {Path}.", path);
        return secret;
    }
}
