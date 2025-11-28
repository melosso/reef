using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Security;
using Serilog;
using System.Data.Common;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing database connections
/// </summary>
public class ConnectionService
{
    private readonly string _connectionString;
    private readonly EncryptionService _encryptionService;
    private readonly HashValidator _hashValidator;

    public ConnectionService(
        DatabaseConfig config,
        EncryptionService encryptionService,
        HashValidator hashValidator)
    {
        _connectionString = config.ConnectionString;
        _encryptionService = encryptionService;
        _hashValidator = hashValidator;
    }

    /// <summary>
    /// Get all connections
    /// </summary>
    public async Task<List<Connection>> GetAllAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM Connections ORDER BY Name";
        var connections = await connection.QueryAsync<Connection>(sql);
        return connections.ToList();
    }

    /// <summary>
    /// Get connection by ID
    /// </summary>
    public async Task<Connection?> GetByIdAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM Connections WHERE Id = @Id";
        return await connection.QueryFirstOrDefaultAsync<Connection>(sql, new { Id = id });
    }

    /// <summary>
    /// Get connection by name
    /// </summary>
    public async Task<Connection?> GetByNameAsync(string name)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM Connections WHERE Name = @Name";
        return await connection.QueryFirstOrDefaultAsync<Connection>(sql, new { Name = name });
    }

    /// <summary>
    /// Create a new connection
    /// </summary>
    public async Task<int> CreateAsync(Connection conn, string createdBy)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Encrypt connection string if not already encrypted
        if (!_encryptionService.IsEncrypted(conn.ConnectionString))
        {
            conn.ConnectionString = _encryptionService.Encrypt(conn.ConnectionString);
        }

        // Compute hash for tamper detection
        conn.Hash = _hashValidator.ComputeHash(new
        {
            conn.Name,
            conn.Type,
            conn.ConnectionString,
            conn.Tags
        });

        conn.CreatedBy = createdBy;
        conn.CreatedAt = DateTime.UtcNow;
        conn.UpdatedAt = DateTime.UtcNow;

        const string sql = @"
            INSERT INTO Connections (Name, Type, ConnectionString, IsActive, Tags, Hash, CreatedAt, UpdatedAt, CreatedBy)
            VALUES (@Name, @Type, @ConnectionString, @IsActive, @Tags, @Hash, @CreatedAt, @UpdatedAt, @CreatedBy);
            SELECT last_insert_rowid();
        ";

        var id = await connection.ExecuteScalarAsync<int>(sql, conn);
        Log.Information("Created connection {Name} (ID: {Id})", conn.Name, id);
        return id;
    }

    /// <summary>
    /// Update an existing connection
    /// </summary>
    public async Task<bool> UpdateAsync(Connection conn)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Encrypt connection string if not already encrypted
        if (!_encryptionService.IsEncrypted(conn.ConnectionString))
        {
            conn.ConnectionString = _encryptionService.Encrypt(conn.ConnectionString);
        }

        // Recompute hash
        conn.Hash = _hashValidator.ComputeHash(new
        {
            conn.Name,
            conn.Type,
            conn.ConnectionString,
            conn.Tags
        });

        conn.UpdatedAt = DateTime.UtcNow;

        const string sql = @"
            UPDATE Connections
            SET Name = @Name, Type = @Type, ConnectionString = @ConnectionString,
                IsActive = @IsActive, Tags = @Tags, Hash = @Hash, UpdatedAt = @UpdatedAt
            WHERE Id = @Id
        ";

        var rowsAffected = await connection.ExecuteAsync(sql, conn);
        Log.Information("Updated connection {Name} (ID: {Id})", conn.Name, conn.Id);
        return rowsAffected > 0;
    }

    /// <summary>
    /// Delete a connection
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "DELETE FROM Connections WHERE Id = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
        Log.Information("Deleted connection (ID: {Id})", id);
        return rowsAffected > 0;
    }

    /// <summary>
    /// Create a database connection for production use by connection ID
    /// Includes enhanced error handling for decryption failures
    /// </summary>
    public async Task<DbConnection> CreateDatabaseConnectionAsync(int connectionId)
    {
        var conn = await GetByIdAsync(connectionId);
        if (conn == null)
        {
            throw new InvalidOperationException($"Connection with ID {connectionId} not found");
        }

        if (!conn.IsActive)
        {
            throw new InvalidOperationException($"Connection '{conn.Name}' is not active");
        }

        try
        {
            return CreateDatabaseConnection(conn.Type, conn.ConnectionString);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Log.Error(ex, "Decryption failed for connection {ConnectionId} ({ConnectionName}). " +
                         "Possible causes: encryption key mismatch, corrupted connection string, or tampered data.",
                connectionId, conn.Name);

            throw new InvalidOperationException(
                $"Failed to decrypt connection string for '{conn.Name}' (ID: {connectionId}). " +
                "This may indicate a security issue or encryption key mismatch. " +
                "Please verify the REEF_ENCRYPTION_KEY environment variable and connection integrity.",
                ex);
        }
    }

    /// <summary>
    /// Create a database connection for production use by connection name
    /// Includes enhanced error handling for decryption failures
    /// </summary>
    public async Task<DbConnection> CreateDatabaseConnectionAsync(string connectionName)
    {
        var conn = await GetByNameAsync(connectionName);
        if (conn == null)
        {
            throw new InvalidOperationException($"Connection '{connectionName}' not found");
        }

        if (!conn.IsActive)
        {
            throw new InvalidOperationException($"Connection '{conn.Name}' is not active");
        }

        try
        {
            return CreateDatabaseConnection(conn.Type, conn.ConnectionString);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Log.Error(ex, "Decryption failed for connection '{ConnectionName}'. " +
                         "Possible causes: encryption key mismatch, corrupted connection string, or tampered data.",
                conn.Name);

            throw new InvalidOperationException(
                $"Failed to decrypt connection string for '{conn.Name}'. " +
                "This may indicate a security issue or encryption key mismatch. " +
                "Please verify the REEF_ENCRYPTION_KEY environment variable and connection integrity.",
                ex);
        }
    }

    /// <summary>
    /// Create a database connection with Application Name set to 'Reef'
    /// </summary>
    private DbConnection CreateDatabaseConnection(string type, string connectionString)
    {
        // Decrypt if encrypted
        if (_encryptionService.IsEncrypted(connectionString))
        {
            connectionString = _encryptionService.Decrypt(connectionString);
        }

        return type switch
        {
            "SqlServer" => new Microsoft.Data.SqlClient.SqlConnection(
                new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
                {
                    ApplicationName = "Reef"
                }.ConnectionString),
            
            "MySQL" => new MySqlConnector.MySqlConnection(
                new MySqlConnector.MySqlConnectionStringBuilder(connectionString)
                {
                    ApplicationName = "Reef"
                }.ConnectionString),
            
            "PostgreSQL" => new Npgsql.NpgsqlConnection(
                new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
                {
                    ApplicationName = "Reef"
                }.ConnectionString),
            
            _ => throw new NotSupportedException($"Database type {type} is not supported")
        };
    }

    /// <summary>
    /// Test a connection
    /// </summary>
    public async Task<(bool Success, string? Message, long ResponseTimeMs)> TestConnectionAsync(string type, string connectionString)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            DbConnection testConnection = CreateDatabaseConnection(type, connectionString);

            await using (testConnection)
            {
                await testConnection.OpenAsync();
                stopwatch.Stop();
                return (true, "Connection successful", stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Warning(ex, "Connection test failed for type {Type}", type);
            return (false, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Update last test result
    /// </summary>
    public async Task UpdateTestResultAsync(int id, bool success, string? message = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            UPDATE Connections
            SET LastTestedAt = datetime('now'), LastTestResult = @Result
            WHERE Id = @Id
        ";

        var result = success ? "Success" : $"Failed: {message}";
        await connection.ExecuteAsync(sql, new { Id = id, Result = result });
    }
}