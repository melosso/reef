// Source/Reef/Core/Services/DatabaseSizeMonitorService.cs
// Background service that monitors database file size and sends notifications

using Microsoft.Extensions.Hosting;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Background service that monitors database size and sends notifications
/// Runs every 30 minutes to check if database exceeds configured threshold
/// Only sends notification if size exceeds threshold (max once per hour via throttling)
/// </summary>
public class DatabaseSizeMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly string _databasePath;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<DatabaseSizeMonitorService>();

    public DatabaseSizeMonitorService(
        IServiceScopeFactory serviceScopeFactory,
        DatabaseConfig databaseConfig)
    {
        _serviceScopeFactory = serviceScopeFactory;

        // Extract database path from connection string
        var connString = databaseConfig.ConnectionString;
        var dataSourceStart = connString.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase) + "Data Source=".Length;
        var dataSourceEnd = connString.IndexOf(";", dataSourceStart);
        if (dataSourceEnd == -1) dataSourceEnd = connString.Length;
        _databasePath = connString.Substring(dataSourceStart, dataSourceEnd - dataSourceStart).Trim();

        Log.Debug("DatabaseSizeMonitorService initialized. Database path: {DatabasePath}", _databasePath);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait a bit before first check (let app startup complete)
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // App is shutting down before first check
                Log.Debug("DatabaseSizeMonitorService cancelled before first check");
                return;
            }

            // Check every 30 minutes
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(_databasePath))
                    {
                        var fileInfo = new FileInfo(_databasePath);
                        var sizeBytes = fileInfo.Length;
                        var sizeMb = sizeBytes / (1024 * 1024);

                        Log.Debug("Database size check: {SizeMB} MB ({SizeBytes} bytes)", sizeMb, sizeBytes);

                        // Send notification if over threshold (throttler will prevent excessive notifications)
                        // Create scope to resolve scoped NotificationService
                        using var scope = _serviceScopeFactory.CreateScope();
                        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
                        await notificationService.NotifyDatabaseSizeAsync(sizeBytes);
                    }
                    else
                    {
                        Log.Warning("Database file not found at: {DatabasePath}", _databasePath);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error checking database size");
                }

                // Wait 30 minutes before next check
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // App is shutting down
                    Log.Debug("DatabaseSizeMonitorService is shutting down");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation during shutdown - don't log as error
            Log.Debug("DatabaseSizeMonitorService cancelled during operation");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception in DatabaseSizeMonitorService");
            throw;
        }
        finally
        {
            Log.Debug("DatabaseSizeMonitorService stopped");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        return base.StopAsync(cancellationToken);
    }
}
