using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Security;
using Dapper;
using Microsoft.Data.Sqlite;
using OtpNet;
using Serilog;
using Reef.Core.Services;

namespace Reef.Api;

public static class AuthEndpoints
{
    private const int MaxMfaAttempts = 5;
    private const int MfaLockoutMinutes = 15;
    private const int OtpLength = 6;
    private const int BackupCodeLength = 8;

    private static readonly string[] AllowedOrigins =
    [
        "http://localhost:8085",
        "https://localhost:8085"
    ];

    private record MfaPendingSession(
        string Username,
        DateTime Expires,
        string Method,
        string? EmailOtp,
        string ClientFingerprint);

    private record MfaAttemptTracker(
        int Count,
        DateTime FirstAttempt,
        HashSet<string> AttemptedCodes);

    private static readonly ConcurrentDictionary<string, MfaPendingSession> _mfaSessions = new();
    private static readonly ConcurrentDictionary<string, MfaAttemptTracker> _mfaAttempts = new();

    private static readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(1);
    private static long _lastCleanupTicks = DateTime.UtcNow.Ticks;

    private static string GetClientFingerprint(HttpContext context)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        
        if (!string.IsNullOrEmpty(forwarded))
        {
            clientIp = forwarded.Split(',')[0].Trim();
        }
        
        return $"{clientIp}:{userAgent.GetHashCode()}";
    }

    private static bool ValidateOrigin(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.FirstOrDefault();
        
        if (string.IsNullOrEmpty(origin))
        {
            var referer = context.Request.Headers.Referer.FirstOrDefault();
            if (!string.IsNullOrEmpty(referer))
            {
                try
                {
                    var refererUri = new Uri(referer);
                    origin = $"{refererUri.Scheme}://{refererUri.Host}";
                    if (refererUri.Port != 80 && refererUri.Port != 443)
                    {
                        origin += $":{refererUri.Port}";
                    }
                }
                catch
                {
                    return true;
                }
            }
            return true;
        }
        
        return AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class OriginValidationFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(
            EndpointFilterInvocationContext context,
            EndpointFilterDelegate next)
        {
            if (!ValidateOrigin(context.HttpContext))
            {
                Log.Warning("Origin validation failed for MFA request from IP: {IP}", 
                    context.HttpContext.Connection.RemoteIpAddress);
                return Results.Json(new { message = "Invalid request origin" }, statusCode: 403);
            }
            return await next(context);
        }
    }

    private static string GenerateSecureOtp()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var num = BitConverter.ToUInt32(bytes);
        return (num % 900_000 + 100_000).ToString();
    }

    private static void CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastCleanupTicks);
        if (now.Ticks - lastTicks < _cleanupInterval.Ticks) return;

        // Only one thread runs cleanup per interval, if CAS fails, another thread won
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, lastTicks) != lastTicks) return;
        
        var expiredSessions = _mfaSessions
            .Where(kvp => kvp.Value.Expires < now)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in expiredSessions)
        {
            _mfaSessions.TryRemove(key, out _);
            _mfaAttempts.TryRemove(key, out _);
        }
        
        var expiredAttempts = _mfaAttempts
            .Where(kvp => kvp.Value.FirstAttempt.AddMinutes(MfaLockoutMinutes) < now)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var key in expiredAttempts)
        {
            _mfaAttempts.TryRemove(key, out _);
        }
        
        if (expiredSessions.Count > 0 || expiredAttempts.Count > 0)
        {
            Log.Debug("MFA cleanup: removed {SessionCount} sessions and {AttemptCount} attempt trackers", 
                expiredSessions.Count, expiredAttempts.Count);
        }
    }

    private static bool CheckRateLimit(string sessionId, string code)
    {
        CleanupExpiredSessions();

        var now = DateTime.UtcNow;

        var result = _mfaAttempts.AddOrUpdate(
            sessionId,
            _ => new MfaAttemptTracker(1, now, [code]),
            (_, existing) =>
            {
                // Reset tracker if the lockout window has expired
                if (existing.FirstAttempt.AddMinutes(MfaLockoutMinutes) <= now)
                    return new MfaAttemptTracker(1, now, [code]);

                return new MfaAttemptTracker(
                    existing.Count + 1,
                    existing.FirstAttempt,
                    new HashSet<string>(existing.AttemptedCodes) { code });
            });

        if (result.Count > MaxMfaAttempts)
        {
            Log.Warning("MFA rate limit exceeded for session {SessionId}", sessionId);
            return false;
        }

        return true;
    }

    private static bool TimingSafeEquals(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length) return false;
        
        var bytesA = Encoding.UTF8.GetBytes(a.ToArray());
        var bytesB = Encoding.UTF8.GetBytes(b.ToArray());
        
        return CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }

    public static void Map(WebApplication app)
    {
        var authGroup = app.MapGroup("/api/auth");

        authGroup.MapPost("/login", Login);
        authGroup.MapPost("/mfa", VerifyMfa).AddEndpointFilter(new OriginValidationFilter());
        authGroup.MapPost("/verify-otp", VerifyMfa).AddEndpointFilter(new OriginValidationFilter());
        authGroup.MapPost("/refresh", RefreshToken).RequireAuthorization();
        authGroup.MapPost("/validate", ValidateToken);
        authGroup.MapPost("/change-password", ChangePassword).RequireAuthorization();
        authGroup.MapPost("/logout", Logout);
        authGroup.MapPost("/resend-otp", ResendOtp).AddEndpointFilter(new OriginValidationFilter());
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
        [FromServices] NotificationService notificationService,
        HttpContext httpContext)
    {
        try
        {
            Log.Debug("Auth endpoint called: /api/auth/login for user {Username}", request.Username);

            await using var connection = new SqliteConnection(dbConfig.ConnectionString);
            await connection.OpenAsync();

            var normalizedUsername = request.Username.ToLowerInvariant();
            const string sql = "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1 AND IsDeleted = 0";
            var user = await connection.QueryFirstOrDefaultAsync<User>(sql, new { Username = normalizedUsername });

            if (user == null)
            {
                Log.Warning("Login attempt failed for user {Username}. Reason: user not found", request.Username);
                return Results.Json(new { message = "Invalid username or password" }, statusCode: 401);
            }

            if (!passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                Log.Warning("Login attempt failed for user {Username}. Reason: invalid password", request.Username);
                return Results.Json(new { message = "Invalid username or password" }, statusCode: 401);
            }

            // ── MFA gate ──────────────────────────────────────────────────────────
            if (user.MfaEnabled && !string.IsNullOrEmpty(user.MfaMethod))
            {
                // Clean up any expired sessions for this user
                foreach (var kvp in _mfaSessions.Where(s => s.Value.Username == user.Username || s.Value.Expires < DateTime.UtcNow).ToList())
                    _mfaSessions.TryRemove(kvp.Key, out _);

                var sessionId = Guid.NewGuid().ToString("N");
                var expiry    = DateTime.UtcNow.AddMinutes(5);
                var fingerprint = GetClientFingerprint(httpContext);

                string? emailOtp = null;
                if (user.MfaMethod == "email")
                {
                    if (string.IsNullOrWhiteSpace(user.Email))
                        return Results.Json(new { message = "Email MFA is configured but no email address is set. Contact your administrator." }, statusCode: 400);

                    // Generate and send a cryptographically secure 6-digit OTP
                    emailOtp = GenerateSecureOtp();
                    _ = Task.Run(async () =>
                    {
                        try { await notificationService.SendMfaOtpEmailAsync(user.Email, emailOtp); }
                        catch (Exception ex) { Log.Warning("Failed to send MFA OTP email to {Email}: {Error}", user.Email, ex.Message); }
                    });
                }

                _mfaSessions[sessionId] = new MfaPendingSession(user.Username, expiry, user.MfaMethod, emailOtp, fingerprint);

                Log.Information("MFA challenge issued for user {Username} (method: {Method})", user.Username, user.MfaMethod);
                return Results.Ok(new
                {
                    mfaRequired = true,
                    mfaMethod   = user.MfaMethod,
                    mfaSessionId = sessionId
                });
            }
            // ─────────────────────────────────────────────────────────────────────

            return await IssueToken(user, connection, jwtService, auditService, httpContext);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during login for user {Username}", request.Username);
            return Results.Problem("An error occurred during login");
        }
    }

    /// <summary>
    /// Second factor verification. Accepts the mfaSessionId from the login response + the OTP/TOTP code.
    /// POST /api/auth/mfa
    /// </summary>
    private static async Task<IResult> VerifyMfa(
        [FromBody] MfaVerifyRequest request,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] JwtTokenService jwtService,
        [FromServices] AuditService auditService,
        [FromServices] EncryptionService encryptionService,
        HttpContext httpContext)
    {
        var clientFingerprint = GetClientFingerprint(httpContext);

        if (!_mfaSessions.TryGetValue(request.MfaSessionId, out var session))
            return Results.Json(new { message = "Invalid or expired session. Please sign in again." }, statusCode: 401);

        if (session.Expires < DateTime.UtcNow)
        {
            _mfaSessions.TryRemove(request.MfaSessionId, out _);
            _mfaAttempts.TryRemove(request.MfaSessionId, out _);
            return Results.Json(new { message = "The code has expired. Please sign in again." }, statusCode: 401);
        }

        if (session.ClientFingerprint != clientFingerprint)
        {
            Log.Warning("MFA session fingerprint mismatch for user {Username}. Original: {OriginalIP}, Current: {CurrentIP}",
                session.Username, session.ClientFingerprint, clientFingerprint);
            _mfaSessions.TryRemove(request.MfaSessionId, out _);
            _mfaAttempts.TryRemove(request.MfaSessionId, out _);
            return Results.Json(new { message = "Session validation failed. Please sign in again." }, statusCode: 401);
        }

        if (!CheckRateLimit(request.MfaSessionId, request.Code))
        {
            _mfaSessions.TryRemove(request.MfaSessionId, out _);
            _mfaAttempts.TryRemove(request.MfaSessionId, out _);
            return Results.Json(new { message = $"Too many failed attempts. Please wait {MfaLockoutMinutes} minutes before trying again." }, statusCode: 429);
        }

        // Validate code length
        var code = request.Code.Replace(" ", "").Replace("-", "").Trim().ToUpper();
        if (code.Length != OtpLength && code.Length != BackupCodeLength)
        {
            return Results.Json(new { message = "Invalid code format. Please enter a 6-digit code." }, statusCode: 401);
        }

        // Consume the session immediately to prevent replay attacks
        _mfaSessions.TryRemove(request.MfaSessionId, out _);
        _mfaAttempts.TryRemove(request.MfaSessionId, out _);

        await using var connection = new SqliteConnection(dbConfig.ConnectionString);
        await connection.OpenAsync();

        var user = await connection.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1 AND IsDeleted = 0",
            new { Username = session.Username.ToLowerInvariant() });

        if (user == null)
            return Results.Json(new { message = "User not found." }, statusCode: 401);

        // Validate the code
        bool valid;
        if (session.Method == "totp")
        {
            valid = ValidateTotp(encryptionService.DecryptField(user.TotpSecret), code);
            if (!valid && code.Length == BackupCodeLength)
                valid = await ValidateAndConsumeBackupCodeAsync(user, code, connection, encryptionService);
        }
        else
        {
            valid = session.Method == "email" && TimingSafeEquals(
                session.EmailOtp.AsSpan(),
                request.Code.Trim().AsSpan());
        }

        if (!valid)
        {
            Log.Warning("MFA verification failed for user {Username} (method: {Method})", user.Username, session.Method);
            return Results.Json(new { message = "Invalid code. Please try again." }, statusCode: 401);
        }

        Log.Information("MFA verified for user {Username} (method: {Method})", user.Username, session.Method);
        return await IssueToken(user, connection, jwtService, auditService, httpContext);
    }

    private static bool ValidateTotp(string? secretBase32, string code)
    {
        if (string.IsNullOrEmpty(secretBase32) || code.Length != OtpLength) return false;
        try
        {
            var secretBytes = Base32Encoding.ToBytes(secretBase32);
            return new Totp(secretBytes).VerifyTotp(code, out _, new VerificationWindow(1, 1));
        }
        catch { return false; }
    }

    private static async Task<bool> ValidateAndConsumeBackupCodeAsync(User user, string normalizedCode, SqliteConnection connection, EncryptionService encryptionService)
    {
        if (string.IsNullOrEmpty(user.BackupCodes)) return false;
        try
        {
            var backupCodesJson = encryptionService.DecryptField(user.BackupCodes);
            if (string.IsNullOrEmpty(backupCodesJson)) return false;
            var hashes = JsonSerializer.Deserialize<List<string>>(backupCodesJson);
            if (hashes == null || hashes.Count == 0) return false;

            var formatted = normalizedCode.Length == BackupCodeLength 
                ? $"{normalizedCode[..4]}-{normalizedCode[4..]}" 
                : normalizedCode;
            var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(formatted)));

            string? matchedHash = null;
            foreach (var storedHash in hashes)
            {
                if (TimingSafeEquals(storedHash.AsSpan(), inputHash.AsSpan()))
                {
                    matchedHash = storedHash;
                    break;
                }
            }

            if (matchedHash == null) return false;

            hashes.Remove(matchedHash);
            await connection.ExecuteAsync(
                "UPDATE Users SET BackupCodes = @Codes WHERE Id = @Id",
                new { Codes = encryptionService.Encrypt(JsonSerializer.Serialize(hashes)), user.Id });

            Log.Warning("User {Username} signed in with a backup code. {Remaining} code(s) remaining.", user.Username, hashes.Count);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Shared helper: generate JWT, set cookie, update last-login, return LoginResponse.</summary>
    private static async Task<IResult> IssueToken(
        User user,
        SqliteConnection connection,
        JwtTokenService jwtService,
        AuditService auditService,
        HttpContext httpContext)
    {
        var token     = jwtService.GenerateToken(user.Username, user.Role, user.Id);
        var expiresAt = DateTime.UtcNow.AddMinutes(60);

        await connection.ExecuteAsync(
            "UPDATE Users SET LastLoginAt = datetime('now') WHERE Id = @Id",
            new { user.Id });

        await auditService.LogAsync("User", user.Id, "Login", user.Username, null, httpContext);

        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
        Log.Information("User {Username} logged in successfully from IP {IP}", user.Username, remoteIp);

        httpContext.Response.Cookies.Append("reef_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure   = false,
            SameSite = SameSiteMode.Lax,
            Expires  = expiresAt,
            Path     = "/"
        });

        return Results.Ok(new LoginResponse
        {
            Token       = token,
            Username    = user.Username,
            Role        = user.Role,
            DisplayName = user.DisplayName,
            ExpiresAt   = expiresAt
        });
    }

    /// <summary>
    /// Refresh token endpoint - generates a new JWT token
    /// POST /api/auth/refresh
    /// </summary>
    private static async Task<IResult> RefreshToken(
        HttpContext httpContext,
        [FromServices] JwtTokenService jwtService,
        [FromServices] DatabaseConfig dbConfig)
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

            // Get user to fetch displayName
            string? displayName = null;
            try
            {
                using var connection = new SqliteConnection(dbConfig.ConnectionString);
                const string sql = "SELECT DisplayName FROM Users WHERE Id = @UserId";
                displayName = await connection.QueryFirstOrDefaultAsync<string>(sql, new { UserId = userId });
            }
            catch { /* Ignore - displayName is optional */ }

            var newToken = jwtService.GenerateToken(username, role, userId);
            var expiresAt = DateTime.UtcNow.AddMinutes(60);

            // Set HTTP-only cookie with the new JWT token
            httpContext.Response.Cookies.Append("reef_token", newToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = false, // Set to true in production with HTTPS
                SameSite = SameSiteMode.Lax,
                Expires = expiresAt,
                Path = "/"
            });

            return Results.Ok(new
            {
                token = newToken,
                username,
                role,
                displayName,
                expiresAt
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during token refresh");
            return Results.Problem("An error occurred during token refresh");
        }
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

            const string sql = "SELECT PasswordChangeRequired FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1 AND IsDeleted = 0";
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
            const string getUserSql = "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1 AND IsDeleted = 0";
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

    /// <summary>
    /// Logout endpoint - clears the authentication cookie
    /// POST /api/auth/logout
    /// </summary>
    private static IResult Logout(HttpContext httpContext)
    {
        try
        {
            // Clear the HTTP-only cookie
            httpContext.Response.Cookies.Delete("reef_token", new CookieOptions
            {
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });

            Log.Debug("User logged out successfully");

            return Results.Ok(new { success = true, message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during logout");
            return Results.Problem("An error occurred during logout");
        }
    }

    /// <summary>
    /// Resend OTP endpoint - generates and sends a new OTP code for email MFA
    /// POST /api/auth/resend-otp
    /// </summary>
    private static async Task<IResult> ResendOtp(
        [FromBody] ResendOtpRequest request,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] NotificationService notificationService,
        HttpContext httpContext)
    {
        var clientFingerprint = GetClientFingerprint(httpContext);

        if (!_mfaSessions.TryGetValue(request.MfaSessionId, out var session))
            return Results.Json(new { message = "Invalid or expired session. Please sign in again." }, statusCode: 401);

        if (session.Expires < DateTime.UtcNow)
        {
            _mfaSessions.TryRemove(request.MfaSessionId, out _);
            _mfaAttempts.TryRemove(request.MfaSessionId, out _);
            return Results.Json(new { message = "The session has expired. Please sign in again." }, statusCode: 401);
        }

        if (session.ClientFingerprint != clientFingerprint)
        {
            Log.Warning("MFA session fingerprint mismatch on resend for user {Username}", session.Username);
            _mfaSessions.TryRemove(request.MfaSessionId, out _);
            _mfaAttempts.TryRemove(request.MfaSessionId, out _);
            return Results.Json(new { message = "Session validation failed. Please sign in again." }, statusCode: 401);
        }

        if (session.Method != "email")
        {
            return Results.Json(new { message = "Resend is only available for email MFA." }, statusCode: 400);
        }

        await using var connection = new SqliteConnection(dbConfig.ConnectionString);
        await connection.OpenAsync();

        var user = await connection.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsActive = 1 AND IsDeleted = 0",
            new { Username = session.Username.ToLowerInvariant() });

        if (user == null)
            return Results.Json(new { message = "User not found." }, statusCode: 401);

        if (string.IsNullOrWhiteSpace(user.Email))
            return Results.Json(new { message = "No email address configured. Contact your administrator." }, statusCode: 400);

        // Generate new OTP and update session
        var newOtp = GenerateSecureOtp();
        var newExpiry = DateTime.UtcNow.AddMinutes(5);
        var newFingerprint = GetClientFingerprint(httpContext);

        _mfaSessions[request.MfaSessionId] = session with 
        { 
            EmailOtp = newOtp, 
            Expires = newExpiry,
            ClientFingerprint = newFingerprint
        };

        // Clear previous attempts on resend
        _mfaAttempts.TryRemove(request.MfaSessionId, out _);

        _ = Task.Run(async () => await notificationService.SendMfaOtpEmailAsync(user.Email, newOtp));

        Log.Information("New OTP sent to user {Username}", user.Username);

        return Results.Ok(new { message = "A new code has been sent to your email." });
    }
}

public class MfaVerifyRequest
{
    public required string MfaSessionId { get; set; }
    public required string Code { get; set; }
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

public class LoginResponse
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public required string Role { get; set; }
    public string? DisplayName { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class ResendOtpRequest
{
    public required string MfaSessionId { get; set; }
}