using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Security;
using Dapper;
using Microsoft.Data.Sqlite;

using Serilog;
using Reef.Core.Services;

namespace Reef.Api;

/// <summary>
/// Authentication endpoints for login and token management
/// </summary>
public static class AuthEndpoints
{
    public static void Map(WebApplication app)
    {
        var authGroup = app.MapGroup("/api/auth");

        authGroup.MapPost("/login", Login);
        authGroup.MapPost("/refresh", RefreshToken).RequireAuthorization();
        authGroup.MapPost("/validate", ValidateToken);
        authGroup.MapPost("/change-password", ChangePassword).RequireAuthorization();
    }

    /// <summary>
    /// Login endpoint - validates credentials and returns JWT token
    /// POST /api/auth/login
    /// </summary>
    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] PasswordHasher passwordHasher,
        [FromServices] JwtTokenService jwtService,
        [FromServices] AuditService auditService,
        HttpContext httpContext)
    {
        try
        {
            Log.Debug("Auth endpoint called: /api/auth/login for user {Username}", request.Username);
                
            await using var connection = new SqliteConnection(dbConfig.ConnectionString);
            await connection.OpenAsync();

            // Normalize username to lowercase for case-insensitive lookup
            var normalizedUsername = request.Username.ToLowerInvariant();

            // Find user by username (case-insensitive via COLLATE NOCASE)
            const string sql = "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1";
            var user = await connection.QueryFirstOrDefaultAsync<User>(sql, new { Username = normalizedUsername });

            if (user == null)
            {
                Log.Warning("Login attempt failed for user {Username} - user not found", request.Username);
                return Results.Json(new { message = "Invalid username or password" }, statusCode: 401);
            }

            // Verify password
            if (!passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                Log.Warning("Login attempt failed for user {Username} - invalid password", request.Username);
                return Results.Json(new { message = "Invalid username or password" }, statusCode: 401);
            }

            // Generate JWT token with userId
            var token = jwtService.GenerateToken(user.Username, user.Role, user.Id);
            var expiresAt = DateTime.UtcNow.AddMinutes(60);

            // Update last login timestamp
            const string updateSql = "UPDATE Users SET LastLoginAt = datetime('now') WHERE Id = @Id";
            await connection.ExecuteAsync(updateSql, new { user.Id });

            // Log audit event

            await auditService.LogAsync(
                entityType: "User",
                entityId: user.Id,
                action: "Login",
                performedBy: user.Username,
                changesObject: null,
                context: httpContext
            );

            var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
            Log.Information("User {Username} logged in successfully from IP {IP}", user.Username, remoteIp);

            return Results.Ok(new LoginResponse
            {
                Token = token,
                Username = user.Username,
                Role = user.Role,
                ExpiresAt = expiresAt
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during login for user {Username}", request.Username);
            return Results.Problem("An error occurred during login");
        }
    }

    /// <summary>
    /// Refresh token endpoint - generates a new JWT token
    /// POST /api/auth/refresh
    /// </summary>
    private static async Task<IResult> RefreshToken(
        HttpContext httpContext,
        [FromServices] JwtTokenService jwtService)
    {
        return await Task.Run(() =>
        {
            try
            {
                var username = httpContext.User.Identity?.Name;
                var role = httpContext.User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;
                var userIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier || c.Type == "userId" || c.Type == "sub")?.Value;

                if (username == null || role == null || !int.TryParse(userIdClaim, out var userId))
                {
                    return Results.Unauthorized();
                }

                var newToken = jwtService.GenerateToken(username, role, userId);
                var expiresAt = DateTime.UtcNow.AddMinutes(60);

                return Results.Ok(new
                {
                    token = newToken,
                    username,
                    role,
                    expiresAt
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during token refresh");
                return Results.Problem("An error occurred during token refresh");
            }
        });
    }

    /// <summary>
    /// Validate token endpoint - checks if a token is valid and returns user info
    /// POST /api/auth/validate
    /// </summary>
    private static async Task<IResult> ValidateToken(
        [FromBody] ValidateTokenRequest request,
        [FromServices] JwtTokenService jwtService,
        [FromServices] DatabaseConfig dbConfig)
    {
        try
        {
            var principal = jwtService.ValidateToken(request.Token);

            if (principal == null)
            {
                return Results.Ok(new { valid = false });
            }

            var username = principal.Identity?.Name;
            var role = principal.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;

            if (string.IsNullOrEmpty(username))
            {
                return Results.Ok(new { valid = false });
            }

            // Fetch user from database to get PasswordChangeRequired flag
            await using var connection = new SqliteConnection(dbConfig.ConnectionString);
            await connection.OpenAsync();

            const string sql = "SELECT PasswordChangeRequired FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1";
            var passwordChangeRequired = await connection.QueryFirstOrDefaultAsync<bool?>(
                sql,
                new { Username = username.ToLowerInvariant() });

            return Results.Ok(new
            {
                valid = true,
                username,
                role,
                passwordChangeRequired = passwordChangeRequired ?? false
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during token validation");
            return Results.Ok(new { valid = false });
        }
    }

    /// <summary>
    /// Change password endpoint - allows users to change their password
    /// POST /api/auth/change-password
    /// </summary>
    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] PasswordHasher passwordHasher,
        [FromServices] AuditService auditService,
        HttpContext httpContext)
    {
        try
        {
            var username = httpContext.User.Identity?.Name;

            if (string.IsNullOrEmpty(username))
            {
                return Results.Json(new { success = false, message = "User not authenticated" }, statusCode: 401);
            }

            await using var connection = new SqliteConnection(dbConfig.ConnectionString);
            await connection.OpenAsync();

            // Get user from database
            const string getUserSql = "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1";
            var user = await connection.QueryFirstOrDefaultAsync<User>(getUserSql, new { Username = username.ToLowerInvariant() });

            if (user == null)
            {
                return Results.Json(new { success = false, message = "User not found" }, statusCode: 404);
            }

            // Verify current password
            if (!passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                Log.Warning("Password change failed for user {Username} - incorrect current password", username);
                return Results.Json(new { success = false, message = "Current password is incorrect" }, statusCode: 400);
            }

            // Prevent reusing the same password
            if (passwordHasher.VerifyPassword(request.NewPassword, user.PasswordHash))
            {
                return Results.Json(new { success = false, message = "New password must be different from the current password" }, statusCode: 400);
            }

            // Validate new password
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            {
                return Results.Json(new { success = false, message = "New password must be at least 6 characters long" }, statusCode: 400);
            }

            // Hash new password
            var newPasswordHash = passwordHasher.HashPassword(request.NewPassword);

            // Update password and clear PasswordChangeRequired flag
            const string updateSql = @"
                UPDATE Users
                SET PasswordHash = @PasswordHash,
                    PasswordChangeRequired = 0,
                    ModifiedAt = datetime('now')
                WHERE Id = @Id";

            await connection.ExecuteAsync(updateSql, new
            {
                PasswordHash = newPasswordHash,
                user.Id
            });

            // Log audit event
            await auditService.LogAsync(
                entityType: "User",
                entityId: user.Id,
                action: "PasswordChanged",
                performedBy: username,
                changesObject: new { PasswordChanged = true },
                context: httpContext
            );

            Log.Information("User {Username} changed their password successfully", username);

            return Results.Ok(new
            {
                success = true,
                message = "Password changed successfully",
                requiresLogoff = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during password change");
            return Results.Problem("An error occurred during password change");
        }
    }
}

public class ValidateTokenRequest
{
    public required string Token { get; set; }
}

public class ChangePasswordRequest
{
    public required string CurrentPassword { get; set; }
    public required string NewPassword { get; set; }
}