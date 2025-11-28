// Source/Reef/Core/Services/ConfigurationValidator.cs
// Validates application configuration on startup

using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Validates application configuration on startup
/// Ensures all required settings are present and valid
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>
    /// Validate configuration and throw if invalid
    /// </summary>
    public static void ValidateConfiguration(IConfiguration configuration)
    {
        var errors = new List<string>();

        // Validate database configuration
        var dbPath = configuration.GetValue<string>("Reef:DatabasePath");
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            errors.Add("Reef:DatabasePath is required");
        }

        // Validate JWT configuration
        var jwtSecretKey = configuration.GetValue<string>("Reef:Jwt:SecretKey");
        if (string.IsNullOrWhiteSpace(jwtSecretKey))
        {
            errors.Add("Reef:Jwt:SecretKey is required");
        }
        else if (jwtSecretKey.Length < 32)
        {
            errors.Add("Reef:Jwt:SecretKey must be at least 32 characters long");
        }

        var jwtIssuer = configuration.GetValue<string>("Reef:Jwt:Issuer");
        if (string.IsNullOrWhiteSpace(jwtIssuer))
        {
            errors.Add("Reef:Jwt:Issuer is required");
        }

        var jwtAudience = configuration.GetValue<string>("Reef:Jwt:Audience");
        if (string.IsNullOrWhiteSpace(jwtAudience))
        {
            errors.Add("Reef:Jwt:Audience is required");
        }

        // Validate server configuration
        var serverPort = configuration.GetValue<int?>("Reef:ListenPort");
        if (!serverPort.HasValue || serverPort.Value <= 0 || serverPort.Value > 65535)
        {
            errors.Add("Reef:ListenPort must be between 1 and 65535");
        }

        // Validate encryption key environment variable
        var encryptionKey = Environment.GetEnvironmentVariable("REEF_ENCRYPTION_KEY");
        if (string.IsNullOrWhiteSpace(encryptionKey))
        {
            Log.Warning("REEF_ENCRYPTION_KEY environment variable is not set - encryption will use default key (NOT RECOMMENDED for production)");
        }
        else if (encryptionKey.Length < 32)
        {
            errors.Add("REEF_ENCRYPTION_KEY must be at least 32 characters long");
        }

        // Validate CORS configuration
        var corsOrigins = configuration.GetSection("Reef:Security:AllowedOrigins").Get<string[]>();
        if (corsOrigins == null || corsOrigins.Length == 0)
        {
            Log.Warning("Reef:Security:AllowedOrigins is not configured - CORS will be disabled");
        }

        // Validate scheduler configuration
        var schedulerEnabled = configuration.GetValue<bool?>("Reef:Scheduler:Enabled");
        if (!schedulerEnabled.HasValue)
        {
            Log.Warning("Reef:Scheduler:Enabled is not configured - using default value (true)");
        }

        var schedulerIntervalSeconds = configuration.GetValue<int?>("Reef:Scheduler:CheckIntervalSeconds");
        if (schedulerIntervalSeconds.HasValue && (schedulerIntervalSeconds.Value < 10 || schedulerIntervalSeconds.Value > 3600))
        {
            Log.Warning("Reef:Scheduler:CheckIntervalSeconds should be between 10 and 3600 seconds");
        }

        // Validate jobs configuration
        var jobsIntervalSeconds = configuration.GetValue<int?>("Reef:Jobs:CheckIntervalSeconds");
        if (jobsIntervalSeconds.HasValue && (jobsIntervalSeconds.Value < 5 || jobsIntervalSeconds.Value > 300))
        {
            Log.Warning("Reef:Jobs:CheckIntervalSeconds should be between 5 and 300 seconds");
        }

        var maxConcurrentJobs = configuration.GetValue<int?>("Reef:Jobs:MaxConcurrentJobs");
        if (maxConcurrentJobs.HasValue && (maxConcurrentJobs.Value < 1 || maxConcurrentJobs.Value > 100))
        {
            Log.Warning("Reef:Jobs:MaxConcurrentJobs should be between 1 and 100");
        }

        // Validate exports directory
        var exportsPath = configuration.GetValue<string>("Reef:ExportsPath");
        if (string.IsNullOrWhiteSpace(exportsPath))
        {
            Log.Warning("Reef:ExportsPath is not configured - using default 'exports' directory");
        }
        else
        {
            try
            {
                // Try to create exports directory if it doesn't exist
                var fullPath = Path.IsPathRooted(exportsPath) 
                    ? exportsPath 
                    : Path.Combine(AppContext.BaseDirectory, exportsPath);

                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                    Log.Information("Created exports directory: {Path}", fullPath);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot create exports directory '{exportsPath}': {ex.Message}");
            }
        }

        // Validate log configuration
        var logPath = configuration.GetValue<string>("Serilog:WriteTo:0:Args:path");
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            try
            {
                var logDir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(logDir) && !Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                    Log.Information("Created log directory: {Path}", logDir);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Cannot create log directory for path '{Path}'", logPath);
            }
        }

        // Throw if there are any critical errors
        if (errors.Count > 0)
        {
            var errorMessage = "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  - {e}"));
            Log.Fatal(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        Log.Debug("✓ Configuration validation passed");
    }

    /// <summary>
    /// Validate configuration warnings (non-critical)
    /// </summary>
    public static void ValidateConfigurationWarnings(IConfiguration configuration)
    {
        var warnings = new List<string>();

        // Check for development mode
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        if (environment == "Development")
        {
            warnings.Add("Running in Development mode - ensure this is not a production deployment");
        }

        // Check JWT expiration
        var jwtExpirationMinutes = configuration.GetValue<int?>("Reef:Jwt:ExpirationMinutes");
        if (!jwtExpirationMinutes.HasValue || jwtExpirationMinutes.Value > 1440)
        {
            warnings.Add("JWT token expiration is very long - consider shortening for better security");
        }

        // Check for insecure CORS configuration
        var corsOrigins = configuration.GetSection("Reef:Security:AllowedOrigins").Get<string[]>();
        if (corsOrigins != null && corsOrigins.Any(o => o == "*"))
        {
            warnings.Add("CORS is configured to allow all origins (*) - this is insecure for production");
        }

        // Check scheduler interval
        var schedulerIntervalSeconds = configuration.GetValue<int?>("Reef:Scheduler:CheckIntervalSeconds");
        if (schedulerIntervalSeconds.HasValue && schedulerIntervalSeconds.Value < 60)
        {
            warnings.Add("Scheduler interval is very short - this may cause high CPU usage");
        }

        // Log warnings
        foreach (var warning in warnings)
        {
            Log.Warning("Configuration warning: {Warning}", warning);
        }
    }

    /// <summary>
    /// Get configuration summary for logging
    /// </summary>
    public static Dictionary<string, object> GetConfigurationSummary(IConfiguration configuration)
    {
        return new Dictionary<string, object>
        {
            { "Database", configuration.GetValue<string>("Reef:DatabasePath") ?? "Reef.db" },
            { "ListenPort", configuration.GetValue<int>("Reef:ListenPort", 8085) },
            { "ListenHost", configuration.GetValue<string>("Reef:ListenHost", "*") },
            { "Reef:Scheduler:Enabled", configuration.GetValue<bool>("Reef:Scheduler:Enabled", true) },
            { "Reef:Scheduler:CheckIntervalSeconds", configuration.GetValue<int>("Reef:Scheduler:CheckIntervalSeconds", 60) },
            { "Reef:Jobs:CheckIntervalSeconds", configuration.GetValue<int>("Reef:Jobs:CheckIntervalSeconds", 10) },
            { "Reef:Jobs:MaxConcurrentJobs", configuration.GetValue<int>("Reef:Jobs:MaxConcurrentJobs", 10) },
            { "Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production" },
            { "Security:AllowedOrigins", string.Join(", ", configuration.GetSection("Reef:Security:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>()) }
        };
    }
}