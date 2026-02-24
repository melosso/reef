using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Reef.Core.Models;
using Serilog;
using System.Data;
using System.Diagnostics;

namespace Reef.Core.Services;

/// <summary>
/// Service for executing SQL queries against different database types
/// Supports SQL Server, MySQL, and PostgreSQL with parameter substitution
/// </summary>
public class QueryExecutor
{
    private readonly EncryptionService _encryptionService;
    private const int DefaultCommandTimeout = 30; // seconds

    public QueryExecutor(EncryptionService encryptionService)
    {
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Execute a SQL query against a database connection with retry logic for transient errors
    /// </summary>
    /// <param name="connection">Database connection configuration</param>
    /// <param name="query">SQL query to execute</param>
    /// <param name="parameters">Optional parameter values for substitution</param>
    /// <param name="commandTimeout">Query timeout in seconds (default: 30)</param>
    /// <param name="maxRetries">Maximum retry attempts for transient errors (default: 2)</param>
    /// <returns>Tuple containing success status, result rows, error message, and execution time</returns>
    public async Task<(bool Success, List<Dictionary<string, object>> Rows, string? ErrorMessage, long ExecutionTimeMs)>
        ExecuteQueryAsync(Connection connection, string query, Dictionary<string, string>? parameters = null, int commandTimeout = DefaultCommandTimeout, int maxRetries = 2)
    {
        return await ExecuteQueryWithRetryAsync(connection, query, parameters, commandTimeout, maxRetries);
    }

    /// <summary>
    /// Internal method with retry logic for transient database errors
    /// </summary>
    private async Task<(bool Success, List<Dictionary<string, object>> Rows, string? ErrorMessage, long ExecutionTimeMs)>
        ExecuteQueryWithRetryAsync(Connection connection, string query, Dictionary<string, string>? parameters, int commandTimeout, int maxRetries)
    {
        int attempt = 0;
        List<Exception> exceptions = new();
        var totalStopwatch = Stopwatch.StartNew();

        while (attempt <= maxRetries)
        {
            try
            {
                var result = await ExecuteQueryCoreAsync(connection, query, parameters, commandTimeout);

                if (result.Success)
                {
                    if (attempt > 0)
                    {
                        Log.Information("Query succeeded on attempt {Attempt} for connection {ConnectionName}",
                            attempt + 1, connection.Name);
                    }
                    return result;
                }

                // Non-success but no exception - return immediately
                return result;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (IsTransientSqlError(ex))
            {
                exceptions.Add(ex);
                Log.Warning("Transient SQL error on attempt {Attempt} for connection {ConnectionName}: {ErrorMessage}",
                    attempt + 1, connection.Name, ex.Message);

                if (attempt < maxRetries)
                {
                    var delaySeconds = 2 * (attempt + 1); // 2s, 4s, 6s
                    Log.Information("Retrying query in {DelaySeconds}s (attempt {Attempt}/{MaxRetries})",
                        delaySeconds, attempt + 1, maxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    attempt++;
                    continue;
                }
            }
            catch (MySqlConnector.MySqlException ex) when (IsTransientMySqlError(ex))
            {
                exceptions.Add(ex);
                Log.Warning("Transient MySQL error on attempt {Attempt} for connection {ConnectionName}: {ErrorMessage}",
                    attempt + 1, connection.Name, ex.Message);

                if (attempt < maxRetries)
                {
                    var delaySeconds = 2 * (attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    attempt++;
                    continue;
                }
            }
            catch (Npgsql.NpgsqlException ex) when (IsTransientPostgreSqlError(ex))
            {
                exceptions.Add(ex);
                Log.Warning("Transient PostgreSQL error on attempt {Attempt} for connection {ConnectionName}: {ErrorMessage}",
                    attempt + 1, connection.Name, ex.Message);

                if (attempt < maxRetries)
                {
                    var delaySeconds = 2 * (attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    attempt++;
                    continue;
                }
            }
            catch (Exception ex)
            {
                // Non-transient errors fail immediately
                totalStopwatch.Stop();
                Log.Error("Non-recoverable query execution error on connection {ConnectionName}: {ErrorMessage}", connection.Name, ex.Message);
                return (false, new List<Dictionary<string, object>>(), ex.Message, totalStopwatch.ElapsedMilliseconds);
            }

            attempt++;
        }

        // All retries exhausted
        totalStopwatch.Stop();
        var aggregateError = $"Query failed after {maxRetries + 1} attempts. Errors: {string.Join("; ", exceptions.Select(e => e.Message))}";
        Log.Error("Query execution failed after all retries: {Error}", aggregateError);
        return (false, new List<Dictionary<string, object>>(), aggregateError, totalStopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Core query execution logic (single attempt, no retry)
    /// </summary>
    private async Task<(bool Success, List<Dictionary<string, object>> Rows, string? ErrorMessage, long ExecutionTimeMs)>
        ExecuteQueryCoreAsync(Connection connection, string query, Dictionary<string, string>? parameters, int commandTimeout)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Decrypt connection string
            string connectionString;
            try
            {
                connectionString = _encryptionService.Decrypt(connection.ConnectionString);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to decrypt connection string for connection {ConnectionId}: {ErrorMessage}", connection.Id, ex.Message);
                return (false, new List<Dictionary<string, object>>(), "Failed to decrypt connection string", stopwatch.ElapsedMilliseconds);
            }

            // Substitute parameters in query
            if (parameters != null && parameters.Count > 0)
            {
                query = SubstituteParameters(query, parameters);
            }

            Log.Information("Executing query on {ConnectionType} connection {ConnectionName}", connection.Type, connection.Name);
            Log.Debug("Query: {Query}", query);

            List<Dictionary<string, object>> rows;

            switch (connection.Type.ToLowerInvariant())
            {
                case "sqlserver":
                    rows = await ExecuteSqlServerQueryAsync(connectionString, query, commandTimeout);
                    break;

                case "mysql":
                    rows = await ExecuteMySqlQueryAsync(connectionString, query, commandTimeout);
                    break;

                case "postgresql":
                case "postgres":
                    rows = await ExecutePostgreSqlQueryAsync(connectionString, query, commandTimeout);
                    break;

                default:
                    return (false, new List<Dictionary<string, object>>(),
                        $"Unsupported database type: {connection.Type}", stopwatch.ElapsedMilliseconds);
            }

            stopwatch.Stop();
            Log.Information("Query executed successfully. Rows returned: {RowCount}, Time: {ExecutionTimeMs}ms",
                rows.Count, stopwatch.ElapsedMilliseconds);

            return (true, rows, null, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error("Error executing query on connection {ConnectionName}: {Error}", connection.Name, ex.Message);
            throw; // Re-throw for retry logic to handle
        }
    }

    /// <summary>
    /// Detect transient SQL Server errors that should be retried
    /// </summary>
    private bool IsTransientSqlError(Microsoft.Data.SqlClient.SqlException ex)
    {
        // SQL Server transient error codes
        // -2: Timeout
        // 1205: Deadlock victim
        // 1204: Lock issue
        // 40197, 40501, 40613: Azure SQL transient errors
        // 49918, 49919, 49920: Azure SQL resource errors
        var transientErrors = new[] { -2, 1205, 1204, 40197, 40501, 40613, 49918, 49919, 49920 };
        return transientErrors.Contains(ex.Number);
    }

    /// <summary>
    /// Detect transient MySQL errors that should be retried
    /// </summary>
    private bool IsTransientMySqlError(MySqlConnector.MySqlException ex)
    {
        // MySQL transient error codes
        // 1205: Lock wait timeout
        // 1213: Deadlock
        // 2006: Server has gone away
        // 2013: Lost connection during query
        var transientErrors = new[] { 1205, 1213, 2006, 2013 };
        return transientErrors.Contains(ex.Number);
    }

    /// <summary>
    /// Detect transient PostgreSQL errors that should be retried
    /// </summary>
    private bool IsTransientPostgreSqlError(Npgsql.NpgsqlException ex)
    {
        // PostgreSQL transient error codes (SQLSTATE)
        // 40001: Serialization failure
        // 40P01: Deadlock detected
        // 53300: Too many connections
        // 57P03: Cannot connect now
        var transientSqlStates = new[] { "40001", "40P01", "53300", "57P03" };
        return ex.SqlState != null && transientSqlStates.Contains(ex.SqlState);
    }

    /// <summary>
    /// Execute a non-query command (UPDATE, INSERT, DELETE, stored procedure) against a database connection
    /// Returns number of rows affected instead of result rows
    /// </summary>
    /// <param name="connection">Database connection configuration</param>
    /// <param name="command">SQL command to execute</param>
    /// <param name="parameters">Optional parameter values for substitution</param>
    /// <param name="commandTimeout">Command timeout in seconds (default: 30)</param>
    /// <returns>Tuple containing success status, rows affected, error message, and execution time</returns>
    public async Task<(bool Success, int RowsAffected, string? ErrorMessage, long ExecutionTimeMs)> 
        ExecuteCommandAsync(Connection connection, string command, Dictionary<string, string>? parameters = null, int commandTimeout = DefaultCommandTimeout)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Decrypt connection string
            string connectionString;
            try
            {
                connectionString = _encryptionService.Decrypt(connection.ConnectionString);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to decrypt connection string for connection {ConnectionId}: {ErrorMessage}", connection.Id, ex.Message);
                return (false, 0, "Failed to decrypt connection string", stopwatch.ElapsedMilliseconds);
            }

            // Substitute parameters in command
            if (parameters != null && parameters.Count > 0)
            {
                command = SubstituteParameters(command, parameters);
            }

            Log.Information("Executing command on {ConnectionType} connection {ConnectionName}", connection.Type, connection.Name);
            Log.Debug("Command: {Command}", command);

            int rowsAffected;

            switch (connection.Type.ToLowerInvariant())
            {
                case "sqlserver":
                    rowsAffected = await ExecuteSqlServerCommandAsync(connectionString, command, commandTimeout);
                    break;
                
                case "mysql":
                    rowsAffected = await ExecuteMySqlCommandAsync(connectionString, command, commandTimeout);
                    break;
                
                case "postgresql":
                case "postgres":
                    rowsAffected = await ExecutePostgreSqlCommandAsync(connectionString, command, commandTimeout);
                    break;
                
                default:
                    return (false, 0, $"Unsupported database type: {connection.Type}", stopwatch.ElapsedMilliseconds);
            }

            stopwatch.Stop();
            Log.Information("Command executed successfully. Rows affected: {RowsAffected}, Time: {ExecutionTimeMs}ms", 
                rowsAffected, stopwatch.ElapsedMilliseconds);

            return (true, rowsAffected, null, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error("Error executing command on connection {ConnectionName}: {Error}", connection.Name, ex.Message);
            return (false, 0, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Execute command against SQL Server using ExecuteAsync
    /// </summary>
    private async Task<int> ExecuteSqlServerCommandAsync(
        string connectionString, string command, int commandTimeout)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "Reef"
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        
        return await connection.ExecuteAsync(command, commandTimeout: commandTimeout);
    }

    /// <summary>
    /// Execute command against MySQL using ExecuteAsync
    /// </summary>
    private async Task<int> ExecuteMySqlCommandAsync(
        string connectionString, string command, int commandTimeout)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "Reef"
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        
        return await connection.ExecuteAsync(command, commandTimeout: commandTimeout);
    }

    /// <summary>
    /// Execute command against PostgreSQL using ExecuteAsync
    /// </summary>
    private async Task<int> ExecutePostgreSqlCommandAsync(
        string connectionString, string command, int commandTimeout)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "Reef"
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        
        return await connection.ExecuteAsync(command, commandTimeout: commandTimeout);
    }

    /// <summary>
    /// Execute query against SQL Server
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExecuteSqlServerQueryAsync(
        string connectionString, string query, int commandTimeout)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "Reef"
        };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        
        return await ExecuteQueryWithDapper(connection, query, commandTimeout);
    }

    /// <summary>
    /// Execute query against MySQL
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExecuteMySqlQueryAsync(
        string connectionString, string query, int commandTimeout)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "Reef"
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        
        return await ExecuteQueryWithDapper(connection, query, commandTimeout);
    }

    /// <summary>
    /// Execute query against PostgreSQL
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExecutePostgreSqlQueryAsync(
        string connectionString, string query, int commandTimeout)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = "Reef"
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        
        return await ExecuteQueryWithDapper(connection, query, commandTimeout);
    }

    /// <summary>
    /// Execute query using Dapper and convert results to dictionary list
    /// </summary>
    private async Task<List<Dictionary<string, object>>> ExecuteQueryWithDapper(
        IDbConnection connection, string query, int commandTimeout)
    {
        var result = await connection.QueryAsync(query, commandTimeout: commandTimeout);
        
        var rows = new List<Dictionary<string, object>>();
        
        foreach (var row in result)
        {
            var dict = new Dictionary<string, object>();
            var dapperRow = row as IDictionary<string, object>;
            
            if (dapperRow != null)
            {
                foreach (var kvp in dapperRow)
                {
                    // Convert DBNull to null
                    dict[kvp.Key] = kvp.Value == DBNull.Value ? null! : kvp.Value;
                }
            }
            
            rows.Add(dict);
        }
        
        return rows;
    }

    /// <summary>
    /// Substitute parameters in query
    /// Replaces @ParamName with provided parameter values
    /// </summary>
    private string SubstituteParameters(string query, Dictionary<string, string> parameters)
    {
        foreach (var param in parameters)
        {
            // Handle both @ParamName and {ParamName} formats
            var paramKey = param.Key.StartsWith("@") ? param.Key : "@" + param.Key;
            var paramValue = param.Value;
            
            // Basic SQL injection prevention: escape single quotes
            paramValue = paramValue.Replace("'", "''");
            
            // Replace parameter in query
            query = query.Replace(paramKey, $"'{paramValue}'", StringComparison.OrdinalIgnoreCase);
            query = query.Replace("{" + param.Key + "}", $"'{paramValue}'", StringComparison.OrdinalIgnoreCase);
        }
        
        return query;
    }

    /// <summary>
    /// Test database connection
    /// </summary>
    public async Task<(bool Success, string? Message, long ResponseTimeMs)> TestConnectionAsync(Connection connection)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Decrypt connection string
            string connectionString;
            if (connection.ConnectionString.StartsWith("PWENC:"))
            {
                try
                {
                    connectionString = _encryptionService.Decrypt(connection.ConnectionString.Substring(6));
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to decrypt connection string for connection {ConnectionId}: {ErrorMessage}", connection.Id, ex.Message);
                    return (false, "Failed to decrypt connection string", stopwatch.ElapsedMilliseconds);
                }
            }
            else
            {
                connectionString = connection.ConnectionString;
            }

            switch (connection.Type.ToLowerInvariant())
            {
                case "sqlserver":
                    {
                        var builder = new SqlConnectionStringBuilder(connectionString)
                        {
                            ApplicationName = "Reef"
                        };
                        await using var conn = new SqlConnection(builder.ConnectionString);
                        await conn.OpenAsync();
                        await conn.ExecuteAsync("SELECT 1");
                    }
                    break;
                
                case "mysql":
                    {
                        var builder = new MySqlConnectionStringBuilder(connectionString)
                        {
                            ApplicationName = "Reef"
                        };
                        await using var conn = new MySqlConnection(builder.ConnectionString);
                        await conn.OpenAsync();
                        await conn.ExecuteAsync("SELECT 1");
                    }
                    break;
                
                case "postgresql":
                case "postgres":
                    {
                        var builder = new NpgsqlConnectionStringBuilder(connectionString)
                        {
                            ApplicationName = "Reef"
                        };
                        await using var conn = new NpgsqlConnection(builder.ConnectionString);
                        await conn.OpenAsync();
                        await conn.ExecuteAsync("SELECT 1");
                    }
                    break;
                
                default:
                    return (false, $"Unsupported database type: {connection.Type}", stopwatch.ElapsedMilliseconds);
            }

            stopwatch.Stop();
            Log.Information("Connection test successful for {ConnectionName} ({Type}). Response time: {ResponseTimeMs}ms", 
                connection.Name, connection.Type, stopwatch.ElapsedMilliseconds);

            return (true, "Connection successful", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error("Connection test failed for {ConnectionName}: {Error}", connection.Name, ex.Message);
            return (false, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }
}