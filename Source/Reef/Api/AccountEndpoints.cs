using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Security;
using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;
using Reef.Core.Services;
using OtpNet;
using QRCoder;

namespace Reef.Api;

/// <summary>
/// Account management endpoints for the current authenticated user
/// </summary>
public static class AccountEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/account").RequireAuthorization();

        group.MapGet("/profile", GetProfile);
        group.MapPut("/profile", UpdateProfile);
        group.MapGet("/mfa/totp/setup", SetupTotp);
        group.MapPost("/mfa/totp/confirm", ConfirmTotp);
        group.MapPost("/mfa/email/enable", EnableEmailMfa);
        group.MapDelete("/mfa", DisableMfa);
    }

    // ── GET /api/account/profile ──────────────────────────────────────────────

    private static async Task<IResult> GetProfile(
        HttpContext httpContext,
        [FromServices] DatabaseConfig dbConfig)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

        await using var conn = new SqliteConnection(dbConfig.ConnectionString);
        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsDeleted = 0",
            new { Username = username.ToLowerInvariant() });

        if (user == null) return Results.NotFound();

        // Email MFA is only available when system notifications are configured and enabled
        var notificationsEnabled = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM NotificationSettings WHERE IsEnabled = 1") > 0;

        return Results.Ok(new
        {
            username = user.Username,
            displayName = user.DisplayName,
            email = user.Email,
            role = user.Role,
            mfaEnabled = user.MfaEnabled,
            mfaMethod = user.MfaMethod,
            emailMfaAvailable = notificationsEnabled
        });
    }

    // ── PUT /api/account/profile ─────────────────────────────────────────────

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        HttpContext httpContext,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] AuditService auditService)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

        await using var conn = new SqliteConnection(dbConfig.ConnectionString);
        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsDeleted = 0",
            new { Username = username.ToLowerInvariant() });

        if (user == null) return Results.NotFound();

        // Validate email if provided
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var atIdx = request.Email.IndexOf('@');
            if (atIdx <= 0 || atIdx == request.Email.Length - 1)
                return Results.Json(new { success = false, message = "Invalid email address" }, statusCode: 400);
        }

        await conn.ExecuteAsync(
            "UPDATE Users SET DisplayName = @DisplayName, Email = @Email, ModifiedAt = datetime('now') WHERE Id = @Id",
            new { DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                  Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                  user.Id });

        await auditService.LogAsync("User", user.Id, "ProfileUpdated", username,
            new { request.DisplayName, request.Email }, httpContext);

        return Results.Ok(new { success = true });
    }

    // GET /api/account/mfa/totp/setup
    // Generates a new TOTP secret and returns a QR code PNG (base64) + manual key. This is NOT saved yet but only saved after the user confirms with a valid code.
    private static async Task<IResult> SetupTotp(
        HttpContext httpContext,
        [FromServices] DatabaseConfig dbConfig)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

        await using var conn = new SqliteConnection(dbConfig.ConnectionString);
        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsDeleted = 0",
            new { Username = username.ToLowerInvariant() });

        if (user == null) return Results.NotFound();

        // Generate 20-byte TOTP secret (RFC 6238 / SHA-1)
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        // Build the otpauth:// URI
        var issuer = Uri.EscapeDataString("Reef");
        var account = Uri.EscapeDataString(user.Username);
        var otpauthUri = $"otpauth://totp/{issuer}:{account}?secret={secretBase32}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";

        // Generate QR code PNG → base64
        var qrGenerator = new QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(otpauthUri, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        var qrPng = qrCode.GetGraphic(6);
        var qrBase64 = Convert.ToBase64String(qrPng);

        // Store in PendingTotpSecret so the active TotpSecret is never overwritten
        // until the user confirms with a valid code.
        await conn.ExecuteAsync(
            "UPDATE Users SET PendingTotpSecret = @Secret WHERE Id = @Id",
            new { Secret = secretBase32, user.Id });

        return Results.Ok(new
        {
            secretBase32,
            qrCodePng = qrBase64,
            otpauthUri
        });
    }

    // ── POST /api/account/mfa/totp/confirm ───────────────────────────────────

    private static async Task<IResult> ConfirmTotp(
        [FromBody] ConfirmTotpRequest request,
        HttpContext httpContext,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] AuditService auditService)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

        await using var conn = new SqliteConnection(dbConfig.ConnectionString);
        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsDeleted = 0",
            new { Username = username.ToLowerInvariant() });

        if (user == null) return Results.NotFound();
        if (string.IsNullOrEmpty(user.PendingTotpSecret))
            return Results.Json(new { success = false, message = "No pending TOTP setup. Request a new setup first." }, statusCode: 400);

        var secretBytes = Base32Encoding.ToBytes(user.PendingTotpSecret);
        var totp = new Totp(secretBytes);
        var isValid = totp.VerifyTotp(request.Code.Trim(), out _, new VerificationWindow(1, 1));

        if (!isValid)
            return Results.Json(new { success = false, message = "Invalid code. Make sure your authenticator app is synced." }, statusCode: 400);

        // Generate 8 one-time backup codes (SHA-256 hashed for storage)
        var plainCodes = new List<string>();
        var hashedCodes = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToUpper();
            var formatted = $"{raw[..4]}-{raw[4..]}";
            plainCodes.Add(formatted);
            hashedCodes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(formatted))));
        }

        await conn.ExecuteAsync(
            "UPDATE Users SET MfaEnabled = 1, MfaMethod = 'totp', TotpSecret = @Secret, PendingTotpSecret = NULL, BackupCodes = @BackupCodes, ModifiedAt = datetime('now') WHERE Id = @Id",
            new { Secret = user.PendingTotpSecret, BackupCodes = JsonSerializer.Serialize(hashedCodes), user.Id });

        await auditService.LogAsync("User", user.Id, "MfaEnabled", username,
            new { method = "totp" }, httpContext);

        Log.Information("User {Username} enabled TOTP MFA", username);
        return Results.Ok(new { success = true, backupCodes = plainCodes });
    }

    // ── POST /api/account/mfa/email/enable ───────────────────────────────────

    private static async Task<IResult> EnableEmailMfa(
        HttpContext httpContext,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] AuditService auditService)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

        await using var conn = new SqliteConnection(dbConfig.ConnectionString);
        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE LOWER(Username) = @Username AND IsDeleted = 0",
            new { Username = username.ToLowerInvariant() });

        if (user == null) return Results.NotFound();

        // Require system notifications to be configured and enabled
        var notificationsEnabled = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM NotificationSettings WHERE IsEnabled = 1") > 0;
        if (!notificationsEnabled)
            return Results.Json(new { success = false, message = "System notifications must be configured and enabled before using email MFA. Ask an administrator to set this up in Settings." }, statusCode: 400);

        if (string.IsNullOrWhiteSpace(user.Email))
            return Results.Json(new { success = false, message = "An email address is required to enable email MFA. Please update your profile first." }, statusCode: 400);

        await conn.ExecuteAsync(
            "UPDATE Users SET MfaEnabled = 1, MfaMethod = 'email', TotpSecret = NULL, ModifiedAt = datetime('now') WHERE Id = @Id",
            new { user.Id });

        await auditService.LogAsync("User", user.Id, "MfaEnabled", username,
            new { method = "email" }, httpContext);

        Log.Information("User {Username} enabled Email MFA", username);
        return Results.Ok(new { success = true });
    }

    // ── DELETE /api/account/mfa ───────────────────────────────────────────────

    private static async Task<IResult> DisableMfa(
        HttpContext httpContext,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] AuditService auditService)
    {
        var username = httpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

        await using var conn = new SqliteConnection(dbConfig.ConnectionString);
        var user = await conn.QueryFirstOrDefaultAsync<User>(
            "SELECT Id FROM Users WHERE LOWER(Username) = @Username AND IsDeleted = 0",
            new { Username = username.ToLowerInvariant() });

        if (user == null) return Results.NotFound();

        await conn.ExecuteAsync(
            "UPDATE Users SET MfaEnabled = 0, MfaMethod = NULL, TotpSecret = NULL, ModifiedAt = datetime('now') WHERE Id = @Id",
            new { user.Id });

        await auditService.LogAsync("User", user.Id, "MfaDisabled", username, null, httpContext);

        Log.Information("User {Username} disabled MFA", username);
        return Results.Ok(new { success = true });
    }
}

public class UpdateProfileRequest
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
}

public class ConfirmTotpRequest
{
    public required string Code { get; set; }
}
