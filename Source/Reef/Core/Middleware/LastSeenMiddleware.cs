using Microsoft.Data.Sqlite;
using Dapper;
using System.Collections.Concurrent;

namespace Reef.Core.Middleware;

/// <summary>
/// Middleware to track user last seen activity with throttling
/// </summary>
public class LastSeenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _connectionString;
    private static readonly ConcurrentDictionary<string, DateTime> _lastUpdateCache = new();
    private static readonly TimeSpan _updateThrottle = TimeSpan.FromMinutes(5);
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<LastSeenMiddleware>();

    public LastSeenMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        // Match the same pattern used in Program.cs for connection string
        var dbPath = configuration["Reef:DatabasePath"] ?? "Reef.db";
        _connectionString = configuration.GetConnectionString("Reef") ?? $"Data Source={dbPath}";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Call the next middleware first
        await _next(context);

        // After the request is processed, update last seen if user is authenticated
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var username = context.User.Identity.Name;

            if (!string.IsNullOrEmpty(username))
            {
                // Check if we should update (throttle to once per 5 minutes)
                if (ShouldUpdateLastSeen(username))
                {
                    // Fire and forget - don't wait for the database update
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await UpdateLastSeenAsync(username);
                            _lastUpdateCache[username] = DateTime.UtcNow;
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Failed to update last seen for user {Username}", username);
                        }
                    });
                }
            }
        }
    }

    private bool ShouldUpdateLastSeen(string username)
    {
        if (!_lastUpdateCache.TryGetValue(username, out var lastUpdate))
        {
            return true; // Never updated before
        }

        return DateTime.UtcNow - lastUpdate >= _updateThrottle;
    }

    private async Task UpdateLastSeenAsync(string username)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE Users
            SET LastSeenAt = datetime('now')
            WHERE Username = @Username";

        await connection.ExecuteAsync(sql, new { Username = username });

        Log.Debug("Updated last seen for user {Username}", username);
    }
}
