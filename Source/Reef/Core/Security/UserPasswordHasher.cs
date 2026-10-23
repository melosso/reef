using System.Security.Cryptography;
using System.Text;

namespace Reef.Core.Security;

/// <summary>
/// BCrypt hashing for user passwords with a pepper derived from REEF_ENCRYPTION_KEY
/// </summary>
public sealed class UserPasswordHasher(string encryptionKey)
{
    private readonly byte[] _pepper = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(encryptionKey), 32,
        info: "reef-user-password-pepper"u8.ToArray());

    public string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(Pepper(password), workFactor: 12);

    public bool VerifyPassword(string password, string hash) => VerifyPassword(password, hash, out _);

    // hashes stored before the pepper still verify and report that they need a rehash
    public bool VerifyPassword(string password, string hash, out bool needsRehash)
    {
        needsRehash = false;
        if (Verify(Pepper(password), hash)) return true;
        needsRehash = Verify(password, hash);
        return needsRehash;
    }

    // hmac output keeps every input under the bcrypt 72 byte limit
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
