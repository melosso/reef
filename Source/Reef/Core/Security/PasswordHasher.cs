namespace Reef.Core.Security;

/// <summary>
/// BCrypt password hashing service for secure user authentication
/// Uses BCrypt.Net library for industry-standard password hashing
/// </summary>
public class PasswordHasher
{
    /// <summary>
    /// Hash a password using BCrypt with work factor 12
    /// </summary>
    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }

    /// <summary>
    /// Verify a password against a BCrypt hash
    /// </summary>
    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }
}