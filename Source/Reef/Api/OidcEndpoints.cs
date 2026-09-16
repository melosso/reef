using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Dapper;
using Reef.Core.Models;
using Reef.Core.Security;
using Reef.Core.Services;
using Serilog;

namespace Reef.Api;

/// <summary>
/// Single-provider generic OIDC SSO sign-in (Pocket ID, Azure Entra ID, or any standard
/// discovery-compliant provider). Configuration lives in AdminEndpoints (/api/admin/oidc-settings).
/// </summary>
public static class OidcEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/auth/oidc");

        group.MapGet("/status", Status);
        group.MapGet("/start", Start);
        group.MapGet("/callback", Callback);
    }

    /// <summary>
    /// GET /api/auth/oidc/status - Whether SSO is enabled and what to label the login button.
    /// Public and cheap; the login page polls this to decide whether to render the button.
    /// </summary>
    private static async Task<IResult> Status([FromServices] AdminService service)
    {
        var settings = await service.GetOidcSettingsAsync();
        return Results.Ok(new
        {
            enabled = settings?.IsEnabled ?? false,
            name = settings?.Name ?? "Single Sign-On"
        });
    }

    /// <summary>
    /// GET /api/auth/oidc/start - Begin the PKCE authorization-code flow and redirect to the IdP.
    /// </summary>
    private static async Task<IResult> Start(
        HttpContext context,
        [FromServices] AdminService service)
    {
        var settings = await service.GetOidcSettingsAsync();
        if (settings is not { IsEnabled: true })
            return Results.Redirect("/index?sso=" + OidcFlow.Disabled);

        try
        {
            var redirectUri = RedirectUri(context);
            var start = await OidcFlow.BeginAsync(settings, redirectUri, context.RequestAborted);
            return Results.Redirect(start.AuthorizeUrl);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start the SSO sign-in flow");
            return Results.Redirect("/index?sso=" + OidcFlow.Failed);
        }
    }

    /// <summary>
    /// GET /api/auth/oidc/callback - Complete the flow, resolve the Reef user, and sign them in.
    /// </summary>
    private static async Task<IResult> Callback(
        HttpContext context,
        string? code,
        string? state,
        string? error,
        [FromServices] AdminService service,
        [FromServices] DatabaseConfig dbConfig,
        [FromServices] PasswordHasher passwordHasher,
        [FromServices] JwtTokenService jwtService,
        [FromServices] AuditService auditService,
        [FromServices] IHttpClientFactory httpClientFactory)
    {
        var flow = OidcFlow.Claim(state);
        if (flow == null) return Results.Redirect("/index?sso=" + OidcFlow.Failed);

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            Log.Information("SSO sign-in was cancelled or denied by the provider");
            return Results.Redirect("/index?sso=" + OidcFlow.Denied);
        }

        var pair = await service.GetOidcSettingsForSignInAsync();
        if (pair is not { Settings.IsEnabled: true })
            return Results.Redirect("/index?sso=" + OidcFlow.Disabled);

        var (settings, clientSecret) = pair.Value;

        OidcIdentity? identity;
        try
        {
            using var http = httpClientFactory.CreateClient();
            identity = await OidcFlow.CompleteAsync(settings, clientSecret, flow, code, http, context.RequestAborted);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to complete the SSO sign-in flow");
            identity = null;
        }

        if (identity == null) return Results.Redirect("/index?sso=" + OidcFlow.Failed);

        await using var connection = new SqliteConnection(dbConfig.ConnectionString);
        await connection.OpenAsync();

        var (user, problem) = await ResolveAccountAsync(connection, passwordHasher, settings, identity);
        if (user == null) return Results.Redirect("/index?sso=" + problem);

        await AuthEndpoints.IssueToken(user, connection, jwtService, auditService, context);
        return Results.Redirect("/dashboard");
    }

    /// <summary>
    /// Decide which Reef user an incoming SSO identity maps to: an existing link by subject,
    /// then an auto-link by username or verified email, then optional first-time provisioning.
    /// </summary>
    private static async Task<(User? User, string Problem)> ResolveAccountAsync(
        SqliteConnection connection,
        PasswordHasher passwordHasher,
        OidcSettings settings,
        OidcIdentity identity)
    {
        var user = await connection.QueryFirstOrDefaultAsync<User>(
            "SELECT * FROM Users WHERE OidcSubject = @Subject AND IsDeleted = 0",
            new { identity.Subject });

        if (user == null && !string.IsNullOrEmpty(identity.Username))
        {
            user = await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE OidcSubject = '' AND LOWER(Username) = LOWER(@Username) AND IsDeleted = 0",
                new { identity.Username });
        }

        if (user == null && identity.EmailVerified && !string.IsNullOrEmpty(identity.Email))
        {
            user = await connection.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE OidcSubject = '' AND LOWER(Email) = LOWER(@Email) AND IsDeleted = 0",
                new { identity.Email });
        }

        if (user != null)
        {
            await connection.ExecuteAsync(
                "UPDATE Users SET OidcSubject = @Subject WHERE Id = @Id",
                new { identity.Subject, user.Id });
            user.OidcSubject = identity.Subject;
        }
        else if (settings.CreateAccounts)
        {
            var username = await DeriveUniqueUsernameAsync(connection, identity);
            var randomPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            var passwordHash = passwordHasher.HashPassword(randomPassword);

            var userId = await connection.QuerySingleAsync<int>(
                @"INSERT INTO Users (Username, PasswordHash, Role, IsActive, Email, DisplayName, CreatedAt, OidcSubject)
                  VALUES (@Username, @PasswordHash, 'User', 1, @Email, @DisplayName, @CreatedAt, @Subject)
                  RETURNING Id",
                new
                {
                    Username = username,
                    PasswordHash = passwordHash,
                    Email = string.IsNullOrEmpty(identity.Email) ? null : identity.Email,
                    DisplayName = string.IsNullOrEmpty(identity.Username) ? username : identity.Username,
                    CreatedAt = DateTime.UtcNow,
                    identity.Subject
                });

            user = await connection.QueryFirstOrDefaultAsync<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = userId });
        }
        else
        {
            Log.Warning("SSO sign-in for subject {Subject} matched no existing account and account creation is off", identity.Subject);
            return (null, OidcFlow.NoAccount);
        }

        if (user == null || !user.IsActive) return (null, OidcFlow.Disabled);

        return (user, "");
    }

    private static async Task<string> DeriveUniqueUsernameAsync(SqliteConnection connection, OidcIdentity identity)
    {
        var baseName = !string.IsNullOrEmpty(identity.Username) ? identity.Username
            : !string.IsNullOrEmpty(identity.Email) ? identity.Email.Split('@')[0]
            : "user" + identity.Subject;

        baseName = new string(baseName.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        if (string.IsNullOrEmpty(baseName)) baseName = "user";

        var candidate = baseName;
        var suffix = 1;
        while (await connection.ExecuteScalarAsync<int>(
                   "SELECT COUNT(*) FROM Users WHERE LOWER(Username) = @Username", new { Username = candidate }) > 0)
        {
            candidate = $"{baseName}{++suffix}";
        }

        return candidate;
    }

    private static string RedirectUri(HttpContext context) =>
        $"{context.Request.Scheme}://{context.Request.Host}/api/auth/oidc/callback";
}
