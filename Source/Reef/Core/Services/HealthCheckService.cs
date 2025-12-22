// Source/Reef/Core/Services/HealthCheckService.cs
// Background service for system health monitoring

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Reef.Core.Models;
using Serilog;
using System.Diagnostics;

namespace Reef.Core.Services;

/// <summary>
/// Background service that monitors system health
/// </summary>
public class HealthCheckService : BackgroundService
{
    private readonly DatabaseConfig _config;
    private readonly DateTime _startTime;
    private HealthStatus _currentStatus;
    private readonly object _statusLock = new();

    public HealthCheckService(DatabaseConfig config)
    {
        _config = config;
        _startTime = DateTime.UtcNow;
        _currentStatus = new HealthStatus
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get current health status
    /// </summary>
    public HealthStatus GetHealthStatus()
    {
        lock (_statusLock)
        {
            return _currentStatus;
        }
    }

    /// <summary>
    /// Execute health checks periodically
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Debug("Health check service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformHealthCheckAsync();
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error performing health check");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        Log.Debug("Health check service stopped");
    }

    /// <summary>
    /// Perform comprehensive health check
    /// </summary>
    private async Task PerformHealthCheckAsync()
    {
        var status = new HealthStatus
        {
            Timestamp = DateTime.UtcNow,
            Uptime = (long)(DateTime.UtcNow - _startTime).TotalSeconds
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Check database connectivity
            var dbHealth = await CheckDatabaseHealthAsync();
            status.Components["database"] = dbHealth;

            // Check disk space
            var diskHealth = CheckDiskSpaceHealth();
            status.Components["diskSpace"] = diskHealth;

            // Check exports directory
            var exportsHealth = await CheckExportsDirectoryHealth();
            status.Components["exportsDirectory"] = exportsHealth;

            // Overall status
            var unhealthyComponents = status.Components.Values.Count(c => c.Status == "Unhealthy");
            var degradedComponents = status.Components.Values.Count(c => c.Status == "Degraded");

            if (unhealthyComponents > 0)
            {
                status.Status = "Unhealthy";
            }
            else if (degradedComponents > 0)
            {
                status.Status = "Degraded";
            }
            else
            {
                status.Status = "Healthy";
            }

            stopwatch.Stop();
            status.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

            lock (_statusLock)
            {
                _currentStatus = status;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Health check failed");
            status.Status = "Unhealthy";
            status.ErrorMessage = ex.Message;
            
            lock (_statusLock)
            {
                _currentStatus = status;
            }
        }
    }

    /// <summary>
    /// Check database connectivity and health
    /// </summary>
    private async Task<ComponentHealth> CheckDatabaseHealthAsync()
    {
        var component = new ComponentHealth
        {
            Name = "SQLite Database"
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var connection = new SqliteConnection(_config.ConnectionString);
            await connection.OpenAsync();

            // Simple query to verify database is responsive
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync();

            stopwatch.Stop();
            component.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

            if (component.ResponseTimeMs > 1000)
            {
                component.Status = "Degraded";
                component.Message = $"Database responding slowly ({component.ResponseTimeMs}ms)";
                Log.Warning("Database health check degraded: {ResponseTime}ms", component.ResponseTimeMs);
            }
            else
            {
                component.Status = "Healthy";
                component.Message = "Database is responsive";
            }

            // Get database size
            var dbPath = connection.DataSource;
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                component.Metadata["sizeBytes"] = fileInfo.Length;
                component.Metadata["sizeMB"] = Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2);
            }
        }
        catch (Exception ex)
        {
            component.Status = "Unhealthy";
            component.Message = $"Database error: {ex.Message}";
            Log.Error(ex, "Database health check failed");
        }

        return component;
    }

    /// <summary>
    /// Check disk space availability
    /// </summary>
    private ComponentHealth CheckDiskSpaceHealth()
    {
        var component = new ComponentHealth
        {
            Name = "Disk Space"
        };

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory) ?? "C:\\");

            var availableGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            var totalGB = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
            var usedPercentage = ((totalGB - availableGB) / totalGB) * 100;

            component.Metadata["availableGB"] = Math.Round(availableGB, 2);
            component.Metadata["totalGB"] = Math.Round(totalGB, 2);
            component.Metadata["usedPercentage"] = Math.Round(usedPercentage, 2);

            if (availableGB < 1)
            {
                component.Status = "Unhealthy";
                component.Message = $"Critical: Only {availableGB:F2}GB available";
                Log.Warning("Disk space critical: {AvailableGB}GB available", availableGB);
            }
            else if (availableGB < 5)
            {
                component.Status = "Degraded";
                component.Message = $"Warning: Only {availableGB:F2}GB available";
                Log.Warning("Disk space low: {AvailableGB}GB available", availableGB);
            }
            else
            {
                component.Status = "Healthy";
                component.Message = $"{availableGB:F2}GB available";
            }
        }
        catch (Exception ex)
        {
            component.Status = "Degraded";
            component.Message = $"Could not check disk space: {ex.Message}";
            Log.Warning(ex, "Disk space check failed");
        }

        return component;
    }

    /// <summary>
    /// Check exports directory health
    /// </summary>
    private async Task<ComponentHealth> CheckExportsDirectoryHealth()
    {
        var component = new ComponentHealth
        {
            Name = "Exports Directory"
        };

        try
        {
            var exportsPath = Path.Combine(AppContext.BaseDirectory, "exports");

            if (!Directory.Exists(exportsPath))
            {
                Directory.CreateDirectory(exportsPath);
                component.Status = "Healthy";
                component.Message = "Created exports directory";
            }
            else
            {
                // Check if directory is writable
                var testFile = Path.Combine(exportsPath, $".health_check_{Guid.NewGuid()}.tmp");
                try
                {
                    await File.WriteAllTextAsync(testFile, "health check");
                    File.Delete(testFile);

                    component.Status = "Healthy";
                    component.Message = "Directory is writable";
                }
                catch (Exception ex)
                {
                    component.Status = "Unhealthy";
                    component.Message = $"Directory not writable: {ex.Message}";
                    Log.Error(ex, "Exports directory not writable");
                }

                // Get file count
                var fileCount = Directory.GetFiles(exportsPath, "*", SearchOption.AllDirectories).Length;
                component.Metadata["fileCount"] = fileCount;

                // Get directory size
                var dirInfo = new DirectoryInfo(exportsPath);
                var totalSize = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                component.Metadata["sizeMB"] = Math.Round(totalSize / 1024.0 / 1024.0, 2);
            }
        }
        catch (Exception ex)
        {
            component.Status = "Degraded";
            component.Message = $"Error checking directory: {ex.Message}";
            Log.Warning(ex, "Exports directory check failed");
        }

        return component;
    }
}

/// <summary>
/// Health status response model
/// </summary>
public class HealthStatus
{
    public string Status { get; set; } = "Unknown";
    public DateTime Timestamp { get; set; }
    public long Uptime { get; set; }
    public long ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, ComponentHealth> Components { get; set; } = new();
}

/// <summary>
/// Individual component health
/// </summary>
public class ComponentHealth
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public string? Message { get; set; }
    public long ResponseTimeMs { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}