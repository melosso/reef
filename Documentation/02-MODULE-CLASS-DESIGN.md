# Reef Import: Detailed Module & Class Design

## Overview

This document defines the concrete classes, interfaces, and design patterns required to implement the Import mechanism. It focuses on **minimal code duplication** through inheritance and composition, **clear separation of concerns**, and **extensibility for future connectors**.

---

## 1. Core Abstractions

### 1.1 Direction-Agnostic Base Classes

**Pattern**: Use a common base class for shared logic between exports and imports.

```csharp
// Existing: Core.Models
public enum DataDirection
{
    Export,  // From Database to destinations
    Import   // From sources to Database
}

// New: Abstract base for both export and import profiles
public abstract class DataProfile
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DataDirection Direction { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int CreatedByUserId { get; set; }

    // Scheduling
    public string CronExpression { get; set; }
    public int IntervalSeconds { get; set; }
    public bool UseWebhook { get; set; }
    public string WebhookSecret { get; set; }

    // Transformation
    public string PreProcessTemplate { get; set; }  // Scriban
    public string PostProcessTemplate { get; set; } // Scriban

    // Error Handling
    public ErrorStrategy ErrorStrategy { get; set; } // Skip | Fail | Retry
    public int MaxRetries { get; set; }
    public int RetryDelaySeconds { get; set; }

    // Delta Sync
    public DeltaSyncMode DeltaSyncMode { get; set; } // Hash | Timestamp | Key
    public string DeltaSyncKeyColumns { get; set; }  // Comma-separated
    public bool TrackChanges { get; set; }

    // Logging
    public bool LogDetailedErrors { get; set; }
    public int ExecutionHistoryRetentionDays { get; set; }

    // Template Engine
    public ITemplateEngine GetTemplateEngine() => new ScribanTemplateEngine();

    public abstract void Validate();
}

// Existing: ExportProfile (inherits from DataProfile)
public class ExportProfile : DataProfile
{
    public int DatabaseConnectionId { get; set; }
    public string Query { get; set; }
    public List<OutputConfig> OutputConfigs { get; set; }
    public List<SplitRule> SplitRules { get; set; }

    public override void Validate()
    {
        if (string.IsNullOrEmpty(Query)) throw new ValidationException("Query required");
        if (OutputConfigs?.Count == 0) throw new ValidationException("At least one output required");
        if (string.IsNullOrEmpty(Name)) throw new ValidationException("Name required");
    }
}

// New: ImportProfile (inherits from DataProfile)
public class ImportProfile : DataProfile
{
    public int SourceConnectionId { get; set; }
    public DataSourceType SourceType { get; set; }  // REST, S3, FTP, Database, File
    public string SourceUri { get; set; }            // API URL, S3 bucket path, FTP path
    public string SourceConfiguration { get; set; }  // JSON: pagination, auth, filters

    public int DestinationConnectionId { get; set; }
    public DestinationType DestinationType { get; set; }  // Database, File, S3, FTP
    public string DestinationUri { get; set; }            // Table name, file path, S3 path
    public string DestinationConfiguration { get; set; }  // JSON: format, options, upsert logic

    // Data mapping
    public List<FieldMapping> FieldMappings { get; set; }
    public List<ValidationRule> ValidationRules { get; set; }

    public override void Validate()
    {
        if (SourceType == DataSourceType.Unknown) throw new ValidationException("Source type required");
        if (string.IsNullOrEmpty(SourceUri)) throw new ValidationException("Source URI required");
        if (DestinationType == DestinationType.Unknown) throw new ValidationException("Destination type required");
        if (string.IsNullOrEmpty(DestinationUri)) throw new ValidationException("Destination URI required");
        if (string.IsNullOrEmpty(Name)) throw new ValidationException("Name required");
    }
}

public enum DataSourceType
{
    Unknown,
    RestApi,
    S3,
    Ftp,
    Sftp,
    Database,
    File,
    AzureBlob,
    GoogleCloudStorage,
    Kafka,  // Future
    DatabaseCdc  // Future
}

public enum DestinationType
{
    Unknown,
    Database,
    File,
    S3,
    Ftp,
    Sftp,
    AzureBlob,
    GoogleCloudStorage,
    Kafka  // Future
}

public enum ErrorStrategy
{
    Skip,    // Skip rows with errors, continue
    Fail,    // Fail entire profile on any error
    Retry,   // Retry with exponential backoff
    Quarantine // Write errors to quarantine location
}

public enum DeltaSyncMode
{
    None,        // No delta sync (full load each time)
    Hash,        // SHA256 hash of all columns
    Timestamp,   // Compare timestamp columns
    Key,         // Natural/synthetic key comparison
    Incremental  // Auto-increment or sequence ID
}

// Field mapping for source → destination transformation
public class FieldMapping
{
    public string SourceColumn { get; set; }
    public string DestinationColumn { get; set; }
    public string DataType { get; set; }  // string, int, datetime, decimal, bool
    public bool Required { get; set; }
    public object DefaultValue { get; set; }
    public string TransformationTemplate { get; set; }  // Scriban for derived fields
}

// Validation rule for imported data
public class ValidationRule
{
    public string ColumnName { get; set; }
    public ValidationType ValidationType { get; set; }
    public string Pattern { get; set; }  // Regex, JSON schema, etc.
    public object MinValue { get; set; }
    public object MaxValue { get; set; }
    public List<object> AllowedValues { get; set; }
}

public enum ValidationType
{
    Required,
    Regex,
    MinLength,
    MaxLength,
    MinValue,
    MaxValue,
    Enum,
    Custom
}
```

---

## 2. Data Source Executors

**Pattern**: Strategy pattern for pluggable data sources. Mirror of existing `QueryExecutor`.

```csharp
// Interface: Core.Abstractions
public interface IDataSourceExecutor
{
    DataSourceType SourceType { get; }

    /// <summary>
    /// Fetch data from source. Returns as List<Dictionary<string, object>> for flexibility.
    /// </summary>
    Task<DataSourceResult> FetchAsync(
        ImportProfile profile,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Test connectivity to source without fetching data.
    /// </summary>
    Task<bool> TestConnectionAsync(ImportProfile profile);

    /// <summary>
    /// Get schema/structure of source data (column names, types).
    /// </summary>
    Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile);
}

public class DataSourceResult
{
    public List<Dictionary<string, object>> Rows { get; set; } = new();
    public int TotalRows { get; set; }
    public string NextPageToken { get; set; }  // For paginated sources
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class DataSourceSchema
{
    public List<ColumnDefinition> Columns { get; set; } = new();
    public string PrimaryKeyColumn { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class ColumnDefinition
{
    public string Name { get; set; }
    public string Type { get; set; }  // string, int, datetime, decimal, bool
    public int MaxLength { get; set; }
    public bool Nullable { get; set; }
}

// ============================================================================
// Implementation 1: REST API Data Source
// ============================================================================

public class RestDataSourceExecutor : IDataSourceExecutor
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RestDataSourceExecutor> _logger;
    private readonly IConnectionService _connectionService;

    public DataSourceType SourceType => DataSourceType.RestApi;

    public RestDataSourceExecutor(
        HttpClient httpClient,
        ILogger<RestDataSourceExecutor> logger,
        IConnectionService connectionService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _connectionService = connectionService;
    }

    public async Task<DataSourceResult> FetchAsync(ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<RestSourceConfig>(profile.SourceConfiguration);
        var result = new DataSourceResult();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, profile.SourceUri);

            // Add authentication headers
            if (!string.IsNullOrEmpty(config.AuthenticationToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AuthenticationToken);
            }

            // Add custom headers
            if (config.CustomHeaders != null)
            {
                foreach (var header in config.CustomHeaders)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            // Handle pagination
            string nextUrl = profile.SourceUri;
            int pageCount = 0;

            while (!string.IsNullOrEmpty(nextUrl) && pageCount < config.MaxPages)
            {
                request.RequestUri = new Uri(nextUrl);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var jObject = JsonDocument.Parse(content);

                // Extract data array (configurable path via JSONPath)
                var dataArray = ExtractDataArray(jObject, config.DataPath);

                // Parse rows
                foreach (var item in dataArray.EnumerateArray())
                {
                    var row = new Dictionary<string, object>();
                    foreach (var property in item.EnumerateObject())
                    {
                        row[property.Name] = property.Value.GetRawText();
                    }
                    result.Rows.Add(row);
                }

                // Handle pagination
                if (config.PaginationType == PaginationType.Cursor)
                {
                    nextUrl = ExtractValue(jObject, config.NextCursorPath);
                }
                else if (config.PaginationType == PaginationType.Offset)
                {
                    config.PageOffset += config.PageSize;
                    nextUrl = $"{profile.SourceUri}?offset={config.PageOffset}&limit={config.PageSize}";
                }
                else
                {
                    break;
                }

                pageCount++;
            }

            result.TotalRows = result.Rows.Count;
            _logger.LogInformation($"Fetched {result.TotalRows} rows from REST API");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching from REST API: {profile.SourceUri}");
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(ImportProfile profile)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, profile.SourceUri);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile)
    {
        // Fetch small sample, extract column names
        var result = await FetchAsync(profile);
        var schema = new DataSourceSchema();

        if (result.Rows.Count > 0)
        {
            var sampleRow = result.Rows[0];
            foreach (var key in sampleRow.Keys)
            {
                schema.Columns.Add(new ColumnDefinition
                {
                    Name = key,
                    Type = InferType(sampleRow[key]),
                    Nullable = true
                });
            }
        }

        return schema;
    }

    private string InferType(object value)
    {
        if (value == null) return "string";
        var type = value.GetType().Name.ToLower();
        return type switch
        {
            "int32" or "int64" => "int",
            "decimal" or "double" => "decimal",
            "boolean" => "bool",
            "datetime" => "datetime",
            _ => "string"
        };
    }
}

public class RestSourceConfig
{
    public string DataPath { get; set; } = "data";  // JSONPath to data array
    public PaginationType PaginationType { get; set; } = PaginationType.None;
    public string NextCursorPath { get; set; }  // JSONPath to next cursor
    public int PageSize { get; set; } = 100;
    public int PageOffset { get; set; } = 0;
    public int MaxPages { get; set; } = 1000;
    public string AuthenticationToken { get; set; }
    public Dictionary<string, string> CustomHeaders { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 30;
}

public enum PaginationType
{
    None,
    Offset,
    Cursor,
    PageNumber,
    Link  // Use Link header for pagination
}

// ============================================================================
// Implementation 2: S3 Data Source
// ============================================================================

public class S3DataSourceExecutor : IDataSourceExecutor
{
    private readonly ILogger<S3DataSourceExecutor> _logger;
    private readonly IConnectionService _connectionService;

    public DataSourceType SourceType => DataSourceType.S3;

    public S3DataSourceExecutor(
        ILogger<S3DataSourceExecutor> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
    }

    public async Task<DataSourceResult> FetchAsync(ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<S3SourceConfig>(profile.SourceConfiguration);
        var result = new DataSourceResult();

        try
        {
            // TODO: Implement S3 client initialization and object download
            // For MVP: Use System.Net.Http with presigned URLs

            _logger.LogInformation($"Fetched S3 objects from {profile.SourceUri}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching from S3: {profile.SourceUri}");
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(ImportProfile profile)
    {
        // TODO: List S3 bucket to verify credentials
        return true;
    }

    public async Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile)
    {
        // TODO: Infer schema from first object
        return new DataSourceSchema();
    }
}

public class S3SourceConfig
{
    public string BucketName { get; set; }
    public string ObjectKeyPrefix { get; set; }
    public string FilePattern { get; set; } = "*.csv";  // Glob pattern
    public string AwsAccessKeyId { get; set; }
    public string AwsSecretAccessKey { get; set; }
    public string Region { get; set; } = "us-east-1";
    public int MaxObjectsPerFetch { get; set; } = 100;
}

// ============================================================================
// Implementation 3: FTP Data Source
// ============================================================================

public class FtpDataSourceExecutor : IDataSourceExecutor
{
    private readonly ILogger<FtpDataSourceExecutor> _logger;
    private readonly IConnectionService _connectionService;

    public DataSourceType SourceType => DataSourceType.Ftp;

    public FtpDataSourceExecutor(
        ILogger<FtpDataSourceExecutor> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
    }

    public async Task<DataSourceResult> FetchAsync(ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<FtpSourceConfig>(profile.SourceConfiguration);
        var result = new DataSourceResult();

        try
        {
            // TODO: FTP client implementation
            _logger.LogInformation($"Fetched files from FTP: {profile.SourceUri}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching from FTP: {profile.SourceUri}");
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(ImportProfile profile) => true;

    public async Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile) => new();
}

public class FtpSourceConfig
{
    public string Host { get; set; }
    public int Port { get; set; } = 21;
    public string Username { get; set; }
    public string Password { get; set; }
    public string RemotePath { get; set; }
    public string FilePattern { get; set; } = "*.csv";
    public bool UseSftp { get; set; } = false;
}

// ============================================================================
// Implementation 4: Database Data Source (Direct)
// ============================================================================

public class DatabaseDataSourceExecutor : IDataSourceExecutor
{
    private readonly ILogger<DatabaseDataSourceExecutor> _logger;
    private readonly IConnectionService _connectionService;
    private readonly QueryExecutor _queryExecutor;

    public DataSourceType SourceType => DataSourceType.Database;

    public DatabaseDataSourceExecutor(
        ILogger<DatabaseDataSourceExecutor> logger,
        IConnectionService connectionService,
        QueryExecutor queryExecutor)
    {
        _logger = logger;
        _connectionService = connectionService;
        _queryExecutor = queryExecutor;
    }

    public async Task<DataSourceResult> FetchAsync(ImportProfile profile, CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<DatabaseSourceConfig>(profile.SourceConfiguration);
        var result = new DataSourceResult();

        try
        {
            // Reuse existing QueryExecutor
            var rows = await _queryExecutor.ExecuteAsync(
                profile.SourceConnectionId,
                config.Query,
                cancellationToken);

            result.Rows = rows;
            result.TotalRows = rows.Count;
            _logger.LogInformation($"Fetched {result.TotalRows} rows from database");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching from database");
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(ImportProfile profile)
    {
        try
        {
            var connection = _connectionService.GetConnection(profile.SourceConnectionId);
            // Test query: SELECT 1
            var rows = await _queryExecutor.ExecuteAsync(
                profile.SourceConnectionId,
                "SELECT 1",
                CancellationToken.None);
            return rows.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile)
    {
        var config = JsonSerializer.Deserialize<DatabaseSourceConfig>(profile.SourceConfiguration);
        // TODO: Fetch INFORMATION_SCHEMA to get column definitions
        return new DataSourceSchema();
    }
}

public class DatabaseSourceConfig
{
    public string Query { get; set; }  // SELECT * FROM table_name
    public int TimeoutSeconds { get; set; } = 300;
}

// ============================================================================
// Factory for Data Source Executors
// ============================================================================

public interface IDataSourceExecutorFactory
{
    IDataSourceExecutor CreateExecutor(DataSourceType sourceType);
}

public class DataSourceExecutorFactory : IDataSourceExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataSourceExecutorFactory> _logger;

    public DataSourceExecutorFactory(IServiceProvider serviceProvider, ILogger<DataSourceExecutorFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IDataSourceExecutor CreateExecutor(DataSourceType sourceType)
    {
        return sourceType switch
        {
            DataSourceType.RestApi => _serviceProvider.GetRequiredService<RestDataSourceExecutor>(),
            DataSourceType.S3 => _serviceProvider.GetRequiredService<S3DataSourceExecutor>(),
            DataSourceType.Ftp => _serviceProvider.GetRequiredService<FtpDataSourceExecutor>(),
            DataSourceType.Sftp => _serviceProvider.GetRequiredService<FtpDataSourceExecutor>(),
            DataSourceType.Database => _serviceProvider.GetRequiredService<DatabaseDataSourceExecutor>(),
            _ => throw new NotSupportedException($"Data source type {sourceType} not supported")
        };
    }
}
```

---

## 3. Writer Service (Mirror of DestinationService)

```csharp
// Interface: Core.Abstractions
public interface IDataWriter
{
    DestinationType DestinationType { get; }

    /// <summary>
    /// Write rows to destination.
    /// </summary>
    Task<DataWriteResult> WriteAsync(
        ImportProfile profile,
        List<Dictionary<string, object>> rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Test connectivity to destination without writing data.
    /// </summary>
    Task<bool> TestConnectionAsync(ImportProfile profile);

    /// <summary>
    /// Get target schema/table structure.
    /// </summary>
    Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile);
}

public class DataWriteResult
{
    public int RowsWritten { get; set; }
    public int RowsFailed { get; set; }
    public List<DataWriteError> Errors { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class DataWriteError
{
    public int RowIndex { get; set; }
    public Dictionary<string, object> RowData { get; set; }
    public string ErrorMessage { get; set; }
    public Exception Exception { get; set; }
}

// ============================================================================
// Implementation 1: Database Writer (with UPSERT)
// ============================================================================

public class DatabaseWriter : IDataWriter
{
    private readonly ILogger<DatabaseWriter> _logger;
    private readonly IConnectionService _connectionService;

    public DestinationType DestinationType => DestinationType.Database;

    public DatabaseWriter(ILogger<DatabaseWriter> logger, IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
    }

    public async Task<DataWriteResult> WriteAsync(
        ImportProfile profile,
        List<Dictionary<string, object>> rows,
        CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<DatabaseWriteConfig>(profile.DestinationConfiguration);
        var result = new DataWriteResult();

        var connection = _connectionService.GetConnection(profile.DestinationConnectionId);

        try
        {
            using var dbConnection = new SqlConnection(connection.ConnectionString);
            await dbConnection.OpenAsync(cancellationToken);

            using var transaction = dbConnection.BeginTransaction();

            foreach (var (row, index) in rows.WithIndex())
            {
                try
                {
                    if (config.UpsertMode == UpsertMode.Insert)
                    {
                        await InsertRowAsync(dbConnection, transaction, profile, row, config);
                    }
                    else
                    {
                        await UpsertRowAsync(dbConnection, transaction, profile, row, config);
                    }
                    result.RowsWritten++;
                }
                catch (Exception ex)
                {
                    if (profile.ErrorStrategy == ErrorStrategy.Fail)
                    {
                        throw;
                    }

                    result.RowsFailed++;
                    result.Errors.Add(new DataWriteError
                    {
                        RowIndex = index,
                        RowData = row,
                        ErrorMessage = ex.Message,
                        Exception = ex
                    });

                    _logger.LogWarning(ex, $"Error writing row {index}");
                }
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation($"Wrote {result.RowsWritten} rows to {profile.DestinationUri}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error writing to database");
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(ImportProfile profile)
    {
        try
        {
            var connection = _connectionService.GetConnection(profile.DestinationConnectionId);
            using var dbConnection = new SqlConnection(connection.ConnectionString);
            await dbConnection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile)
    {
        var config = JsonSerializer.Deserialize<DatabaseWriteConfig>(profile.DestinationConfiguration);
        // TODO: Query INFORMATION_SCHEMA for table columns
        return new DataSourceSchema();
    }

    private async Task InsertRowAsync(
        SqlConnection dbConnection,
        SqlTransaction transaction,
        ImportProfile profile,
        Dictionary<string, object> row,
        DatabaseWriteConfig config)
    {
        var columns = string.Join(", ", row.Keys);
        var parameters = string.Join(", ", row.Keys.Select(k => $"@{k}"));
        var sql = $"INSERT INTO {config.TableName} ({columns}) VALUES ({parameters})";

        using var cmd = new SqlCommand(sql, dbConnection, transaction);
        foreach (var (key, value) in row)
        {
            cmd.Parameters.AddWithValue($"@{key}", value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpsertRowAsync(
        SqlConnection dbConnection,
        SqlTransaction transaction,
        ImportProfile profile,
        Dictionary<string, object> row,
        DatabaseWriteConfig config)
    {
        // SQL Server MERGE statement for UPSERT
        var keyColumns = config.KeyColumns.Split(',').Select(c => c.Trim()).ToList();
        var allColumns = row.Keys.ToList();
        var updateColumns = allColumns.Except(keyColumns).ToList();

        var matchCondition = string.Join(" AND ", keyColumns.Select(k => $"target.[{k}] = source.[{k}]"));
        var updateSet = string.Join(", ", updateColumns.Select(c => $"target.[{c}] = source.[{c}]"));
        var sourceSelect = string.Join(", ", allColumns.Select(c => $"@{c} AS [{c}]"));

        var sql = $@"
            MERGE INTO {config.TableName} AS target
            USING (SELECT {sourceSelect}) AS source
            ON {matchCondition}
            WHEN MATCHED THEN UPDATE SET {updateSet}
            WHEN NOT MATCHED THEN INSERT ({string.Join(", ", allColumns.Select(c => $"[{c}]"))})
            VALUES ({string.Join(", ", allColumns.Select(c => $"source.[{c}]"))});
        ";

        using var cmd = new SqlCommand(sql, dbConnection, transaction);
        foreach (var (key, value) in row)
        {
            cmd.Parameters.AddWithValue($"@{key}", value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync();
    }
}

public class DatabaseWriteConfig
{
    public string TableName { get; set; }
    public UpsertMode UpsertMode { get; set; } = UpsertMode.Insert;
    public string KeyColumns { get; set; }  // Comma-separated for UPSERT
    public bool CreateTableIfNotExists { get; set; } = false;
}

public enum UpsertMode
{
    Insert,    // Only insert (fail if exists)
    Upsert,    // Insert or update
    Update,    // Only update
    Truncate   // Clear and re-insert
}

// ============================================================================
// Implementation 2: File Writer (CSV, JSON)
// ============================================================================

public class FileWriter : IDataWriter
{
    private readonly ILogger<FileWriter> _logger;

    public DestinationType DestinationType => DestinationType.File;

    public FileWriter(ILogger<FileWriter> logger)
    {
        _logger = logger;
    }

    public async Task<DataWriteResult> WriteAsync(
        ImportProfile profile,
        List<Dictionary<string, object>> rows,
        CancellationToken cancellationToken = default)
    {
        var config = JsonSerializer.Deserialize<FileWriteConfig>(profile.DestinationConfiguration);
        var result = new DataWriteResult();

        try
        {
            var directory = Path.GetDirectoryName(profile.DestinationUri);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (config.Format == FileFormat.Csv)
            {
                await WriteCsvAsync(profile.DestinationUri, rows);
            }
            else if (config.Format == FileFormat.Json)
            {
                await WriteJsonAsync(profile.DestinationUri, rows);
            }

            result.RowsWritten = rows.Count;
            _logger.LogInformation($"Wrote {result.RowsWritten} rows to {profile.DestinationUri}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error writing to file");
            throw;
        }
    }

    private async Task WriteCsvAsync(string path, List<Dictionary<string, object>> rows)
    {
        if (rows.Count == 0) return;

        using var writer = new StreamWriter(path, append: false);

        // Write header
        var headers = rows[0].Keys;
        await writer.WriteLineAsync(string.Join(",", headers.Select(h => $"\"{h}\"")));

        // Write rows
        foreach (var row in rows)
        {
            var values = headers.Select(h => row.ContainsKey(h) ? row[h]?.ToString() ?? "" : "");
            var csvLine = string.Join(",", values.Select(v => $"\"{v.Replace("\"", "\"\"")}\""));
            await writer.WriteLineAsync(csvLine);
        }
    }

    private async Task WriteJsonAsync(string path, List<Dictionary<string, object>> rows)
    {
        var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<bool> TestConnectionAsync(ImportProfile profile)
    {
        try
        {
            var directory = Path.GetDirectoryName(profile.DestinationUri);
            return Directory.Exists(directory) && IsDirectoryWritable(directory);
        }
        catch
        {
            return false;
        }
    }

    public async Task<DataSourceSchema> GetSchemaAsync(ImportProfile profile) => new();

    private bool IsDirectoryWritable(string path)
    {
        try
        {
            var testFile = Path.Combine(path, ".reef-test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class FileWriteConfig
{
    public FileFormat Format { get; set; } = FileFormat.Csv;
    public bool Append { get; set; } = false;
    public bool CreateDirectoryIfNotExists { get; set; } = true;
    public string Encoding { get; set; } = "UTF-8";
}

public enum FileFormat
{
    Csv,
    Json,
    Parquet,
    Xml
}

// ============================================================================
// Writer Factory
// ============================================================================

public interface IDataWriterFactory
{
    IDataWriter CreateWriter(DestinationType destinationType);
}

public class DataWriterFactory : IDataWriterFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DataWriterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IDataWriter CreateWriter(DestinationType destinationType)
    {
        return destinationType switch
        {
            DestinationType.Database => _serviceProvider.GetRequiredService<DatabaseWriter>(),
            DestinationType.File => _serviceProvider.GetRequiredService<FileWriter>(),
            DestinationType.S3 => _serviceProvider.GetRequiredService<S3Writer>(),
            DestinationType.Ftp => _serviceProvider.GetRequiredService<FtpWriter>(),
            _ => throw new NotSupportedException($"Destination type {destinationType} not supported")
        };
    }
}
```

---

## 4. Import Execution Service

```csharp
// Core.Services
public interface IImportExecutionService
{
    Task<ImportExecutionResult> ExecuteAsync(
        ImportProfile profile,
        ExecutionContext context,
        CancellationToken cancellationToken = default);
}

public class ImportExecutionService : IImportExecutionService
{
    private readonly IDataSourceExecutorFactory _sourceFactory;
    private readonly IDataWriterFactory _writerFactory;
    private readonly IDeltaSyncService _deltaSyncService;
    private readonly ITemplateEngine _templateEngine;
    private readonly IImportExecutionLogger _executionLogger;
    private readonly ILogger<ImportExecutionService> _logger;

    public ImportExecutionService(
        IDataSourceExecutorFactory sourceFactory,
        IDataWriterFactory writerFactory,
        IDeltaSyncService deltaSyncService,
        ITemplateEngine templateEngine,
        IImportExecutionLogger executionLogger,
        ILogger<ImportExecutionService> logger)
    {
        _sourceFactory = sourceFactory;
        _writerFactory = writerFactory;
        _deltaSyncService = deltaSyncService;
        _templateEngine = templateEngine;
        _executionLogger = executionLogger;
        _logger = logger;
    }

    public async Task<ImportExecutionResult> ExecuteAsync(
        ImportProfile profile,
        ExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var result = new ImportExecutionResult { ProfileId = profile.Id };
        var executionId = Guid.NewGuid();
        var startTime = DateTime.UtcNow;

        try
        {
            // 1. VALIDATION
            await LogAsync(executionId, "VALIDATION", "Starting validation...");
            profile.Validate();
            await LogAsync(executionId, "VALIDATION", "Profile validation passed");

            // 2. PRE-FETCH TRANSFORMATION
            if (!string.IsNullOrEmpty(profile.PreProcessTemplate))
            {
                await LogAsync(executionId, "PRE_PROCESS", "Applying pre-fetch template...");
                // Context data: { "execution_context": {...} }
            }

            // 3. DATA SOURCE READ
            await LogAsync(executionId, "SOURCE_READ", $"Reading from {profile.SourceType}...");
            var sourceExecutor = _sourceFactory.CreateExecutor(profile.SourceType);
            var sourceResult = await sourceExecutor.FetchAsync(profile, cancellationToken);
            await LogAsync(executionId, "SOURCE_READ", $"Fetched {sourceResult.Rows.Count} rows from source");

            // 4. DELTA SYNC
            if (profile.TrackChanges && profile.DeltaSyncMode != DeltaSyncMode.None)
            {
                await LogAsync(executionId, "DELTA_SYNC", "Detecting changes...");
                var (newRows, changedRows) = await _deltaSyncService.ClassifyRowsAsync(
                    profile.Id,
                    sourceResult.Rows,
                    profile.DeltaSyncMode,
                    profile.DeltaSyncKeyColumns,
                    cancellationToken);

                sourceResult.Rows = newRows.Concat(changedRows).ToList();
                result.NewRowsDetected = newRows.Count;
                result.ChangedRowsDetected = changedRows.Count;
                await LogAsync(executionId, "DELTA_SYNC",
                    $"Delta sync: {newRows.Count} new, {changedRows.Count} changed");
            }

            // 5. ROW-LEVEL TRANSFORMATION
            await LogAsync(executionId, "TRANSFORM", "Transforming rows...");
            var transformedRows = new List<Dictionary<string, object>>();

            foreach (var (row, index) in sourceResult.Rows.WithIndex())
            {
                try
                {
                    var transformed = await TransformRowAsync(profile, row, context, cancellationToken);
                    transformedRows.Add(transformed);
                }
                catch (Exception ex)
                {
                    if (profile.ErrorStrategy == ErrorStrategy.Fail)
                    {
                        throw;
                    }

                    result.RowsFailed++;
                    await LogAsync(executionId, "TRANSFORM_ERROR",
                        $"Row {index}: {ex.Message}", isError: true);
                }
            }

            // 6. SCHEMA VALIDATION
            await LogAsync(executionId, "VALIDATION", "Validating transformed schema...");
            ValidateSchema(profile, transformedRows);

            // 7. WRITE TO DESTINATION
            await LogAsync(executionId, "WRITE", "Writing to destination...");
            var writer = _writerFactory.CreateWriter(profile.DestinationType);
            var writeResult = await writer.WriteAsync(profile, transformedRows, cancellationToken);

            result.RowsWritten = writeResult.RowsWritten;
            result.RowsFailed += writeResult.RowsFailed;
            result.WriteErrors = writeResult.Errors;

            await LogAsync(executionId, "WRITE",
                $"Wrote {writeResult.RowsWritten} rows, {writeResult.RowsFailed} failures");

            // 8. POST-WRITE TRANSFORMATION
            if (!string.IsNullOrEmpty(profile.PostProcessTemplate))
            {
                await LogAsync(executionId, "POST_PROCESS", "Executing post-write template...");
                // Example: trigger downstream exports, send notifications
            }

            // 9. COMMIT & AUDIT
            await LogAsync(executionId, "COMMIT", "Committing changes...");
            if (profile.TrackChanges && profile.DeltaSyncMode != DeltaSyncMode.None)
            {
                await _deltaSyncService.CommitAsync(profile.Id, sourceResult.Rows, cancellationToken);
            }

            result.Status = ExecutionStatus.Success;
            result.Duration = DateTime.UtcNow - startTime;

            await _executionLogger.LogExecutionAsync(executionId, result);
            _logger.LogInformation($"Import {profile.Name} completed successfully in {result.Duration.TotalSeconds}s");

            return result;
        }
        catch (Exception ex)
        {
            result.Status = ExecutionStatus.Failed;
            result.ErrorMessage = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;

            await LogAsync(executionId, "ERROR", ex.Message, isError: true);
            await _executionLogger.LogExecutionAsync(executionId, result);

            _logger.LogError(ex, $"Import {profile.Name} failed");
            throw;
        }
    }

    private async Task<Dictionary<string, object>> TransformRowAsync(
        ImportProfile profile,
        Dictionary<string, object> row,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var transformed = new Dictionary<string, object>();

        // Apply field mappings
        foreach (var mapping in profile.FieldMappings ?? new List<FieldMapping>())
        {
            var value = row.ContainsKey(mapping.SourceColumn) ? row[mapping.SourceColumn] : null;

            // Apply transformation template if defined
            if (!string.IsNullOrEmpty(mapping.TransformationTemplate))
            {
                var templateContext = new Dictionary<string, object>
                {
                    { "value", value },
                    { "row", row },
                    { "execution_context", context }
                };

                value = await _templateEngine.RenderAsync(
                    mapping.TransformationTemplate,
                    templateContext,
                    cancellationToken);
            }

            // Type conversion
            value = ConvertType(value, mapping.DataType);

            // Validation
            ValidateField(mapping, value);

            // Apply default if null
            if (value == null && mapping.DefaultValue != null)
            {
                value = mapping.DefaultValue;
            }

            transformed[mapping.DestinationColumn] = value;
        }

        return transformed;
    }

    private object ConvertType(object value, string targetType)
    {
        if (value == null) return null;

        return targetType.ToLower() switch
        {
            "int" => Convert.ToInt32(value),
            "decimal" => Convert.ToDecimal(value),
            "bool" or "boolean" => Convert.ToBoolean(value),
            "datetime" => Convert.ToDateTime(value),
            _ => value.ToString()
        };
    }

    private void ValidateField(FieldMapping mapping, object value)
    {
        if (mapping.Required && (value == null || string.IsNullOrEmpty(value.ToString())))
        {
            throw new ValidationException($"Field {mapping.DestinationColumn} is required");
        }
    }

    private void ValidateSchema(ImportProfile profile, List<Dictionary<string, object>> rows)
    {
        foreach (var rule in profile.ValidationRules ?? new List<ValidationRule>())
        {
            foreach (var row in rows)
            {
                if (!row.ContainsKey(rule.ColumnName))
                {
                    throw new ValidationException($"Required column {rule.ColumnName} not found");
                }

                var value = row[rule.ColumnName];
                ValidateRule(rule, value);
            }
        }
    }

    private void ValidateRule(ValidationRule rule, object value)
    {
        if (value == null) return;

        switch (rule.ValidationType)
        {
            case ValidationType.Required:
                if (string.IsNullOrEmpty(value.ToString()))
                    throw new ValidationException($"Value is required");
                break;

            case ValidationType.MinValue:
                if (decimal.Parse(value.ToString()) < decimal.Parse(rule.MinValue.ToString()))
                    throw new ValidationException($"Value below minimum {rule.MinValue}");
                break;

            case ValidationType.MaxValue:
                if (decimal.Parse(value.ToString()) > decimal.Parse(rule.MaxValue.ToString()))
                    throw new ValidationException($"Value exceeds maximum {rule.MaxValue}");
                break;

            case ValidationType.Enum:
                if (!rule.AllowedValues.Contains(value))
                    throw new ValidationException($"Invalid value {value}");
                break;
        }
    }

    private async Task LogAsync(Guid executionId, string stage, string message, bool isError = false)
    {
        if (isError)
        {
            _logger.LogError($"[{executionId}] {stage}: {message}");
        }
        else
        {
            _logger.LogInformation($"[{executionId}] {stage}: {message}");
        }
    }
}

public class ImportExecutionResult
{
    public int ProfileId { get; set; }
    public ExecutionStatus Status { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public int RowsWritten { get; set; }
    public int RowsFailed { get; set; }
    public int NewRowsDetected { get; set; }
    public int ChangedRowsDetected { get; set; }
    public List<DataWriteError> WriteErrors { get; set; } = new();
    public string ErrorMessage { get; set; }
}

public enum ExecutionStatus
{
    Pending,
    Running,
    Success,
    Failed,
    Cancelled
}

public class ExecutionContext
{
    public Guid ExecutionId { get; set; }
    public int UserId { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
    public DateTime TriggeredAt { get; set; }
    public string TriggerType { get; set; }  // Manual, Scheduled, Webhook
}
```

---

## 5. Service Registration (Dependency Injection)

```csharp
// Program.cs or ServiceCollectionExtensions.cs

public static IServiceCollection AddImportServices(this IServiceCollection services)
{
    // Data source executors
    services.AddScoped<RestDataSourceExecutor>();
    services.AddScoped<S3DataSourceExecutor>();
    services.AddScoped<FtpDataSourceExecutor>();
    services.AddScoped<DatabaseDataSourceExecutor>();
    services.AddScoped<IDataSourceExecutorFactory, DataSourceExecutorFactory>();

    // Writers
    services.AddScoped<DatabaseWriter>();
    services.AddScoped<FileWriter>();
    services.AddScoped<S3Writer>();
    services.AddScoped<FtpWriter>();
    services.AddScoped<IDataWriterFactory, DataWriterFactory>();

    // Execution
    services.AddScoped<IImportExecutionService, ImportExecutionService>();

    // Profile service
    services.AddScoped<IImportProfileService, ImportProfileService>();

    // Job service
    services.AddScoped<IImportJobService, ImportJobService>();

    // Execution logging
    services.AddScoped<IImportExecutionLogger, ImportExecutionLogger>();

    // HTTP client for REST executor
    services.AddHttpClient<RestDataSourceExecutor>();

    return services;
}
```

---

## 6. Summary of New Classes and Interfaces

| Class | Purpose | Location |
|-------|---------|----------|
| `ImportProfile` | Profile definition for imports | `Core.Models` |
| `DataSourceType` (enum) | Source types | `Core.Models` |
| `DestinationType` (enum) | Destination types | `Core.Models` |
| `IDataSourceExecutor` | Interface for source executors | `Core.Abstractions` |
| `RestDataSourceExecutor` | REST API source | `Core.Services` |
| `S3DataSourceExecutor` | S3 bucket source | `Core.Services` |
| `FtpDataSourceExecutor` | FTP/SFTP source | `Core.Services` |
| `DatabaseDataSourceExecutor` | Direct database read | `Core.Services` |
| `IDataWriter` | Interface for destination writers | `Core.Abstractions` |
| `DatabaseWriter` | Database UPSERT destination | `Core.Services` |
| `FileWriter` | CSV/JSON file destination | `Core.Services` |
| `IImportExecutionService` | Main import orchestrator | `Core.Services` |
| `ImportExecutionService` | Implementation | `Core.Services` |
| `FieldMapping` | Column mapping definition | `Core.Models` |
| `ValidationRule` | Data validation rule | `Core.Models` |

**Total new classes**: ~15
**Total new interfaces**: ~5
**Reused components**: QueryExecutor, DeltaSyncService, TemplateEngine, JobScheduler, ExecutionLogger, ConnectionService

---

## 7. Design Patterns Applied

| Pattern | Usage |
|---------|-------|
| **Strategy** | Data source executors, writers (pluggable implementations) |
| **Factory** | DataSourceExecutorFactory, DataWriterFactory |
| **Template Method** | ImportExecutionService (defines pipeline steps) |
| **Decorator** | Retry logic on async operations |
| **Dependency Injection** | Constructor injection for services |
| **Repository** | IImportProfileRepository for data access |

---

**Document Version**: 1.0
**Status**: Design Phase
**Next Step**: Implement data layer and API endpoints
