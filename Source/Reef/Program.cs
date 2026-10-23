using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
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
using System.Security.Claims;
using System.Runtime.InteropServices;
using Dapper;

namespace Reef;

/// <summary>
/// Main entry point
/// </summary>
public partial class Program
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
        var loggerConfig = new LoggerConfiguration()
            .Enrich.FromLogContext();

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

        StartupLogHelper.LogApplicationBanner();

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

            // Get connection string (AS SINGLE SOURCE OF TRUTH)
            var dbPath = builder.Configuration["Reef:DatabasePath"] ?? "Reef.db";
            var connectionString = builder.Configuration.GetConnectionString("Reef")
                ?? $"Data Source={dbPath}";

            // Get configured port for logging
            var port = builder.Configuration.GetValue<int>("Reef:ListenPort", 8085);
            var localhostOnly = builder.Configuration.GetValue<bool>("Reef:LocalhostOnly", false);

            // Fail fast with a clean message if the port is already taken
            if (IsPortInUse(port))
            {
                Log.Fatal("Port {Port} is already in use. Stop the existing process, or set Reef__ListenPort to use another port.", port);
                await Log.CloseAndFlushAsync();
                Environment.Exit(1);
            }

            // Configure server URLs only in production (launchSettings.json handles development)
            if (!builder.Environment.IsDevelopment())
            {
                var host = localhostOnly ? "127.0.0.1" : "*";
                builder.WebHost.UseUrls($"http://{host}:{port}");
            }

            // Register services
            RegisterServices(builder.Services, builder.Configuration, encryptionService, connectionString);

            // Configure Forwarded Headers for Reverse Proxy support
            builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.All;
                // Clear KnownNetworks and KnownProxies so it accepts forwarded headers from any proxy
                // In production, you would normally restrict this to specific proxy IPs
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });

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

            // Configure CORS with configured origins
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    var allowedOrigins = builder.Configuration.GetSection("Reef:AllowedOrigins").Get<List<string>>();
                    if (allowedOrigins != null && allowedOrigins.Count > 0)
                    {
                        policy.WithOrigins(allowedOrigins.ToArray())
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                    else
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    }
                });
            });

            // Initialize database (run before building the app)
            var dbInitializer = new DatabaseInitializer(connectionString, encryptionService);
            await dbInitializer.InitializeAsync();
            await dbInitializer.SeedSampleDataAsync();

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

            // Run MFA migration to add MFA columns to Users table
            var mfaMigration = new MfaMigration(connectionString);
            await mfaMigration.ApplyAsync();

            // Run OIDC migration to add the OidcSubject column to Users table
            var oidcMigration = new OidcMigration(connectionString);
            await oidcMigration.ApplyAsync();

            // Run Profile canvas migration to add the CanvasLayoutJson column to Profiles table
            var profileCanvasMigration = new ProfileCanvasMigration(connectionString);
            await profileCanvasMigration.ApplyAsync();

            // Run Scripting migration to add stdout/stderr/exit-code columns for Script processing steps
            Log.Debug("Running Scripting database migration...");
            var scriptingMigration = new ScriptingMigration(connectionString);
            await scriptingMigration.ApplyAsync();

            var scriptingStats = await scriptingMigration.GetStatsAsync();
            Log.Debug("Scripting Migration Stats: {@Stats}", scriptingStats);

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
            await ConfigureMiddleware(app);

            // Map API endpoints
            MapEndpoints(app);

            StartupLogHelper.LogHostingSummary(app, port, localhostOnly);

            app.Lifetime.ApplicationStopping.Register(() =>
            {
                Log.Information("");
                Log.Information("Application shutting down...");
            });

            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal("");
            Log.Fatal(ex, "Application failed to start.");
            Log.CloseAndFlush();
            Environment.Exit(1);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return true;
        }
    }

    private static void RegisterServices(
        IServiceCollection services,
        IConfiguration configuration,
        EncryptionService encryptionService,
        string connectionString)
    {
        // Keys live next to the encryption keys so tokens survive restarts and container rebuilds
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, ".core", "keys")));

        // Singleton services (one instance for the app lifetime)
        services.AddSingleton(encryptionService);
        services.AddSingleton<DatabaseInitializer>(sp =>
            new DatabaseInitializer(connectionString, encryptionService));
        services.AddSingleton<HashValidator>();
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<ApiKeyValidator>();
        services.AddHttpClient();
        services.AddSingleton(new DatabaseConfig { ConnectionString = connectionString });

        // Template engines
        services.AddSingleton<ScribanTemplateEngine>();

        // Document generation services
        services.AddScoped<Reef.Core.DocumentGeneration.PdfGenerator>();
        services.AddScoped<Reef.Core.DocumentGeneration.DocxGenerator>();
        services.AddScoped<Reef.Core.DocumentGeneration.IDocumentGeneratorFactory, Reef.Core.DocumentGeneration.DocumentGeneratorFactory>();
        services.AddScoped<Reef.Core.DocumentGeneration.DocumentTemplateEngine>(sp =>
            new Reef.Core.DocumentGeneration.DocumentTemplateEngine(
                sp.GetRequiredService<ScribanTemplateEngine>(),
                sp.GetRequiredService<Reef.Core.DocumentGeneration.IDocumentGeneratorFactory>(),
                configuration));

        // Scoped services (each request gets its own instance)
        // Core services
        services.AddScoped<ValidationService>();
        services.AddScoped<QueryExecutor>();
        services.AddScoped<ConnectionService>();
        services.AddScoped<ProfileService>();
        services.AddSingleton<Reef.Core.Scripting.IScriptRunner, Reef.Core.Scripting.ProcessScriptRunner>();
        services.AddScoped<ExecutionService>();
        services.AddScoped<WebhookService>();
        services.AddScoped<AuditService>();
        services.AddScoped<AdminService>();
        services.AddScoped<InterpreterService>();
        services.AddScoped<GroupService>();
        services.AddScoped<DeltaSyncService>();
        services.AddScoped<EmailExportService>();
        services.AddScoped<EmailApprovalService>();

        // Import Profile services
        services.AddScoped<ImportProfileService>();
        services.AddScoped<Reef.Core.ProfileDocuments.ProfileDocumentService>();
        services.AddScoped<Reef.Core.Targets.DatabaseImportTarget>();
        services.AddScoped<Reef.Core.Targets.LocalFileImportTarget>();
        services.AddScoped<Reef.Core.Sources.DestinationSourceAdapter>();
        services.AddScoped<ImportFileOutputService>();
        services.AddScoped<ImportExecutionService>();

        // Notification system
        services.AddSingleton<NotificationThrottler>();
        services.AddScoped<NotificationTemplateService>(sp =>
            new NotificationTemplateService(connectionString));

        // Notification service (depends on template service)
        services.AddScoped<NotificationService>(sp =>
            new NotificationService(
                connectionString,
                sp.GetRequiredService<EncryptionService>(),
                sp.GetRequiredService<NotificationThrottler>(),
                sp.GetRequiredService<NotificationTemplateService>(),
                sp.GetRequiredService<ScribanTemplateEngine>()));

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
            // OIDC sign-in sets only the HttpOnly cookie, refresh exchanges it for a bearer token
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (string.IsNullOrEmpty(context.Token)
                        && !context.Request.Headers.ContainsKey("Authorization")
                        && context.Request.Path.Equals("/api/auth/refresh", StringComparison.OrdinalIgnoreCase)
                        && context.Request.Cookies.TryGetValue("reef_token", out var cookie))
                    {
                        context.Token = cookie;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

        Log.Debug("Authentication configured");
    }

    private static async Task ConfigureMiddleware(WebApplication app)
    {
        // Use forwarded headers before any other middleware (required for correct reverse proxy IP/scheme resolution)
        app.UseForwardedHeaders();

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
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/app/assets")
                ? "public, max-age=31536000, immutable"
                : "no-cache"
        });
        // Authentication middleware - validates JWT from cookie and redirects to /logoff if invalid
        app.UseMiddleware<AuthenticationMiddleware>();

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
        OidcEndpoints.Map(app);
        AccountEndpoints.Map(app);
        ConnectionsEndpoints.Map(app);
        ProfilesEndpoints.Map(app);
        GroupsEndpoints.Map(app);
        ExecutionsEndpoints.Map(app);
        EmailApprovalsEndpoints.Map(app);
        WebhooksEndpoints.Map(app);
        SystemInfoEndpoints.Map(app);
        AdminEndpoints.Map(app);
        app.MapJobsEndpoints();
        app.MapDestinationsEndpoints();
        app.MapQueryTemplatesEndpoints();
        ImportProfilesEndpoints.Map(app);
        ProfileDocumentsEndpoints.Map(app);

        var spaIndex = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "app", "index.html");
        app.MapFallback(async context =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path.StartsWithSegments("/api") || !File.Exists(spaIndex))
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsJsonAsync(new { error = "Not found" });
                return;
            }

            context.Response.ContentType = "text/html";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.SendFileAsync(spaIndex);
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
                Environment.MachineName,
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
/// Database configuration
/// </summary>
public class DatabaseConfig
{
    public required string ConnectionString { get; init; }
}