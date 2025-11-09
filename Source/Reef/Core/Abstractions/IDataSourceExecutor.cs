using Reef.Core.Models;

namespace Reef.Core.Abstractions;

/// <summary>
/// Interface for executing data source queries and retrieving data
/// Implemented by specific source executors (REST, S3, FTP, Database, etc.)
/// </summary>
public interface IDataSourceExecutor
{
    /// <summary>
    /// Gets the type of data source this executor handles
    /// </summary>
    DataSourceType SourceType { get; }

    /// <summary>
    /// Executes a data source query and returns rows
    /// </summary>
    /// <param name="sourceUri">The source location (API URL, S3 path, FTP path, etc.)</param>
    /// <param name="sourceConfig">JSON configuration for the source</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of rows as dictionaries</returns>
    Task<List<Dictionary<string, object>>> ExecuteAsync(
        string sourceUri,
        string? sourceConfig,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the source is accessible and configured correctly
    /// </summary>
    /// <param name="sourceUri">The source location</param>
    /// <param name="sourceConfig">JSON configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with error message if validation fails</returns>
    Task<ValidationResult> ValidateAsync(
        string sourceUri,
        string? sourceConfig,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for writing data to destinations
/// Implemented by specific writers (Database, File, S3, etc.)
/// </summary>
public interface IDataWriter
{
    /// <summary>
    /// Gets the type of destination this writer handles
    /// </summary>
    ImportDestinationType DestinationType { get; }

    /// <summary>
    /// Writes data rows to the destination
    /// </summary>
    /// <param name="destinationUri">The destination location (table name, file path, S3 path, etc.)</param>
    /// <param name="destinationConfig">JSON configuration for the destination</param>
    /// <param name="rows">Data rows to write</param>
    /// <param name="fieldMappings">Field mapping information</param>
    /// <param name="mode">Write mode: Insert or Upsert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Write result with row counts</returns>
    Task<WriteResult> WriteAsync(
        string destinationUri,
        string? destinationConfig,
        List<Dictionary<string, object>> rows,
        List<FieldMapping> fieldMappings,
        WriteMode mode = WriteMode.Insert,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the destination is accessible and configured correctly
    /// </summary>
    /// <param name="destinationUri">The destination location</param>
    /// <param name="destinationConfig">JSON configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with error message if validation fails</returns>
    Task<ValidationResult> ValidateAsync(
        string destinationUri,
        string? destinationConfig,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for the import execution service (orchestrates the import pipeline)
/// </summary>
public interface IImportExecutionService
{
    /// <summary>
    /// Executes a complete import profile (9-stage pipeline)
    /// </summary>
    /// <param name="profileId">The import profile ID to execute</param>
    /// <param name="triggeredBy">Who/what triggered this execution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution result</returns>
    Task<ImportExecutionResult> ExecuteAsync(
        int profileId,
        string triggeredBy = "Manual",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a running import execution
    /// </summary>
    /// <param name="executionId">The execution ID to cancel</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully cancelled, false if not found</returns>
    Task<bool> CancelAsync(int executionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for managing import profiles
/// </summary>
public interface IImportProfileService
{
    /// <summary>
    /// Creates a new import profile
    /// </summary>
    Task<int> CreateAsync(ImportProfile profile, string createdBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an import profile by ID
    /// </summary>
    Task<ImportProfile?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all import profiles
    /// </summary>
    Task<List<ImportProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an import profile
    /// </summary>
    Task<bool> UpdateAsync(ImportProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an import profile
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Validation result returned by source/destination validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public long? ResponseTimeMs { get; set; }
}

/// <summary>
/// Result of a data write operation
/// </summary>
public class WriteResult
{
    public int RowsWritten { get; set; }
    public int RowsFailed { get; set; }
    public int RowsSkipped { get; set; }
    public long? ExecutionTimeMs { get; set; }
    public List<string>? ErrorMessages { get; set; }
}

/// <summary>
/// Write mode for data writers
/// </summary>
public enum WriteMode
{
    Insert,    // INSERT all rows (fail if duplicates)
    Upsert     // INSERT or UPDATE based on keys
}

/// <summary>
/// Result of a complete import execution
/// </summary>
public class ImportExecutionResult
{
    public int ExecutionId { get; set; }
    public string Status { get; set; } = "Running";  // Running, Success, Failed
    public int? RowsRead { get; set; }
    public int? RowsWritten { get; set; }
    public int? RowsSkipped { get; set; }
    public int? RowsFailed { get; set; }
    public long? ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
}
