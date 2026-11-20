using System.Collections.Concurrent;
using System.Text.Json;

namespace Reef.Core.Destinations;

// Fetches smtp access tokens with the client credentials grant and caches them until a minute before expiry.
public static class OAuthTokenProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> Cache = new();

    public static async Task<string?> GetTokenAsync(EmailDestinationConfiguration config, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(config.OauthTokenEndpoint) || string.IsNullOrEmpty(config.OauthClientId))
            return config.OauthToken;

        var key = $"{config.OauthTokenEndpoint}|{config.OauthClientId}|{config.OauthScope}";
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTime.UtcNow.AddMinutes(1))
            return cached.Token;

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = config.OauthClientId,
            ["client_secret"] = config.OauthClientSecret ?? ""
        };
        if (!string.IsNullOrEmpty(config.OauthScope)) form["scope"] = config.OauthScope;

        using var response = await Http.PostAsync(config.OauthTokenEndpoint, new FormUrlEncodedContent(form), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OAuth2 token request failed with {(int)response.StatusCode}: {body}");

        using var json = JsonDocument.Parse(body);
        var token = json.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("OAuth2 token response has no access_token");
        var lifetime = json.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : 3600;
        Cache[key] = (token, DateTime.UtcNow.AddSeconds(lifetime));
        return token;
    }
}
