using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Security;

namespace Reef.Core.Security;

/// <summary>
/// API key validation service
/// </summary>
public class ApiKeyValidator
{
    private readonly string _connectionString;
    private readonly PasswordHasher _passwordHasher;

    public ApiKeyValidator(DatabaseConfig config, PasswordHasher passwordHasher)
    {
        _connectionString = config.ConnectionString;
        _passwordHasher = passwordHasher;
    }

    /// <summary>
    /// Validate API key and return permissions if valid
    /// </summary>
    public async Task<(bool IsValid, string? Permissions)> ValidateApiKeyAsync(string apiKey)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT KeyHash, Permissions, ExpiresAt, IsActive
            FROM ApiKeys
            WHERE IsActive = 1
        ";

        var apiKeys = await connection.QueryAsync<dynamic>(sql);

        foreach (var key in apiKeys)
        {
            // Check if key matches hash
            if (_passwordHasher.VerifyPassword(apiKey, (string)key.KeyHash))
            {
                // Check expiration
                if (key.ExpiresAt != null)
                {
                    if (DateTime.Parse((string)key.ExpiresAt) < DateTime.UtcNow)
                    {
                        return (false, null); // Expired
                    }
                }

                // Update last used timestamp
                const string updateSql = @"
                    UPDATE ApiKeys
                    SET LastUsedAt = datetime('now')
                    WHERE KeyHash = @KeyHash
                ";
                await connection.ExecuteAsync(updateSql, new { KeyHash = (string)key.KeyHash });

                return (true, (string)key.Permissions);
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Generate a new API key
    /// </summary>
    public string GenerateApiKey()
    {
        // Generate a 32-byte random key
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
    }

    /// <summary>
    /// Create a new API key
    /// </summary>
    public async Task<string> CreateApiKeyAsync(string name, string permissions, DateTime? expiresAt = null)
    {
        var apiKey = GenerateApiKey();
        var keyHash = _passwordHasher.HashPassword(apiKey);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            INSERT INTO ApiKeys (Name, KeyHash, Permissions, ExpiresAt, IsActive)
            VALUES (@Name, @KeyHash, @Permissions, @ExpiresAt, 1)
        ";

        await connection.ExecuteAsync(sql, new
        {
            Name = name,
            KeyHash = keyHash,
            Permissions = permissions,
            ExpiresAt = expiresAt?.ToString("o")
        });

        return apiKey;
    }
}