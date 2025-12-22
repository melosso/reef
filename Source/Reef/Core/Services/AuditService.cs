// Source/Reef/Core/Services/AuditService.cs
// Service for tracking and logging all system changes

using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Services;

/// <summary>
/// Service for audit logging
/// Tracks all entity changes for compliance and security
/// </summary>
public class AuditService
{
    private readonly string _connectionString;

    public AuditService(DatabaseConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    /// <summary>
    /// Log an audit event
    /// </summary>
    /// <param name="entityType">Type of entity (Connection, Profile, User, etc.)</param>
    /// <param name="entityId">ID of the entity</param>
    /// <param name="action">Action performed (Created, Updated, Deleted, Executed, etc.)</param>
    /// <param name="performedBy">Username who performed the action</param>
    /// <param name="changes">JSON string of changes made (optional)</param>
    /// <param name="context">Optional HTTP context for IP address tracking</param>
    public async Task LogAsync(
        string entityType,
        int entityId,
        string action,
        string performedBy,
        string? changes = null,
        HttpContext? context = null)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var ipAddress = context?.Connection?.RemoteIpAddress?.ToString();

            await conn.ExecuteAsync(
                @"INSERT INTO AuditLog (EntityType, EntityId, Action, PerformedBy, Changes, IpAddress, Timestamp) 
                  VALUES (@EntityType, @EntityId, @Action, @PerformedBy, @Changes, @IpAddress, @Timestamp)",
                new
                {
                    EntityType = entityType,
                    EntityId = entityId,
                    Action = action,
                    PerformedBy = performedBy,
                    Changes = changes,
                    IpAddress = ipAddress,
                    Timestamp = DateTime.UtcNow
                });

            Log.Debug("Audit log: {EntityType} {EntityId} {Action} by {PerformedBy}",
                entityType, entityId, action, performedBy);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error logging audit event for {EntityType} {EntityId}",
                entityType, entityId);
            // Don't throw - audit logging should not break normal operations
        }
    }

    /// <summary>
    /// Log an audit event with structured changes object
    /// </summary>
    public async Task LogAsync(
        string entityType,
        int entityId,
        string action,
        string performedBy,
        object? changesObject = null,
        HttpContext? context = null)
    {
        string? changes = null;
        if (changesObject != null)
        {
            try
            {
                changes = JsonSerializer.Serialize(changesObject);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to serialize audit changes object");
            }
        }

        await LogAsync(entityType, entityId, action, performedBy, changes, context);
    }

    /// <summary>
    /// Get audit logs for a specific entity
    /// </summary>
    public async Task<IEnumerable<dynamic>> GetEntityLogsAsync(string entityType, int entityId, int limit = 50)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var logs = await conn.QueryAsync<dynamic>(
                @"SELECT * FROM AuditLog 
                  WHERE EntityType = @EntityType AND EntityId = @EntityId 
                  ORDER BY Timestamp DESC 
                  LIMIT @Limit",
                new { EntityType = entityType, EntityId = entityId, Limit = limit });

            return logs;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving audit logs for {EntityType} {EntityId}",
                entityType, entityId);
            return Enumerable.Empty<dynamic>();
        }
    }

    /// <summary>
    /// Get recent audit logs
    /// </summary>
    public async Task<IEnumerable<dynamic>> GetRecentLogsAsync(int limit = 100)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var logs = await conn.QueryAsync<dynamic>(
                "SELECT * FROM AuditLog ORDER BY Timestamp DESC LIMIT @Limit",
                new { Limit = limit });

            return logs;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving recent audit logs");
            return Enumerable.Empty<dynamic>();
        }
    }

    /// <summary>
    /// Get audit logs by user
    /// </summary>
    public async Task<IEnumerable<dynamic>> GetUserLogsAsync(string username, int limit = 100)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var logs = await conn.QueryAsync<dynamic>(
                @"SELECT * FROM AuditLog 
                  WHERE PerformedBy = @Username 
                  ORDER BY Timestamp DESC 
                  LIMIT @Limit",
                new { Username = username, Limit = limit });

            return logs;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrieving audit logs for user {Username}", username);
            return Enumerable.Empty<dynamic>();
        }
    }

    /// <summary>
    /// Delete old audit logs (retention policy)
    /// </summary>
    public async Task<int> DeleteOldLogsAsync(int retentionDays = 90)
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            var rowsDeleted = await conn.ExecuteAsync(
                "DELETE FROM AuditLog WHERE Timestamp < @CutoffDate",
                new { CutoffDate = cutoffDate });

            if (rowsDeleted > 0)
            {
                Log.Information("Deleted {Count} audit logs older than {Days} days",
                    rowsDeleted, retentionDays);
            }

            return rowsDeleted;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting old audit logs");
            return 0;
        }
    }
}