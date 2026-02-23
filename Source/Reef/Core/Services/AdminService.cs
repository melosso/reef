// Source/Reef/Core/Services/AdminService.cs
// Service for system administration, metrics, and user management

using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Security;
using Reef.Core.Data;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Services;

/// <summary>
/// Service for system administration and monitoring
/// Handles user management, API keys, system metrics, and audit logs
/// </summary>
public class AdminService
{
    private readonly string _connectionString;
    private readonly PasswordHasher _passwordHasher;
    private readonly EncryptionService _encryptionService;
    private readonly NotificationService _notificationService;

    public AdminService(DatabaseConfig config, PasswordHasher passwordHasher, EncryptionService encryptionService, NotificationService notificationService)
    {
        _connectionString = config.ConnectionString;
        _passwordHasher = passwordHasher;
        _encryptionService = encryptionService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Get system metrics and statistics
    /// </summary>
    public async Task<object> GetSystemMetricsAsync()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Get total users
            var totalUsers = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users");

            // Get total connections
            var totalConnections = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Connections");

            // Get total profiles (export + import)
            var totalProfiles = await conn.ExecuteScalarAsync<int>(
                "SELECT (SELECT COUNT(*) FROM Profiles) + (SELECT COUNT(*) FROM ImportProfiles)");
            var activeProfiles = await conn.ExecuteScalarAsync<int>(
                "SELECT (SELECT COUNT(*) FROM Profiles WHERE IsEnabled = 1) + (SELECT COUNT(*) FROM ImportProfiles WHERE IsEnabled = 1)");

            // Get total executions
            var totalExecutions = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ProfileExecutions");
            var successfulExecutions = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ProfileExecutions WHERE Status = 'Success'");
            var failedLast24h = await conn.ExecuteScalarAsync<int>(@"SELECT COUNT(*) FROM ProfileExecutions WHERE Status = 'Failed' AND StartedAt >= datetime('now', '-1 day')");
            var runningExecutions = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ProfileExecutions WHERE Status = 'Running'");
            var successRate = totalExecutions > 0 ? Math.Round((double)successfulExecutions / totalExecutions * 100, 2) : 0;

            // Get total API keys
            var totalApiKeys = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ApiKeys");

            // Get exports directory size
            long exportsSize = 0;
            var exportsPath = Path.Combine(AppContext.BaseDirectory, "exports");
            if (Directory.Exists(exportsPath))
            {
                exportsSize = Directory.GetFiles(exportsPath, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            }

            // Get recent execution statistics
            var recentStats = await conn.QueryFirstOrDefaultAsync<dynamic>(@"SELECT COUNT(*) as Total, SUM(CASE WHEN Status = 'Success' THEN 1 ELSE 0 END) as Success, SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) as Failed, AVG(CAST((julianday(CompletedAt) - julianday(StartedAt)) * 86400000 AS INTEGER)) as AvgDurationMs FROM ProfileExecutions WHERE StartedAt >= datetime('now', '-7 days')");

            // Get uptime (from most recent application startup)
            var uptimeQuery = await conn.QueryFirstOrDefaultAsync<DateTime?>("SELECT StartedAt FROM ApplicationStartup ORDER BY StartedAt DESC LIMIT 1");
            var uptime = uptimeQuery.HasValue ? DateTime.UtcNow - uptimeQuery.Value : TimeSpan.Zero;

            // Get database size
            var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
            long dbSize = 0;
            if (File.Exists(dbPath))
            {
                dbSize = new FileInfo(dbPath).Length;
            }

            // Get notification settings
            var notificationSettings = await conn.QueryFirstOrDefaultAsync<NotificationSettings>("SELECT * FROM NotificationSettings LIMIT 1");
            var notificationsEnabled = notificationSettings?.IsEnabled ?? false;
            var dbSizeThresholdMb = notificationSettings?.DatabaseSizeThresholdBytes ?? 1073741824; // 1 GB default

            // Return a nested object
            return new
            {
                users = totalUsers,
                connections = totalConnections,
                apikeys = totalApiKeys,
                profiles = new
                {
                    total = totalProfiles,
                    active = activeProfiles
                },
                executions = new
                {
                    total = totalExecutions,
                    successRate = successRate,
                    failedLast24h = failedLast24h,
                    running = runningExecutions
                },
                system = new
                {
                    databaseSizeMb = Math.Round(dbSize / 1024.0 / 1024.0, 2),
                    databaseSizeThresholdMb = Math.Round(dbSizeThresholdMb / 1024.0 / 1024.0, 2),
                    exportsSizeMb = Math.Round(exportsSize / 1024.0 / 1024.0, 2),
                    uptimeDays = Math.Round(uptime.TotalDays, 2)
                },
                notifications = new
                {
                    isEnabled = notificationsEnabled,
                    databaseSizeThresholdMb = Math.Round(dbSizeThresholdMb / 1024.0 / 1024.0, 2)
                },
                recentStats = new
                {
                    last7Days = new
                    {
                        total = (int)(recentStats?.Total ?? 0),
                        success = (int)(recentStats?.Success ?? 0),
                        failed = (int)(recentStats?.Failed ?? 0),
                        avgDurationMs = (int)(recentStats?.AvgDurationMs ?? 0)
                    }
                },
                timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving system metrics");
            throw;
        }
    }

    /// <summary>
    /// Get audit logs with optional filtering and pagination
    /// </summary>
    public async Task<(IEnumerable<dynamic> Logs, int TotalCount)> GetAuditLogsAsync(
        int page = 1,
        int pageSize = 50,
        string? entityType = null,
        string? action = null,
        string? username = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var whereClause = "WHERE 1=1";
            var parameters = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(entityType))
            {
                whereClause += " AND EntityType = @EntityType";
                parameters.Add("EntityType", entityType);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                whereClause += " AND Action = @Action";
                parameters.Add("Action", action);
            }

            if (!string.IsNullOrWhiteSpace(username))
            {
                whereClause += " AND PerformedBy = @Username";
                parameters.Add("Username", username);
            }

            if (startDate.HasValue)
            {
                whereClause += " AND Timestamp >= @StartDate";
                parameters.Add("StartDate", startDate.Value);
            }

            if (endDate.HasValue)
            {
                whereClause += " AND Timestamp <= @EndDate";
                parameters.Add("EndDate", endDate.Value);
            }

            // Get total count
            var countSql = $"SELECT COUNT(*) FROM AuditLog {whereClause}";
            var totalCount = await conn.ExecuteScalarAsync<int>(countSql, parameters);

            // Get paginated results
            var offset = (page - 1) * pageSize;
            var sql = $"SELECT * FROM AuditLog {whereClause} ORDER BY Timestamp DESC LIMIT @PageSize OFFSET @Offset";
            parameters.Add("PageSize", pageSize);
            parameters.Add("Offset", offset);

            var logs = await conn.QueryAsync<dynamic>(sql, parameters);
            return (logs, totalCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving audit logs");
            throw;
        }
    }

    /// <summary>
    /// Get all users
    /// </summary>
    public async Task<IEnumerable<User>> GetUsersAsync()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Only return non-deleted users
            var users = await conn.QueryAsync<User>("SELECT * FROM Users WHERE IsDeleted = 0 ORDER BY Username");
            return users;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving users");
            throw;
        }
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    public async Task<int> CreateUserAsync(string username, string password, string role)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Normalize username to lowercase for consistency
            var normalizedUsername = username.ToLowerInvariant();

            // Check if username already exists among non-deleted users (case-insensitive)
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Users WHERE LOWER(Username) = @Username AND IsDeleted = 0",
                new { Username = normalizedUsername });

            if (exists > 0)
            {
                throw new InvalidOperationException($"User '{username}' already exists");
            }

            // Validate role
            if (role != "Admin" && role != "User")
            {
                throw new InvalidOperationException("Role must be 'Admin' or 'User'");
            }

            // Hash password
            var passwordHash = _passwordHasher.HashPassword(password);

            // Insert user with normalized (lowercase) username
            var userId = await conn.QuerySingleAsync<int>(
                @"INSERT INTO Users (Username, PasswordHash, Role, IsActive, CreatedAt, PasswordChangeRequired)
                  VALUES (@Username, @PasswordHash, @Role, 1, @CreatedAt, 0)
                  RETURNING Id",
                new
                {
                    Username = normalizedUsername,
                    PasswordHash = passwordHash,
                    Role = role,
                    CreatedAt = DateTime.UtcNow
                });

            Log.Information("Created user {Username} with role {Role}", normalizedUsername, role);

            // Send user creation notification (fire and forget)
            _notificationService.NotifyUserCreationAsync(normalizedUsername, $"{normalizedUsername}@local");

            return userId;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating user {Username}", username);
            throw;
        }
    }

    /// <summary>
    /// Update an existing user with protection for last admin
    /// </summary>
    public async Task<bool> UpdateUserAsync(User user, string? newPassword = null)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Get the current user state
            var currentUser = await conn.QuerySingleOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id",
                new { user.Id });

            if (currentUser == null)
            {
                throw new InvalidOperationException("User not found");
            }

            // Check if this is the last active administrator
            if ((currentUser.Role == "Admin" || currentUser.Role == "Administrator") && currentUser.IsActive)
            {
                // If trying to disable or change role of admin, check if it's the last one
                if (!user.IsActive || (user.Role != "Admin" && user.Role != "Administrator"))
                {
                    var activeAdminCount = await CountActiveAdministratorsAsync();
                    if (activeAdminCount <= 1)
                    {
                        if (!user.IsActive)
                        {
                            throw new InvalidOperationException("Cannot disable the last active administrator");
                        }
                        if (user.Role != "Admin" && user.Role != "Administrator")
                        {
                            throw new InvalidOperationException("Cannot change role of the last active administrator");
                        }
                    }
                }
            }

            int rowsAffected;

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                // Prevent reusing the same password
                if (_passwordHasher.VerifyPassword(newPassword, user.PasswordHash))
                {
                    throw new InvalidOperationException("New password must be different from the current password");
                }

                // Update with new password
                var passwordHash = _passwordHasher.HashPassword(newPassword);
                rowsAffected = await conn.ExecuteAsync(
                    @"UPDATE Users
                      SET Role = @Role, IsActive = @IsActive, DisplayName = @DisplayName, PasswordHash = @PasswordHash
                      WHERE Id = @Id",
                    new
                    {
                        user.Id,
                        user.Role,
                        user.IsActive,
                        user.DisplayName,
                        PasswordHash = passwordHash
                    });

                if (rowsAffected > 0)
                {
                    Log.Information("Updated user {UserId} with new password", user.Id);
                }
            }
            else
            {
                // Update without changing password
                rowsAffected = await conn.ExecuteAsync(
                    @"UPDATE Users
                      SET Role = @Role, IsActive = @IsActive, DisplayName = @DisplayName
                      WHERE Id = @Id",
                    new
                    {
                        user.Id,
                        user.Role,
                        user.IsActive,
                        user.DisplayName
                    });

                if (rowsAffected > 0)
                {
                    Log.Information("Updated user {UserId}", user.Id);
                }
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating user {UserId}", user.Id);
            throw;
        }
    }

    /// <summary>
    /// Change username for an existing user with validation, history tracking, and transaction support
    /// </summary>
    public async Task<bool> ChangeUsernameAsync(int userId, string newUsername, string changedBy, int? changedByUserId, string? ipAddress = null)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Validate username format
            ValidateUsername(newUsername);

            // Normalize new username to lowercase for consistency
            var normalizedUsername = newUsername.ToLowerInvariant().Trim();

            // Start a transaction to ensure atomicity
            using var transaction = conn.BeginTransaction();

            try
            {
                // Get current username before changing
                var currentUsername = await conn.QuerySingleOrDefaultAsync<string>(
                    "SELECT Username FROM Users WHERE Id = @UserId",
                    new { UserId = userId },
                    transaction);

                if (string.IsNullOrEmpty(currentUsername))
                {
                    throw new InvalidOperationException("User not found");
                }

                // Check if username is actually different
                if (currentUsername.Equals(normalizedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("New username must be different from current username");
                }

                // Check if the new username already exists (case-insensitive), excluding the current user
                var exists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Users WHERE LOWER(Username) = @Username AND Id != @UserId",
                    new { Username = normalizedUsername, UserId = userId },
                    transaction);

                if (exists > 0)
                {
                    throw new InvalidOperationException($"Username '{normalizedUsername}' is already taken");
                }

                // Update the username
                var rowsAffected = await conn.ExecuteAsync(
                    "UPDATE Users SET Username = @Username WHERE Id = @Id",
                    new { Username = normalizedUsername, Id = userId },
                    transaction);

                if (rowsAffected == 0)
                {
                    throw new InvalidOperationException("Failed to update username");
                }

                // Record username change in history table
                await conn.ExecuteAsync(
                    @"INSERT INTO UsernameHistory (UserId, OldUsername, NewUsername, ChangedAt, ChangedBy, ChangedByUserId, IpAddress)
                      VALUES (@UserId, @OldUsername, @NewUsername, @ChangedAt, @ChangedBy, @ChangedByUserId, @IpAddress)",
                    new
                    {
                        UserId = userId,
                        OldUsername = currentUsername,
                        NewUsername = normalizedUsername,
                        ChangedAt = DateTime.UtcNow,
                        ChangedBy = changedBy,
                        ChangedByUserId = changedByUserId,
                        IpAddress = ipAddress
                    },
                    transaction);

                // Note: CreatedBy columns now store User IDs (integers), not usernames
                // No backfill needed - the User ID reference remains valid after username change

                // Commit the transaction
                transaction.Commit();

                Log.Information("User {OldUsername} changed username for user {UserId} from '{OldUsername}' to '{NewUsername}'",
                    currentUsername, userId, currentUsername, normalizedUsername);

                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error changing username for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Validate username format and requirements
    /// </summary>
    private void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException("Username cannot be empty");
        }

        var trimmedUsername = username.Trim();

        // Check length
        if (trimmedUsername.Length < 3)
        {
            throw new InvalidOperationException("Username must be at least 3 characters long");
        }

        if (trimmedUsername.Length > 50)
        {
            throw new InvalidOperationException("Username cannot exceed 50 characters");
        }

        // Check format: allow alphanumeric, underscores, hyphens, dots, @ symbol (for email addresses)
        if (!System.Text.RegularExpressions.Regex.IsMatch(trimmedUsername, @"^[a-zA-Z0-9._@-]+$"))
        {
            throw new InvalidOperationException("Username can only contain letters, numbers, underscores, hyphens, dots, and @ symbols");
        }

        // If contains @, validate as email format
        if (trimmedUsername.Contains("@"))
        {
            // Basic email validation
            var emailRegex = new System.Text.RegularExpressions.Regex(@"^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$");
            if (!emailRegex.IsMatch(trimmedUsername))
            {
                throw new InvalidOperationException("Invalid email address format");
            }
        }
        else
        {
            // For non-email usernames, apply the original rules
            // Cannot start or end with dots, underscores, or hyphens
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmedUsername, @"^[._-]|[._-]$"))
            {
                throw new InvalidOperationException("Username cannot start or end with dots, underscores, or hyphens");
            }

            // Cannot have consecutive dots
            if (trimmedUsername.Contains(".."))
            {
                throw new InvalidOperationException("Username cannot contain consecutive dots");
            }
        }
    }

    /// <summary>
    /// Count active administrators (for last admin protection)
    /// </summary>
    public async Task<int> CountActiveAdministratorsAsync()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Users WHERE (Role = 'Admin' OR Role = 'Administrator') AND IsActive = 1");

            return count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error counting active administrators");
            throw;
        }
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    public async Task<bool> DeleteUserAsync(int userId, string deletedBy = "System")
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Soft delete: set IsDeleted flag instead of actually deleting
            var rowsAffected = await conn.ExecuteAsync(
                @"UPDATE Users 
                  SET IsDeleted = 1, 
                      DeletedAt = @DeletedAt, 
                      DeletedBy = @DeletedBy,
                      IsActive = 0
                  WHERE Id = @Id AND IsDeleted = 0",
                new { 
                    Id = userId, 
                    DeletedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    DeletedBy = deletedBy
                });

            if (rowsAffected > 0)
            {
                Log.Information("Soft deleted user {UserId} by {DeletedBy}", userId, deletedBy);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error soft deleting user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Get all API keys
    /// </summary>
    public async Task<IEnumerable<ApiKey>> GetApiKeysAsync()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var keys = await conn.QueryAsync<ApiKey>(
                "SELECT * FROM ApiKeys ORDER BY CreatedAt DESC");
            
            return keys;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving API keys");
            throw;
        }
    }

    /// <summary>
    /// Create a new API key
    /// Returns tuple of (keyId, apiKeyValue)
    /// </summary>
    public async Task<(int KeyId, string ApiKeyValue)> CreateApiKeyAsync(
        string name,
        string permissions,
        DateTime? expiresAt = null)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Generate random API key
            var apiKeyValue = GenerateApiKey();
            var keyHash = _passwordHasher.HashPassword(apiKeyValue);

            // Insert API key
            var keyId = await conn.QuerySingleAsync<int>(
                @"INSERT INTO ApiKeys (Name, KeyHash, Permissions, ExpiresAt, IsActive, CreatedAt) 
                  VALUES (@Name, @KeyHash, @Permissions, @ExpiresAt, 1, @CreatedAt)
                  RETURNING Id",
                new
                {
                    Name = name,
                    KeyHash = keyHash,
                    Permissions = permissions,
                    ExpiresAt = expiresAt,
                    CreatedAt = DateTime.UtcNow
                });

            Log.Information("Created API key {Name} (ID: {KeyId})", name, keyId);

            // Send API key creation notification (fire and forget)
            _notificationService.NotifyApiKeyCreationAsync(name);

            return (keyId, apiKeyValue);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating API key {Name}", name);
            throw;
        }
    }

    /// <summary>
    /// Revoke (delete) an API key
    /// </summary>
    public async Task<bool> RevokeApiKeyAsync(int keyId)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var rowsAffected = await conn.ExecuteAsync(
                "DELETE FROM ApiKeys WHERE Id = @Id",
                new { Id = keyId });

            if (rowsAffected > 0)
            {
                Log.Information("Revoked API key {KeyId}", keyId);
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error revoking API key {KeyId}", keyId);
            throw;
        }
    }

    /// <summary>
    /// Generate a random API key
    /// Format: reef_live_32randomcharacters
    /// </summary>
    private string GenerateApiKey()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var randomPart = new string(Enumerable.Repeat(chars, 32)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        
        return $"reef_live_{randomPart}";
    }

    /// <summary>
    /// Generate a random confirmation string for dangerous operations
    /// Format: word_word_word (e.g., donald_duck_carrot)
    /// </summary>
    public string GenerateConfirmationString()
    {
        var words = new[]
        {
            "apple", "banana", "carrot", "dragon", "elephant", "falcon", "giraffe", "hammer",
            "igloo", "jacket", "kangaroo", "lemon", "mango", "needle", "orange", "penguin",
            "quartz", "rabbit", "sandwich", "turtle", "umbrella", "violet", "walrus", "xylophone",
            "yellow", "zebra", "anchor", "bridge", "castle", "diamond", "eclipse", "forest",
            "sinister", "vecna", "whitestone", "bridge", "flayer", "wheeler", "stranger", "billy",
            "galaxy", "horizon", "island", "jungle", "knight", "lantern", "mountain", "nebula",
            "ocean", "palace", "quantum", "river", "sunset", "thunder", "universe", "volcano",
            "whisper", "crystal", "meadow", "phoenix", "shadow", "starlight", "waterfall"
        };

        var random = new Random();
        var word1 = words[random.Next(words.Length)];
        var word2 = words[random.Next(words.Length)];
        var word3 = words[random.Next(words.Length)];
        
        return $"{word1}_{word2}_{word3}";
    }

    /// <summary>
    /// Reset the database completely
    /// Deletes the database file and recreates it with initial data
    /// </summary>
    public async Task<bool> ResetDatabaseAsync()
    {
        try
        {
            var dbPath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
            
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                Log.Warning("Database file not found at {DbPath}", dbPath);
                return false;
            }

            Log.Warning("DANGER: Resetting database at {DbPath}", dbPath);
            
            // Close any existing connections
            SqliteConnection.ClearAllPools();
            
            // Delete the database file
            File.Delete(dbPath);
            Log.Information("Database file deleted successfully");
            
            // Recreate the database
            var dbInitializer = new DatabaseInitializer(_connectionString, _encryptionService);
            await dbInitializer.InitializeAsync();
            await dbInitializer.SeedSampleDataAsync();
            
            Log.Information("Database reset and reseeded successfully");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting database");
            throw;
        }
    }

    /// <summary>
    /// Clear audit logs older than specified days
    /// Minimum retention period is 90 days
    /// </summary>
    public async Task<int> ClearOldAuditLogsAsync(int retentionDays)
    {
        try
        {
            if (retentionDays < 90)
            {
                throw new InvalidOperationException("Cannot delete audit logs newer than 90 days. Minimum retention is 90 days.");
            }

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var deletedCount = await conn.ExecuteAsync(
                "DELETE FROM AuditLog WHERE Timestamp < @CutoffDate",
                new { CutoffDate = cutoffDate });

            Log.Information("Deleted {Count} audit log entries older than {Days} days", deletedCount, retentionDays);
            return deletedCount;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing old audit logs");
            throw;
        }
    }

    /// <summary>
    /// Clear execution logs older than specified days
    /// </summary>
    public async Task<int> ClearOldExecutionLogsAsync(int retentionDays)
    {
        try
        {
            if (retentionDays < 1)
            {
                throw new InvalidOperationException("Retention days must be at least 1");
            }

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
            var deletedCount = await conn.ExecuteAsync(
                "DELETE FROM ProfileExecutions WHERE StartedAt < @CutoffDate",
                new { CutoffDate = cutoffDate });

            Log.Information("Deleted {Count} execution log entries older than {Days} days", deletedCount, retentionDays);
            return deletedCount;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing old execution logs");
            throw;
        }
    }

    /// <summary>
    /// Clear all execution logs
    /// </summary>
    public async Task<int> ClearAllExecutionLogsAsync()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // First get the count
            var totalCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM ProfileExecutions");

            // Then delete all
            await conn.ExecuteAsync("DELETE FROM ProfileExecutions");

            Log.Warning("DANGER: Deleted ALL execution logs ({Count} entries)", totalCount);
            return totalCount;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error clearing all execution logs");
            throw;
        }
    }

    /// <summary>
    /// Get system notification settings
    /// </summary>
    public async Task<NotificationSettings?> GetNotificationSettingsAsync()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            const string sql = "SELECT * FROM NotificationSettings LIMIT 1";
            var result = await conn.QueryFirstOrDefaultAsync<NotificationSettings>(sql);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving notification settings");
            return null;
        }
    }

    /// <summary>
    /// Update system notification settings
    /// Creates new settings if none exist
    /// </summary>
    public async Task<bool> UpdateNotificationSettingsAsync(NotificationSettings settings)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Check if settings exist
            const string checkSql = "SELECT COUNT(*) FROM NotificationSettings";
            var count = await conn.ExecuteScalarAsync<int>(checkSql);

            // Compute hash for tamper detection
            settings.Hash = Reef.Helpers.HashHelper.ComputeDestinationHash(
                settings.Id.ToString(),
                settings.DestinationId.ToString(),
                System.Text.Json.JsonSerializer.Serialize(settings));

            settings.UpdatedAt = DateTime.UtcNow;

            if (count > 0)
            {
                // Update existing
                const string updateSql = @"
                    UPDATE NotificationSettings SET
                        IsEnabled = @IsEnabled,
                        DestinationId = @DestinationId,
                        DestinationName = @DestinationName,
                        NotifyOnJobFailure = @NotifyOnJobFailure,
                        NotifyOnJobSuccess = @NotifyOnJobSuccess,
                        NotifyOnProfileFailure = @NotifyOnProfileFailure,
                        NotifyOnProfileSuccess = @NotifyOnProfileSuccess,
                        NotifyOnImportProfileFailure = @NotifyOnImportProfileFailure,
                        NotifyOnImportProfileSuccess = @NotifyOnImportProfileSuccess,
                        NotifyOnDatabaseSizeThreshold = @NotifyOnDatabaseSizeThreshold,
                        DatabaseSizeThresholdBytes = @DatabaseSizeThresholdBytes,
                        NotifyOnNewUser = @NotifyOnNewUser,
                        NotifyOnNewApiKey = @NotifyOnNewApiKey,
                        NotifyOnNewWebhook = @NotifyOnNewWebhook,
                        NotifyOnNewEmailApproval = @NotifyOnNewEmailApproval,
                        NewEmailApprovalCooldownHours = @NewEmailApprovalCooldownHours,
                        EnableCTA = @EnableCTA,
                        CTAUrl = @CTAUrl,
                        RecipientEmails = @RecipientEmails,
                        UpdatedAt = @UpdatedAt,
                        Hash = @Hash
                    WHERE Id = (SELECT Id FROM NotificationSettings LIMIT 1)";

                await conn.ExecuteAsync(updateSql, settings);
            }
            else
            {
                // Insert new
                const string insertSql = @"
                    INSERT INTO NotificationSettings (
                        IsEnabled, DestinationId, DestinationName,
                        NotifyOnJobFailure, NotifyOnJobSuccess,
                        NotifyOnProfileFailure, NotifyOnProfileSuccess,
                        NotifyOnImportProfileFailure, NotifyOnImportProfileSuccess,
                        NotifyOnDatabaseSizeThreshold, DatabaseSizeThresholdBytes,
                        NotifyOnNewUser, NotifyOnNewApiKey, NotifyOnNewWebhook,
                        NotifyOnNewEmailApproval, NewEmailApprovalCooldownHours,
                        EnableCTA, CTAUrl,
                        RecipientEmails, CreatedAt, UpdatedAt, Hash)
                    VALUES (
                        @IsEnabled, @DestinationId, @DestinationName,
                        @NotifyOnJobFailure, @NotifyOnJobSuccess,
                        @NotifyOnProfileFailure, @NotifyOnProfileSuccess,
                        @NotifyOnImportProfileFailure, @NotifyOnImportProfileSuccess,
                        @NotifyOnDatabaseSizeThreshold, @DatabaseSizeThresholdBytes,
                        @NotifyOnNewUser, @NotifyOnNewApiKey, @NotifyOnNewWebhook,
                        @NotifyOnNewEmailApproval, @NewEmailApprovalCooldownHours,
                        @EnableCTA, @CTAUrl,
                        @RecipientEmails, @CreatedAt, @UpdatedAt, @Hash)";

                await conn.ExecuteAsync(insertSql, settings);
            }

            Log.Information("Updated notification settings");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating notification settings");
            return false;
        }
    }
}