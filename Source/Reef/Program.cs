using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Reef.Core.Services;
using Reef.Core.Destinations;
using Reef.Core.Security;
using Reef.Core.Data;
using Reef.Core.Database;
using Reef.Core.TemplateEngines;
using Reef.Api;
using Reef.Helpers;
using Reef.Core.Middleware;
using Serilog;
using Serilog.Sinks.EventLog;
using System.Text;
using System.Runtime.InteropServices;
using Dapper;

namespace Reef;

/// <summary>
/// Main entry point and service configuration
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // Register Dapper type handlers for TimeSpan (must be done before any database operations)
        SqlMapper.AddTypeHandler(new TimeSpanHandler());
        SqlMapper.AddTypeHandler(new NullableTimeSpanHandler());

        // Set working directory to application base
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        // Load temporary configuration for initial logging setup
        var tempConfig = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .Build();

        bool useEventLog = tempConfig.GetValue<bool>("Windows:UseEventLog", false);

        var logDirectory = Path.Combine(AppContext.BaseDirectory, "log");
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        // Configure initial logger from appsettings.json
        var loggerConfig = new LoggerConfiguration();
        
        try
        {
            loggerConfig.ReadFrom.Configuration(tempConfig);
        }
        catch (InvalidOperationException)
        {
            loggerConfig
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(logDirectory, "reef-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && useEventLog)
        {
            try
            {
                loggerConfig.WriteTo.EventLog(source: "Reef", logName: "Application");
            }
            catch
            {
                Console.WriteLine("Warning: Unable to write to Windows Event Log. Falling back to file logging.");
            }
        }

        Log.Logger = loggerConfig.CreateLogger();

        Log.Information("");
        Log.Information("██████╗ ███████╗███████╗███████╗");
        Log.Information("██╔══██╗██╔════╝██╔════╝██╔════╝");
        Log.Information("██████╔╝█████╗  █████╗  █████╗  ");
        Log.Information("██╔══██╗██╔══╝  ██╔══╝  ██╔══╝  ");
        Log.Information("██║  ██║███████╗███████╗██║     ");
        Log.Information("╚═╝  ╚═╝╚══════╝╚══════╝╚═╝     ");
        Log.Information("");

        // Initialize encryption service
        var encryptionService = new EncryptionService(AppContext.BaseDirectory);
        Log.Debug("Encryption service initialized");

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            // Initialize Serilog globally
            builder.Host.UseSerilog();

            // Configure Windows Service (if running as service)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                builder.Host.UseWindowsService(options =>
                {
                    options.ServiceName = "ReefExportService";
                });
            }

            // Get connection string - SINGLE SOURCE OF TRUTH
            var connectionString = builder.Configuration.GetConnectionString("Reef")
                ?? "Data Source=Reef.db";

            // Get configured port for logging
            var port = builder.Configuration.GetValue<int>("Reef:ListenPort", 8085);
            var localhostOnly = builder.Configuration.GetValue<bool>("Reef:LocalhostOnly", false);

            // Configure server URLs only in production (launchSettings.json handles development)
            if (!builder.Environment.IsDevelopment())
            {
                var host = localhostOnly ? "127.0.0.1" : "*";
                builder.WebHost.UseUrls($"http://{host}:{port}");
            }

            // Register services
            RegisterServices(builder.Services, builder.Configuration, encryptionService, connectionString);

            // Add controllers with JSON string enum converter for enums
            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });

            // Also configure minimal API JSON options for string enums
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
            {
                options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            });

            // Configure JWT Authentication
            ConfigureAuthentication(builder.Services, builder.Configuration);

            // Configure CORS
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // Initialize database (run before building the app)
            var dbInitializer = new DatabaseInitializer(connectionString, encryptionService);
            await dbInitializer.InitializeAsync();
            await dbInitializer.SeedSampleDataAsync(); // Optional

            // Seed default email templates
            Log.Debug("Seeding default email templates...");
            var templateService = new NotificationTemplateService(connectionString);
            await templateService.SeedDefaultTemplatesAsync();

            // Run Jobs migration to fix any corrupted NextRunTime values
            Log.Debug("Running Jobs database migration...");
            var jobsMigration = new JobsMigration(connectionString);
            await jobsMigration.ApplyAsync();

            // Log migration stats
            var stats = await jobsMigration.GetStatsAsync();
            Log.Debug("Jobs Migration Stats: {@Stats}", stats);

            // Run PrePostProcessing migration to add pre/post-processing columns
            Log.Debug("Running PrePostProcessing database migration...");
            var prePostMigration = new PrePostProcessingMigration(connectionString);
            await prePostMigration.ApplyAsync();

            // Log pre/post migration stats
            var prePostStats = await prePostMigration.GetStatsAsync();
            Log.Debug("PrePostProcessing Migration Stats: {@Stats}", prePostStats);

            // Run TransformationOptions migration to add TransformationOptionsJson column
            Log.Debug("Running TransformationOptions database migration...");
            var transformationOptionsMigration = new TransformationOptionsMigration(connectionString);
            await transformationOptionsMigration.ApplyAsync();

            // Log transformation options migration stats
            var transformationOptionsStats = await transformationOptionsMigration.GetStatsAsync();
            Log.Debug("TransformationOptions Migration Stats: {@Stats}", transformationOptionsStats);

            // Resolve any corrupted/stuck jobs on startup
            Log.Debug("Checking for corrupted jobs...");
            var jobService = new JobService(new DatabaseConfig { ConnectionString = connectionString }, builder.Configuration);
            await jobService.FixCorruptedNextRunTimesAsync();
            Log.Debug("Done! Job cleanup completed");

            var app = builder.Build();

            // Record application startup time for uptime tracking and get startup token
            var startupToken = await RecordApplicationStartupAsync(connectionString);
            if (!string.IsNullOrEmpty(startupToken))
            {
                JwtTokenService.SetCurrentStartupToken(startupToken);
                Log.Debug("Startup token set for JWT validation");
            }

            // Configure HTTP pipeline
            ConfigureMiddleware(app);

            // Map API endpoints
            MapEndpoints(app);

            var accessNote = localhostOnly ? "(localhost only)" : "(reachable from other devices)";
            Log.Information("Reef is spinning up on http://localhost:{Port} {AccessNote}", port, accessNote);
            Log.Information("Endpoints online: /api and /health");
            Log.Information("");

            // Register shutdown handlers
            var lifetime = app.Lifetime;
            
            lifetime.ApplicationStarted.Register(() =>
            {
                Log.Information("✓ Application started successfully");
                Log.Information("");
            });

            lifetime.ApplicationStopping.Register(() =>
            {
                Log.Information("");
                Log.Information("Exit: Application is stopping...");
            });

            lifetime.ApplicationStopped.Register(() =>
            {
                Log.Information("Application stopped");
            });

            await app.RunAsync();
            
            Log.Information("Exit: Application shutdown complete");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            
            // Show error if Serilog fails
            Console.WriteLine("");
            Console.WriteLine("Fatal error during application startup:");
            Console.WriteLine(ex.ToString());
            
            // If running as console, wait for user input
            if (!OperatingSystem.IsWindows() || Environment.UserInteractive)
            {
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }
            
            Environment.Exit(1);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void RegisterServices(
        IServiceCollection services, 
        IConfiguration configuration, 
        EncryptionService encryptionService,
        string connectionString)
    {
        // Singleton services (one instance for the app lifetime)
        services.AddSingleton(encryptionService);
        services.AddSingleton<DatabaseInitializer>(sp =>
            new DatabaseInitializer(connectionString, encryptionService));
        services.AddSingleton<HashValidator>();
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<ApiKeyValidator>();
        services.AddSingleton(new DatabaseConfig { ConnectionString = connectionString });
        
        // Template engines
        services.AddSingleton<ScribanTemplateEngine>();

        // Scoped services (each request gets its own instance)
        // Core services
        services.AddScoped<QueryExecutor>();
        services.AddScoped<ConnectionService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<ExecutionService>();
        services.AddScoped<WebhookService>();
        services.AddScoped<AuditService>();
        services.AddScoped<AdminService>();
        services.AddScoped<GroupService>();
        services.AddScoped<DeltaSyncService>();
        services.AddScoped<EmailExportService>();
        services.AddScoped<EmailApprovalService>();
        // Notification system - throttler is singleton to maintain state across requests
        services.AddSingleton<NotificationThrottler>();

        // Notification template service
        services.AddScoped<NotificationTemplateService>(sp =>
            new NotificationTemplateService(connectionString));

        // Notification service (depends on template service)
        services.AddScoped<NotificationService>(sp =>
            new NotificationService(
                connectionString,
                sp.GetRequiredService<EncryptionService>(),
                sp.GetRequiredService<NotificationThrottler>(),
                sp.GetRequiredService<NotificationTemplateService>()));

        // Destination services
        services.AddScoped<DestinationService>(sp =>
            new DestinationService(connectionString, sp.GetRequiredService<EncryptionService>()));

        // Query template service
        services.AddScoped<QueryTemplateService>(sp =>
            new QueryTemplateService(
                connectionString, 
                sp.GetRequiredService<HashValidator>(),
                sp.GetRequiredService<ScribanTemplateEngine>()));

        // Jobs services - NEW SYSTEM
        services.AddScoped<JobService>(sp =>
            new JobService(sp.GetRequiredService<DatabaseConfig>(), sp.GetRequiredService<IConfiguration>()));
        
        services.AddScoped<Reef.Api.JobExecutorService>();

        // Background services (hosted services)

        // Profile scheduler (for Profile-based exports)
        // Uncomment if you want to use the old profile scheduler
        // services.AddHostedService<SchedulerService>();

        // Health monitoring
        services.AddHostedService<HealthCheckService>();

        // Database size monitoring - monitors database file size every 30 minutes
        services.AddHostedService<DatabaseSizeMonitorService>();

        // Jobs scheduler (for Job-based scheduled tasks) - NEW
        services.AddSingleton<JobScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<JobScheduler>());

        // Email approval background service - polls for approved emails and sends them
        services.AddHostedService<ApprovedEmailSenderService>();

        Log.Debug("Services registered");
    }

    private static void ConfigureAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwtKey = configuration["Reef:Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        var key = Encoding.UTF8.GetBytes(jwtKey);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = configuration["Reef:Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = configuration["Reef:Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        services.AddAuthorization();

        Log.Debug("Authentication configured");
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        // Global exception handler - always return JSON
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled exception");
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                var error = new { success = false, message = "An unexpected error occurred.", detail = ex.Message };
                var json = System.Text.Json.JsonSerializer.Serialize(error);
                await context.Response.WriteAsync(json);
            }
        });

        // Security headers
        app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            await next();
        });

        // Localhost-only Host header validation (if enabled)
        var localhostOnly = app.Configuration.GetValue<bool>("Reef:LocalhostOnly", false);
        if (localhostOnly)
        {
            var allowedHosts = app.Configuration.GetSection("Reef:AllowedHosts").Get<List<string>>()
                ?? new List<string> { "localhost", "127.0.0.1", "::1" };

            var allowedHostsLower = allowedHosts.Select(h => h.ToLowerInvariant()).ToHashSet();

            app.Use(async (context, next) =>
            {
                var host = context.Request.Host.Host.ToLowerInvariant();

                if (!allowedHostsLower.Contains(host))
                {
                    Log.Warning("Rejected request with Host header: {Host} (LocalhostOnly is enabled)", host);
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { error = "Access denied: localhost only" });
                    return;
                }

                await next();
            });
        }

        // Cache prevention for sensitive API endpoints
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/destinations"))
            {
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "-1";
            }
            await next();
        });

        app.UseCors();
        app.UseStaticFiles(); // Serve wwwroot static files

        // Serve HTML views from Views folder
        var viewsFolder = Path.Combine(AppContext.BaseDirectory, "views");
        if (!Directory.Exists(viewsFolder))
        {
            var parentDir = Directory.GetParent(AppContext.BaseDirectory);
            if (parentDir?.Parent?.Parent != null)
            {
                var projectRoot = parentDir.Parent.Parent.FullName;
                viewsFolder = Path.Combine(projectRoot, "views");
            }
        }

        if (Directory.Exists(viewsFolder))
        {
            Log.Debug("Serving HTML from folder: {ViewsFolder}", viewsFolder);

            var htmlFiles = new[]
            {
                "index.html",
                "dashboard.html",
                "connections.html",
                "documentation.html",
                "email-approvals.html",
                "jobs.html",
                "admin.html",
                "destinations.html",
                "executions.html",
                "groups.html",
                "logoff.html",
                "profiles.html",
                "templates.html",
                "404.html"
            };

            var mappedRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fileName in htmlFiles)
            {
                var filePath = Path.Combine(viewsFolder, fileName);
                if (File.Exists(filePath))
                {
                    var route = "/" + Path.GetFileNameWithoutExtension(fileName);
                    if (mappedRoutes.Add(route))
                    {
                        app.MapGet(route, async context =>
                        {
                            Log.Debug("Serving HTML route: {Route} -> {File}", route, filePath);
                            context.Response.ContentType = "text/html";
                            await context.Response.SendFileAsync(filePath);
                        });
                    }
                }
            }

            // Redirect root "/" to "/index"
            if (mappedRoutes.Contains("/index"))
            {
                app.MapGet("/", () => Results.Redirect("/index"));
            }
        }

        app.UseAuthentication();
        app.UseMiddleware<LastSeenMiddleware>();
        app.UseAuthorization();

        Log.Debug("Middleware configured");
    }

    private static void MapEndpoints(WebApplication app)
    {
        // Health check
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
        }));

        // API endpoints
        AuthEndpoints.Map(app);
        ConnectionsEndpoints.Map(app);
        ProfilesEndpoints.Map(app);
        GroupsEndpoints.Map(app);
        ExecutionsEndpoints.Map(app);
        EmailApprovalsEndpoints.Map(app);
        WebhooksEndpoints.Map(app);
        SystemInfoEndpoints.Map(app);
        AdminEndpoints.Map(app);

        // New endpoints for Jobs, Destinations, and Query Templates (extension methods)
        app.MapJobsEndpoints();
        app.MapDestinationsEndpoints();
        app.MapQueryTemplatesEndpoints();

        // Fallback handler for unmapped routes (404)
        app.MapFallback(async context =>
        {
            var viewsFolder = Path.Combine(AppContext.BaseDirectory, "views");
            if (!Directory.Exists(viewsFolder))
            {
                var projectRoot = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.FullName;
                viewsFolder = Path.Combine(projectRoot, "views");
            }

            var notFoundPath = Path.Combine(viewsFolder, "404.html");
            if (File.Exists(notFoundPath))
            {
                Log.Debug("Serving 404 page for unmapped route: {Route}", context.Request.Path);
                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(notFoundPath);
            }
            else
            {
                // Fallback JSON response if 404.html doesn't exist
                context.Response.StatusCode = 404;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "Not found" });
            }
        });

        Log.Debug("Endpoints mapped");
    }

    private static async Task<string?> RecordApplicationStartupAsync(string connectionString)
    {
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            await conn.OpenAsync();

            // Generate a unique startup token for JWT validation
            var startupToken = Guid.NewGuid().ToString();

            var sql = @"
                INSERT INTO ApplicationStartup (StartupToken, StartedAt, MachineName, Version)
                VALUES (@StartupToken, @StartedAt, @MachineName, @Version)";

            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";

            await conn.ExecuteAsync(sql, new
            {
                StartupToken = startupToken,
                StartedAt = DateTime.UtcNow,
                MachineName = Environment.MachineName,
                Version = version
            });

            Log.Debug("Recorded application startup time for uptime tracking");
            return startupToken;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to record application startup time");
            return null;
        }
    }
}

/// <summary>
/// Database configuration - used by all services
/// </summary>
public class DatabaseConfig
{
    public required string ConnectionString { get; init; }
}