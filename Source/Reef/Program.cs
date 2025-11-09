using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Reef.Core.Services;
using Reef.Core.Destinations;
using Reef.Core.Security;
using Reef.Core.Data;
using Reef.Core.Database;
using Reef.Core.TemplateEngines;
using Reef.Core.Abstractions;
using Reef.Core.Services.Import;
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
                    retainedFileCountLimit: 30);
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
        Log.Debug("✓ Encryption service initialized");

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

            // Get configured port
            var port = builder.Configuration.GetValue<int>("Reef:ListenPort", 8085);

            // Explicitly configure Kestrel to listen on the specified port
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(port);
            });

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

            // Configure HTTP pipeline
            ConfigureMiddleware(app);

            // Map API endpoints
            MapEndpoints(app);

            Log.Information("✓ Reef is running on http://localhost:{Port}", port);
            Log.Information("✓ API: http://localhost:{Port}/api", port);
            Log.Information("✓ Health: http://localhost:{Port}/health", port);
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

        // Import services - NEW SYSTEM
        services.AddScoped<IImportProfileService>(sp =>
            new Reef.Core.Services.Import.ImportProfileService(
                sp.GetRequiredService<DatabaseConfig>(),
                sp.GetRequiredService<HashValidator>()));

        services.AddScoped<IImportExecutionService>(sp =>
            new Reef.Core.Services.Import.ImportExecutionService(
                sp.GetRequiredService<DatabaseConfig>(),
                sp.GetRequiredService<IImportProfileService>(),
                sp.GetRequiredService<EncryptionService>()));

        // Background services (hosted services)
        
        // Profile scheduler (for Profile-based exports)
        // Uncomment if you want to use the old profile scheduler
        // services.AddHostedService<SchedulerService>();
        
        // Health monitoring
        services.AddHostedService<HealthCheckService>();
        
        // Jobs scheduler (for Job-based scheduled tasks) - NEW
        services.AddSingleton<JobScheduler>();
        services.AddHostedService(sp => sp.GetRequiredService<JobScheduler>());

        Log.Debug("✓ Services registered");
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

        Log.Debug("✓ Authentication configured");
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

        // Cache prevention headers for sensitive API endpoints (prevents browser caching of encrypted data)
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
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseMiddleware<LastSeenMiddleware>();
        app.UseAuthorization();

        Log.Debug("✓ Middleware configured");
    }

    private static void MapEndpoints(WebApplication app)
    {
        // Root endpoint - Redirect to dashboard
        app.MapGet("/", () => Results.Redirect("/index.html"));

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
        WebhooksEndpoints.Map(app);
        AdminEndpoints.Map(app);

        // New endpoints for Jobs, Destinations, and Query Templates (extension methods)
        app.MapJobsEndpoints();
        app.MapDestinationsEndpoints();
        app.MapQueryTemplatesEndpoints();

        // Import endpoints (new system)
        app.MapImportEndpoints();

        Log.Debug("✓ Endpoints mapped");
    }
}

/// <summary>
/// Database configuration - used by all services
/// </summary>
public class DatabaseConfig
{
    public required string ConnectionString { get; init; }
}