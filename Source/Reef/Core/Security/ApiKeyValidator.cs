using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;

namespace Reef.Core.Security;

public sealed class ApiKeyValidator(DatabaseConfig config, TimeProvider time)
{
    private const string KeyStart = "reef_ak_";
    private static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(1);

    public static (string Key, string KeyPrefix, string KeyHash) Generate()
    {
        var prefix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var key = $"{KeyStart}{prefix}_{secret}";
        return (key, prefix, Hash(key));
    }

    public async Task<ApiKey?> ValidateAsync(string presented)
    {
        var end = presented.StartsWith(KeyStart, StringComparison.Ordinal) ? presented.IndexOf('_', KeyStart.Length) : -1;
        if (end < 0)
            return null;

        await using var connection = new SqliteConnection(config.ConnectionString);
        var key = await connection.QuerySingleOrDefaultAsync<ApiKey>(
            "SELECT * FROM ApiKeys WHERE KeyPrefix = @Prefix AND IsActive = 1",
            new { Prefix = presented[KeyStart.Length..end] });

        var now = time.GetUtcNow().UtcDateTime;
        if (key is null
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(key.KeyHash), Encoding.ASCII.GetBytes(Hash(presented)))
            || key.ExpiresAt < now)
            return null;

        await connection.ExecuteAsync(
            "UPDATE ApiKeys SET LastUsedAt = @Now WHERE Id = @Id AND (LastUsedAt IS NULL OR LastUsedAt < @Cutoff)",
            new { Now = now, key.Id, Cutoff = now - LastUsedThrottle });
        return key;
    }

    public static bool CanWrite(string permissions)
    {
        try
        {
            using var json = JsonDocument.Parse(permissions);
            return json.RootElement.TryGetProperty("write", out var write) && write.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
