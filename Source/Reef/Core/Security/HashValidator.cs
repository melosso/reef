using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Reef.Core.Security;

/// <summary>
/// SHA256 hash validation service for detecting entity tampering
/// </summary>
public class HashValidator
{
    /// <summary>
    /// Compute SHA256 hash for an object
    /// </summary>
    public string ComputeHash(object entity)
    {
        var json = JsonSerializer.Serialize(entity, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null
        });

        return ComputeHash(json);
    }

    /// <summary>
    /// Compute SHA256 hash for a string
    /// </summary>
    public string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Validate hash matches computed hash
    /// </summary>
    public bool ValidateHash(object entity, string expectedHash)
    {
        var computedHash = ComputeHash(entity);
        return computedHash == expectedHash;
    }

    /// <summary>
    /// Validate hash matches string content
    /// </summary>
    public bool ValidateHash(string input, string expectedHash)
    {
        var computedHash = ComputeHash(input);
        return computedHash == expectedHash;
    }
}