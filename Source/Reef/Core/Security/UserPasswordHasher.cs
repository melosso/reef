using System.Security.Cryptography;
using System.Text;

namespace Reef.Core.Security;

/// <summary>
/// Hashes user passwords with BCrypt and a pepper derived from ENCRYPTION_KEY.
/// </summary>
public sealed class UserPasswordHasher(string encryptionKey)
{
    private readonly byte[] _pepper = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(encryptionKey), 32,
        info: "reef-user-password-pepper"u8.ToArray());

    public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(Pepper(password), workFactor: 12);

    public bool VerifyPassword(string password, string hash) => VerifyPassword(password, hash, out _);

    // Hashes without the pepper verify and report a required rehash.
    public bool VerifyPassword(string password, string hash, out bool needsRehash)
    {
        needsRehash = false;
        if (Verify(Pepper(password), hash)) return true;
        needsRehash = Verify(password, hash);
        return needsRehash;
    }

    // HMAC output keeps every input below the BCrypt 72 byte limit.
    private string Pepper(string password) => Convert.ToBase64String(HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(password)));

    private static bool Verify(string text, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(text, hash);
        }
        catch
        {
            return false;
        }
    }
}
