namespace Reef.Helpers;

/// <summary>
/// Generates unique short codes for profiles.
/// Format: 6 uppercase alphanumeric chars (base-32 style, e.g. "A3F1K9")
/// Used for unambiguous identification in logs and audit entries.
/// </summary>
public static class ProfileCodeGenerator
{
    // Base-32 style: uppercase letters + digits, excluding ambiguous chars (0, O, I, L)
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private static readonly Random _rng = Random.Shared;

    /// <summary>
    /// Generate a random 6-character code.
    /// </summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < 6; i++)
            chars[i] = Alphabet[_rng.Next(Alphabet.Length)];
        return new string(chars);
    }
}
