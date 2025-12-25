using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Reef.Core.Security;

/// <summary>
/// JWT token generation and validation service
/// </summary>
public class JwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;
    private static string? _currentStartupToken;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        _secretKey = configuration["Reef:Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        _issuer = configuration["Reef:Jwt:Issuer"] ?? "Reef";
        _audience = configuration["Reef:Jwt:Audience"] ?? "Reef";
        _expirationMinutes = configuration.GetValue<int>("Reef:Jwt:ExpirationMinutes", 60);
    }

    /// <summary>
    /// Set the current startup token (called once on application startup)
    /// </summary>
    public static void SetCurrentStartupToken(string startupToken)
    {
        _currentStartupToken = startupToken;
    }

    /// <summary>
    /// Get the current startup token
    /// </summary>
    public static string? GetCurrentStartupToken()
    {
        return _currentStartupToken;
    }

    /// <summary>
    /// Generate JWT token for a user
    /// </summary>
    public string GenerateToken(string username, string role, int userId, int? customExpirationMinutes = null)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_secretKey);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()), // Standard claim for user ID
            new Claim("userId", userId.ToString()), // Additional custom claim for backwards compatibility
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), // JWT standard subject claim
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Include the startup token to invalidate tokens on service restart or password change
        if (!string.IsNullOrEmpty(_currentStartupToken))
        {
            claims.Add(new Claim("startup_token", _currentStartupToken));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(customExpirationMinutes ?? _expirationMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    /// <summary>
    /// Validate JWT token and extract claims
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = ClaimTypes.Name
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

            // Validate that the startup token in the JWT matches the current startup token
            // This ensures tokens are invalidated when service restarts or password is changed
            var startupTokenClaim = principal.FindFirst("startup_token")?.Value;

            // If we have a current startup token set, it must match the token's startup token
            if (!string.IsNullOrEmpty(_currentStartupToken))
            {
                // Token must have the startup token claim and it must match
                if (string.IsNullOrEmpty(startupTokenClaim) || startupTokenClaim != _currentStartupToken)
                {
                    // Token is from a previous service session or doesn't have startup token - invalid
                    return null;
                }
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract username from token
    /// </summary>
    public string? GetUsernameFromToken(string token)
    {
        var principal = ValidateToken(token);
        return principal?.Identity?.Name;
    }

    /// <summary>
    /// Extract role from token
    /// </summary>
    public string? GetRoleFromToken(string token)
    {
        var principal = ValidateToken(token);
        return principal?.FindFirst(ClaimTypes.Role)?.Value;
    }
}