using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace Reef.Helpers;

// Appends a content hash to /assets URLs. Browsers refetch an asset when its content changes.
public partial class AssetVersioner(IFileProvider files)
{
    private readonly ConcurrentDictionary<string, string?> _hashes = new(StringComparer.OrdinalIgnoreCase);

    public string Apply(string html) => AssetUrl().Replace(html, match =>
    {
        var hash = _hashes.GetOrAdd(match.Groups["path"].Value, Hash);
        return hash is null ? match.Value : $"{match.Groups["attr"].Value}=\"/assets/{match.Groups["path"].Value}?v={hash}\"";
    });

    private string? Hash(string path)
    {
        var file = files.GetFileInfo("assets/" + path);
        if (!file.Exists)
            return null;
        using var stream = file.CreateReadStream();
        return Convert.ToHexStringLower(SHA256.HashData(stream))[..10];
    }

    [GeneratedRegex("(?<attr>src|href)=\"/assets/(?<path>[^\"?#]+)\"")]
    private static partial Regex AssetUrl();
}
