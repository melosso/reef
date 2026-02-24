using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Serilog;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing webhook triggers for profiles and jobs
///
/// Features:
/// - Secure token generation (CSPRNG-based)
/// - Flexible rate limiting (requests per time window)
/// - "Only once per period" mode (maxRequests=1)
/// - Unlimited access mode (maxRequests=0)
///
/// Rate Limiting Examples:
/// - Default: 100 requests per hour
/// - Once per day: CheckRateLimit(webhookId, 1, 24)
/// - Once per hour: CheckRateLimit(webhookId, 1, 1)
/// - 10 requests per 6 hours: CheckRateLimit(webhookId, 10, 6)
/// - Unlimited: CheckRateLimit(webhookId, 0, 0)
/// </summary>
public class WebhookService
{
    private readonly string _connectionString;
    private readonly NotificationService _notificationService;
    private static readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimits = new();

    // Default rate limiting: 100 requests per hour
    private const int DEFAULT_MAX_REQUESTS = 100;
    private const int DEFAULT_WINDOW_HOURS = 1;

    public WebhookService(DatabaseConfig config, NotificationService notificationService)
    {
        _connectionString = config.ConnectionString;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Create webhook trigger for a profile
    /// </summary>
    public async Task<(int WebhookId, string Token)> CreateWebhookAsync(int profileId)
    {
        try
        {
            // Generate secure token
            var token = GenerateWebhookToken();
            var tokenHash = ComputeTokenHash(token);

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                INSERT INTO WebhookTriggers (ProfileId, JobId, Token, IsActive, CreatedAt)
                VALUES (@ProfileId, NULL, @TokenHash, 1, datetime('now'));
                SELECT last_insert_rowid();";

            var webhookId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                ProfileId = profileId,
                TokenHash = tokenHash
            });

            Log.Information("Created webhook trigger {WebhookId} for profile {ProfileId}", webhookId, profileId);

            // Send webhook creation notification (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _notificationService.SendNewWebhookNotificationAsync($"Webhook for Profile {profileId}");
                }
                catch (Exception notifEx) { Log.Error(notifEx, "Failed to send webhook creation notification"); }
            });

            return (webhookId, token);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating webhook for profile {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Create webhook trigger for a job
    /// </summary>
    public async Task<(int WebhookId, string Token)> CreateWebhookForJobAsync(int jobId)
    {
        try
        {
            // Generate secure token
            var token = GenerateWebhookToken();
            var tokenHash = ComputeTokenHash(token);

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                INSERT INTO WebhookTriggers (ProfileId, JobId, Token, IsActive, CreatedAt)
                VALUES (NULL, @JobId, @TokenHash, 1, datetime('now'));
                SELECT last_insert_rowid();";

            var webhookId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                JobId = jobId,
                TokenHash = tokenHash
            });

            Log.Information("Created webhook trigger {WebhookId} for job {JobId}", webhookId, jobId);

            // Send webhook creation notification (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _notificationService.SendNewWebhookNotificationAsync($"Webhook for Job {jobId}");
                }
                catch (Exception notifEx) { Log.Error(notifEx, "Failed to send webhook creation notification"); }
            });

            return (webhookId, token);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating webhook for job {JobId}", jobId);
            throw;
        }
    }

    /// <summary>
    /// Get webhook triggers by profile ID
    /// </summary>
    public async Task<List<WebhookTrigger>> GetByProfileIdAsync(int profileId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM WebhookTriggers WHERE ProfileId = @ProfileId";

            var webhooks = await connection.QueryAsync<WebhookTrigger>(sql, new { ProfileId = profileId });
            return webhooks.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving webhooks for profile {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Get webhook trigger by ID
    /// </summary>
    public async Task<WebhookTrigger?> GetByIdAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM WebhookTriggers WHERE Id = @Id";
            return await connection.QueryFirstOrDefaultAsync<WebhookTrigger>(sql, new { Id = id });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving webhook {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Get webhook triggers by import profile ID
    /// </summary>
    public async Task<List<WebhookTrigger>> GetByImportProfileIdAsync(int importProfileId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM WebhookTriggers WHERE ImportProfileId = @ImportProfileId";
            var webhooks = await connection.QueryAsync<WebhookTrigger>(sql, new { ImportProfileId = importProfileId });
            return webhooks.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving webhooks for import profile {ImportProfileId}", importProfileId);
            throw;
        }
    }

    /// <summary>
    /// Create webhook trigger for an import profile
    /// </summary>
    public async Task<(int WebhookId, string Token)> CreateWebhookForImportProfileAsync(int importProfileId)
    {
        try
        {
            var token = GenerateWebhookToken();
            var tokenHash = ComputeTokenHash(token);

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                INSERT INTO WebhookTriggers (ProfileId, JobId, ImportProfileId, Token, IsActive, CreatedAt)
                VALUES (NULL, NULL, @ImportProfileId, @TokenHash, 1, datetime('now'));
                SELECT last_insert_rowid();";

            var webhookId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                ImportProfileId = importProfileId,
                TokenHash = tokenHash
            });

            Log.Information("Created webhook trigger {WebhookId} for import profile {ImportProfileId}", webhookId, importProfileId);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _notificationService.SendNewWebhookNotificationAsync($"Webhook for Import Profile {importProfileId}");
                }
                catch (Exception notifEx) { Log.Error(notifEx, "Failed to send webhook creation notification"); }
            });

            return (webhookId, token);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating webhook for import profile {ImportProfileId}", importProfileId);
            throw;
        }
    }

    /// <summary>
    /// Get webhook triggers by job ID
    /// </summary>
    public async Task<List<WebhookTrigger>> GetByJobIdAsync(int jobId)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM WebhookTriggers WHERE JobId = @JobId";

            var webhooks = await connection.QueryAsync<WebhookTrigger>(sql, new { JobId = jobId });
            return webhooks.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving webhooks for job {JobId}", jobId);
            throw;
        }
    }

    /// <summary>
    /// Get webhook trigger by token
    /// </summary>
    public async Task<WebhookTrigger?> GetByTokenAsync(string token)
    {
        try
        {
            var tokenHash = ComputeTokenHash(token);

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "SELECT * FROM WebhookTriggers WHERE Token = @TokenHash";

            var webhook = await connection.QueryFirstOrDefaultAsync<WebhookTrigger>(sql, new { TokenHash = tokenHash });
            return webhook;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving webhook by token");
            throw;
        }
    }

    /// <summary>
    /// Delete webhook trigger
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "DELETE FROM WebhookTriggers WHERE Id = @Id";

            var rows = await connection.ExecuteAsync(sql, new { Id = id });

            if (rows > 0)
            {
                Log.Information("Deleted webhook trigger {WebhookId}", id);
            }

            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting webhook {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Regenerate webhook token
    /// </summary>
    public async Task<string> RegenerateTokenAsync(int id)
    {
        try
        {
            var token = GenerateWebhookToken();
            var tokenHash = ComputeTokenHash(token);

            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                UPDATE WebhookTriggers 
                SET Token = @TokenHash
                WHERE Id = @Id";

            var rows = await connection.ExecuteAsync(sql, new { Id = id, TokenHash = tokenHash });

            if (rows > 0)
            {
                Log.Information("Regenerated token for webhook {WebhookId}", id);
                
                // Clear rate limit for old token
                _rateLimits.TryRemove($"webhook_{id}", out _);
            }

            return token;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error regenerating token for webhook {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Enable webhook trigger
    /// </summary>
    public async Task<bool> EnableAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "UPDATE WebhookTriggers SET IsActive = 1 WHERE Id = @Id";

            var rows = await connection.ExecuteAsync(sql, new { Id = id });

            if (rows > 0)
            {
                Log.Information("Enabled webhook trigger {WebhookId}", id);
            }

            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enabling webhook {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Disable webhook trigger
    /// </summary>
    public async Task<bool> DisableAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = "UPDATE WebhookTriggers SET IsActive = 0 WHERE Id = @Id";

            var rows = await connection.ExecuteAsync(sql, new { Id = id });

            if (rows > 0)
            {
                Log.Information("Disabled webhook trigger {WebhookId}", id);
            }

            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disabling webhook {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// Update last triggered timestamp
    /// </summary>
    public async Task UpdateLastTriggeredAsync(int id)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            const string sql = @"
                UPDATE WebhookTriggers 
                SET LastTriggeredAt = datetime('now'),
                    TriggerCount = TriggerCount + 1
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, new { Id = id });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating last triggered for webhook {Id}", id);
        }
    }

    /// <summary>
    /// Check rate limit for webhook (default: 100 requests per hour)
    /// </summary>
    public bool CheckRateLimit(int webhookId)
    {
        return CheckRateLimit(webhookId, DEFAULT_MAX_REQUESTS, DEFAULT_WINDOW_HOURS);
    }

    /// <summary>
    /// Check rate limit for webhook with custom limits
    /// </summary>
    /// <param name="webhookId">The webhook ID</param>
    /// <param name="maxRequests">Maximum requests allowed (0 = unlimited, 1 = only once per period)</param>
    /// <param name="windowHours">Time window in hours</param>
    /// <returns>True if request is allowed, false if rate limited</returns>
    public bool CheckRateLimit(int webhookId, int maxRequests, int windowHours)
    {
        var key = $"webhook_{webhookId}";
        var now = DateTime.UtcNow;

        var rateLimitInfo = _rateLimits.GetOrAdd(key, _ => new RateLimitInfo());

        lock (rateLimitInfo)
        {
            // Remove requests older than the window
            var windowCutoff = now.AddHours(-windowHours);
            rateLimitInfo.Requests.RemoveAll(r => r < windowCutoff);

            // If maxRequests is 0, unlimited access
            if (maxRequests <= 0)
            {
                rateLimitInfo.Requests.Add(now);
                return true;
            }

            // Check if under limit
            if (rateLimitInfo.Requests.Count >= maxRequests)
            {
                Log.Warning("Rate limit exceeded for webhook {WebhookId} ({RequestCount}/{MaxRequests} in {WindowHours}h)",
                    webhookId, rateLimitInfo.Requests.Count, maxRequests, windowHours);
                return false;
            }

            // Add current request
            rateLimitInfo.Requests.Add(now);
            return true;
        }
    }

    /// <summary>
    /// Get rate limit info for webhook (default window)
    /// </summary>
    public (int RequestCount, DateTime? OldestRequest) GetRateLimitInfo(int webhookId)
    {
        return GetRateLimitInfo(webhookId, DEFAULT_WINDOW_HOURS);
    }

    /// <summary>
    /// Get rate limit info for webhook with custom window
    /// </summary>
    public (int RequestCount, DateTime? OldestRequest) GetRateLimitInfo(int webhookId, int windowHours)
    {
        var key = $"webhook_{webhookId}";

        if (_rateLimits.TryGetValue(key, out var rateLimitInfo))
        {
            lock (rateLimitInfo)
            {
                var now = DateTime.UtcNow;
                var windowCutoff = now.AddHours(-windowHours);
                rateLimitInfo.Requests.RemoveAll(r => r < windowCutoff);

                return (
                    rateLimitInfo.Requests.Count,
                    rateLimitInfo.Requests.Count > 0 ? rateLimitInfo.Requests.Min() : null
                );
            }
        }

        return (0, null);
    }

    /// <summary>
    /// Generate secure webhook token
    /// Format: reef_wh_<base64url>
    /// </summary>
    private string GenerateWebhookToken()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        var base64 = Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        return $"reef_wh_{base64}";
    }

    /// <summary>
    /// Compute SHA256 hash of token for storage
    /// </summary>
    private string ComputeTokenHash(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Validate webhook token format
    /// </summary>
    public static bool IsValidTokenFormat(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (!token.StartsWith("reef_wh_"))
            return false;

        var tokenPart = token.Substring(8);
        return tokenPart.Length >= 32 && tokenPart.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
    }
}

/// <summary>
/// Rate limit tracking for webhooks
/// </summary>
public class RateLimitInfo
{
    public List<DateTime> Requests { get; set; } = new();
}