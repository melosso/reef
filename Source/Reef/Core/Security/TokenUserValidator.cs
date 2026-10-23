using System.Security.Claims;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;

namespace Reef.Core.Security;

/// <summary>
/// Rejects tokens of deactivated or deleted accounts and of accounts with a different role or password.
/// </summary>
public sealed class TokenUserValidator(Reef.DatabaseConfig config)
{
    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("userId"), out var id))
            return false;

        await using var connection = new SqliteConnection(config.ConnectionString);
        var user = await connection.QuerySingleOrDefaultAsync<User>("SELECT * FROM Users WHERE Id = @id", new { id });
        return user is { IsActive: true, IsDeleted: false }
            && user.Role == principal.FindFirstValue(ClaimTypes.Role)
            && JwtTokenService.PasswordStamp(user.PasswordHash) == principal.FindFirstValue(JwtTokenService.PasswordStampClaim);
    }
}
