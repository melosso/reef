using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Recipes;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Pure orchestration for the Store guided recipe wizard. Delegates every actual
/// create/update to the existing service for that entity type (ConnectionService,
/// ImportProfileService, DestinationService, QueryTemplateService, GroupService,
/// ProfileService, JobService, WebhookService) - this class adds no new
/// entity-creation logic of its own. The one exception is the staging-table DDL step,
/// which is genuinely new: Reef's import pipeline never auto-creates target tables
/// (DatabaseImportTarget only checks existence), so the wizard must issue the
/// CREATE TABLE itself.
/// </summary>
public class RecipeService
{
    private readonly string _connectionString;
    private readonly ConnectionService _connectionService;
    private readonly GroupService _groupService;
    private readonly DestinationService _destinationService;
    private readonly ImportProfileService _importProfileService;
    private readonly QueryTemplateService _queryTemplateService;
    private readonly ProfileService _profileService;
    private readonly JobService _jobService;
    private readonly WebhookService _webhookService;
    private readonly EncryptionService _encryptionService;
    private readonly IReadOnlyDictionary<RecipeVerifierKind, IRecipeVerifier> _verifiers;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<RecipeService>();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RecipeService(
        DatabaseConfig config,
        ConnectionService connectionService,
        GroupService groupService,
        DestinationService destinationService,
        ImportProfileService importProfileService,
        QueryTemplateService queryTemplateService,
        ProfileService profileService,
        JobService jobService,
        WebhookService webhookService,
        EncryptionService encryptionService,
        ConnectionVerifier connectionVerifier,
        HttpSourceVerifier httpSourceVerifier,
        StagingTableVerifier stagingTableVerifier,
        EmailDestinationVerifier emailDestinationVerifier,
        ScribanTemplateVerifier scribanTemplateVerifier,
        ExportQueryVerifier exportQueryVerifier)
    {
        _connectionString = config.ConnectionString;
        _connectionService = connectionService;
        _groupService = groupService;
        _destinationService = destinationService;
        _importProfileService = importProfileService;
        _queryTemplateService = queryTemplateService;
        _profileService = profileService;
        _jobService = jobService;
        _webhookService = webhookService;
        _encryptionService = encryptionService;

        _verifiers = new Dictionary<RecipeVerifierKind, IRecipeVerifier>
        {
            [RecipeVerifierKind.Connection] = connectionVerifier,
            [RecipeVerifierKind.HttpSource] = httpSourceVerifier,
            [RecipeVerifierKind.StagingTable] = stagingTableVerifier,
            [RecipeVerifierKind.EmailDestination] = emailDestinationVerifier,
            [RecipeVerifierKind.ScribanTemplate] = scribanTemplateVerifier,
            [RecipeVerifierKind.ExportQuery] = exportQueryVerifier
        };
    }

    // Recipe listing

    public async Task<List<RecipeListItem>> GetAvailableRecipesAsync(int? userId, CancellationToken ct = default)
    {
        var runs = await GetRunsForUserAsync(userId, ct);

        return RecipeRegistry.All.Select(recipe =>
        {
            var existingRun = runs
                .Where(r => r.RecipeKey.Equals(recipe.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefault();

            // "Completed" lookup is independent of existingRun (which only tracks the most
            // recent run regardless of status) - a Reconfigure action should stay offered
            // even after the user has started a newer InProgress run for the same recipe.
            var lastCompletedRun = runs
                .Where(r => r.RecipeKey.Equals(recipe.Key, StringComparison.OrdinalIgnoreCase) && r.Status == "Completed")
                .OrderByDescending(r => r.CompletedAt)
                .FirstOrDefault();

            return new RecipeListItem
            {
                Key = recipe.Key,
                Name = recipe.Name,
                Description = recipe.Description,
                StepCount = recipe.Steps.Count,
                ExistingRunId = existingRun?.Status == "InProgress" ? existingRun.Id : null,
                LastCompletedRunId = lastCompletedRun?.Id
            };
        }).ToList();
    }

    private async Task<List<RecipeRun>> GetRunsForUserAsync(int? userId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT * FROM RecipeRuns WHERE (@UserId IS NULL AND CreatedBy IS NULL) OR CreatedBy = @UserId ORDER BY UpdatedAt DESC";
        var rows = await conn.QueryAsync<RecipeRun>(new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        return rows.ToList();
    }

    // Run lifecycle

    /// <summary>
    /// Starts a new run for a recipe. When <paramref name="cloneFromRunId"/> is given (the
    /// "Reconfigure" action on a Completed run), the new run's StepStateJson is pre-seeded
    /// from that prior run's saved step params/entity ids as defaults - WooCommerce-specific
    /// config (the ImportProfile steps' consumer key/secret/store URL, carried in their
    /// sourceConfig param) is left for the user to overwrite when they re-save those steps,
    /// since "different store" means different credentials, not different Connections/Groups.
    /// Cloned steps start unverified (Verified=false) even though EntityId/Params carry over -
    /// pointing an existing entity at new WooCommerce credentials genuinely changes its
    /// behavior, so the prior run's verification result no longer applies until re-checked.
    /// </summary>
    public async Task<RecipeRun> StartRecipeAsync(string recipeKey, int? userId, int? cloneFromRunId = null, CancellationToken ct = default)
    {
        var recipe = RecipeRegistry.GetByKey(recipeKey)
            ?? throw new InvalidOperationException($"Unknown recipe '{recipeKey}'.");

        var seedStepState = await BuildCloneSeedStateAsync(recipe, cloneFromRunId, ct);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var now = DateTime.UtcNow;
        const string sql = @"
            INSERT INTO RecipeRuns (RecipeKey, Status, CurrentStepKey, StepStateJson, CreatedAt, UpdatedAt, CreatedBy)
            VALUES (@RecipeKey, 'InProgress', @CurrentStepKey, @StepStateJson, @CreatedAt, @UpdatedAt, @CreatedBy);
            SELECT last_insert_rowid();";

        var id = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new
        {
            RecipeKey = recipe.Key,
            CurrentStepKey = recipe.Steps.First().StepKey,
            StepStateJson = JsonSerializer.Serialize(seedStepState, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId
        }, cancellationToken: ct));

        Log.Information(
            "Recipe run started: {RecipeKey} (RunId: {RunId}) by user {UserId}{ClonedFrom}",
            recipe.Key, id, userId, cloneFromRunId is { } cf ? $" (cloned from run {cf})" : "");
        return await GetRunOrThrowAsync(id, ct);
    }

    private async Task<Dictionary<string, RecipeStepState>> BuildCloneSeedStateAsync(RecipeDefinition recipe, int? cloneFromRunId, CancellationToken ct)
    {
        if (cloneFromRunId is not { } sourceRunId)
            return new();

        var sourceRun = await GetRunOrThrowAsync(sourceRunId, ct);
        if (!sourceRun.RecipeKey.Equals(recipe.Key, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Run {sourceRunId} belongs to a different recipe and cannot be used to reconfigure '{recipe.Key}'.");

        var sourceState = DeserializeStepState(sourceRun.StepStateJson);
        var seeded = new Dictionary<string, RecipeStepState>();

        foreach (var step in recipe.Steps)
        {
            if (sourceState.GetValueOrDefault(step.StepKey) is not { } sourceStepState)
                continue;

            // Carry over the entity id and saved params as defaults, but never the verified
            // flag - re-pointing at a different store's credentials means the entity's
            // current configuration is unverified until the user re-saves and re-verifies it.
            seeded[step.StepKey] = new RecipeStepState
            {
                EntityId = sourceStepState.EntityId,
                Params = new Dictionary<string, object?>(sourceStepState.Params),
                Verified = false,
                Skipped = sourceStepState.Skipped
            };
        }

        return seeded;
    }

    public async Task<RecipeRunStateDto> GetRunStateAsync(int runId, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var recipe = RecipeRegistry.GetByKey(run.RecipeKey)
            ?? throw new InvalidOperationException($"Recipe '{run.RecipeKey}' no longer exists.");

        var stepState = DeserializeStepState(run.StepStateJson);

        return new RecipeRunStateDto
        {
            RunId = run.Id,
            RecipeKey = run.RecipeKey,
            RecipeName = recipe.Name,
            Status = run.Status,
            CurrentStepKey = run.CurrentStepKey,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            CompletedAt = run.CompletedAt,
            Steps = recipe.Steps.Select(step => ToDto(step, stepState.GetValueOrDefault(step.StepKey) ?? new RecipeStepState())).ToList()
        };
    }

    public async Task CompleteRunAsync(int runId, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var recipe = RecipeRegistry.GetByKey(run.RecipeKey)
            ?? throw new InvalidOperationException($"Recipe '{run.RecipeKey}' no longer exists.");

        var stepState = DeserializeStepState(run.StepStateJson);
        var blockingStep = recipe.Steps.FirstOrDefault(s =>
            !s.IsOptional && s.VerifierKind is not null &&
            stepState.GetValueOrDefault(s.StepKey)?.Verified != true);

        if (blockingStep is not null)
            throw new InvalidOperationException($"Step '{blockingStep.Title}' must be verified before completing the recipe.");

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE RecipeRuns SET Status = 'Completed', UpdatedAt = @Now, CompletedAt = @Now WHERE Id = @Id",
            new { Id = runId, Now = now }, cancellationToken: ct));

        Log.Information("Recipe run completed: {RunId} ({RecipeKey})", runId, run.RecipeKey);
    }

    public async Task AbandonRunAsync(int runId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE RecipeRuns SET Status = 'Abandoned', UpdatedAt = @Now WHERE Id = @Id",
            new { Id = runId, Now = DateTime.UtcNow }, cancellationToken: ct));

        Log.Information("Recipe run abandoned: {RunId}", runId);
    }

    // Step execution (create-or-update the step's entity)

    public async Task<RecipeStepStateDto> ExecuteStepAsync(int runId, string stepKey, Dictionary<string, object?> stepParams, int? userId, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var recipe = RecipeRegistry.GetByKey(run.RecipeKey)
            ?? throw new InvalidOperationException($"Recipe '{run.RecipeKey}' no longer exists.");

        var step = recipe.Steps.FirstOrDefault(s => s.StepKey == stepKey)
            ?? throw new InvalidOperationException($"Step '{stepKey}' is not part of recipe '{run.RecipeKey}'.");

        var stepState = DeserializeStepState(run.StepStateJson);
        var existing = stepState.GetValueOrDefault(stepKey);

        int? entityId = step.EntityType switch
        {
            RecipeEntityType.Connection => await SaveConnectionAsync(existing, stepParams, userId, ct),
            RecipeEntityType.Group => await SaveGroupAsync(existing, stepParams, ct),
            RecipeEntityType.Destination => await SaveDestinationAsync(existing, stepParams, stepState, ct),
            RecipeEntityType.StagingTable => await CreateStagingTableAsync(stepKey, stepParams, ct),
            RecipeEntityType.ImportProfile => await SaveImportProfileAsync(stepKey, existing, stepParams, userId, ct),
            RecipeEntityType.QueryTemplate => await SaveQueryTemplateAsync(recipe.Key, stepKey, existing, stepParams, ct),
            RecipeEntityType.Profile => await SaveProfileAsync(recipe.Key, stepKey, existing, stepParams, userId, ct),
            RecipeEntityType.Job => await SaveJobAsync(stepParams, ct),
            _ => existing?.EntityId
        };

        var newState = new RecipeStepState
        {
            EntityId = entityId,
            // Steps with no verifier (Group, optional Jobs) are considered verified as soon
            // as they're saved - there's nothing left to live-check. Steps with a verifier
            // always require a fresh VerifyStepAsync call after a save, since the entity
            // just changed and the previous verification result no longer applies.
            Verified = step.VerifierKind is null,
            LastVerifiedAt = step.VerifierKind is null ? existing?.LastVerifiedAt : null,
            LastVerifyMessage = step.VerifierKind is null ? existing?.LastVerifyMessage : null,
            Params = stepParams,
            Skipped = false
        };

        stepState[stepKey] = newState;
        await PersistStepStateAsync(runId, stepKey, recipe, stepState, ct);

        return ToDto(step, newState);
    }

    public async Task<RecipeStepStateDto> SkipStepAsync(int runId, string stepKey, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var recipe = RecipeRegistry.GetByKey(run.RecipeKey)
            ?? throw new InvalidOperationException($"Recipe '{run.RecipeKey}' no longer exists.");

        var step = recipe.Steps.FirstOrDefault(s => s.StepKey == stepKey)
            ?? throw new InvalidOperationException($"Step '{stepKey}' is not part of recipe '{run.RecipeKey}'.");

        if (!step.IsOptional)
            throw new InvalidOperationException($"Step '{step.Title}' is required and cannot be skipped.");

        var stepState = DeserializeStepState(run.StepStateJson);
        var newState = new RecipeStepState { Skipped = true };
        stepState[stepKey] = newState;
        await PersistStepStateAsync(runId, stepKey, recipe, stepState, ct);

        return ToDto(step, newState);
    }

    // Verification

    public async Task<RecipeStepStateDto> VerifyStepAsync(int runId, string stepKey, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var recipe = RecipeRegistry.GetByKey(run.RecipeKey)
            ?? throw new InvalidOperationException($"Recipe '{run.RecipeKey}' no longer exists.");

        var step = recipe.Steps.FirstOrDefault(s => s.StepKey == stepKey)
            ?? throw new InvalidOperationException($"Step '{stepKey}' is not part of recipe '{run.RecipeKey}'.");

        if (step.VerifierKind is not { } verifierKind)
            throw new InvalidOperationException($"Step '{step.Title}' has no live verification.");

        var stepState = DeserializeStepState(run.StepStateJson);
        var current = stepState.GetValueOrDefault(stepKey)
            ?? throw new InvalidOperationException($"Step '{step.Title}' has not been saved yet.");

        var verifyContext = BuildVerifyContext(recipe.Key, step, current, stepState);
        var verifier = _verifiers[verifierKind];
        var result = await verifier.VerifyAsync(verifyContext, ct);

        current.Verified = result.Success;
        current.LastVerifiedAt = DateTime.UtcNow;
        current.LastVerifyMessage = result.Success ? result.Message : $"{result.Message}";
        stepState[stepKey] = current;

        await PersistStepStateAsync(runId, stepKey, recipe, stepState, ct, advanceOnSuccess: result.Success);

        Log.Information("Recipe run {RunId} step {StepKey} verified={Success}: {Message}", runId, stepKey, result.Success, result.Message);

        return ToDto(step, current);
    }

    private RecipeVerifyContext BuildVerifyContext(string recipeKey, RecipeStepDefinition step, RecipeStepState current, Dictionary<string, RecipeStepState> allSteps)
    {
        // The Connection step's entity is the staging Connection used by several
        // later steps' verifiers (StagingTable, ScribanTemplate, ExportQuery).
        var connectionId = allSteps.GetValueOrDefault("connection")?.EntityId;

        // Only recipes with an actual staging-table step (WooCommerceRecipe's Flow A/B) need
        // a TableName here - fall back to the *recipe's own* staging-table step's saved name,
        // never a hardcoded default, so a recipe with no staging table (ErrorDigestRecipe,
        // which queries the user's own existing tables) correctly gets TableName = null instead
        // of being defaulted to "StoreOrders" by accident just because it reuses the
        // "query-template"/"export-profile" step keys.
        var tableName = step.StepKey switch
        {
            "staging-table" => current.Params.GetValueOrDefault("tableName") as string,
            "query-template" => allSteps.GetValueOrDefault("staging-table")?.Params.GetValueOrDefault("tableName") as string,
            "export-profile" => allSteps.GetValueOrDefault("staging-table")?.Params.GetValueOrDefault("tableName") as string,
            "shipments-staging-table" => current.Params.GetValueOrDefault("tableName") as string,
            "shipments-query-template" => allSteps.GetValueOrDefault("shipments-staging-table")?.Params.GetValueOrDefault("tableName") as string,
            "shipments-export-profile" => allSteps.GetValueOrDefault("shipments-staging-table")?.Params.GetValueOrDefault("tableName") as string,
            _ => null
        };

        var mockTemplateRow = recipeKey.Equals(ErrorDigestRecipe.Key, StringComparison.OrdinalIgnoreCase)
            ? ErrorDigestRecipe.MockDigestRow()
            : null;

        return new RecipeVerifyContext
        {
            EntityId = current.EntityId,
            ConnectionId = connectionId,
            TableName = tableName,
            Params = current.Params,
            MockTemplateRow = mockTemplateRow
        };
    }

    // Entity save helpers - each delegates entirely to the existing entity service

    private async Task<int?> SaveConnectionAsync(RecipeStepState? existing, Dictionary<string, object?> p, int? userId, CancellationToken ct)
    {
        var name = RequireString(p, "name");
        var type = RequireString(p, "type");
        var connectionString = RequireString(p, "connectionString");

        if (existing?.EntityId is { } id)
        {
            var conn = await _connectionService.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("Connection no longer exists.");
            conn.Name = name;
            conn.Type = type;
            conn.ConnectionString = connectionString;
            await _connectionService.UpdateAsync(conn, ct);
            return id;
        }

        var newConn = new Connection { Name = name, Type = type, ConnectionString = connectionString, Hash = "" };
        return await _connectionService.CreateAsync(newConn, userId, ct);
    }

    private async Task<int?> SaveGroupAsync(RecipeStepState? existing, Dictionary<string, object?> p, CancellationToken ct)
    {
        var name = RequireString(p, "name");
        var description = p.GetValueOrDefault("description") as string ?? "";

        if (existing?.EntityId is { } id)
        {
            var group = await _groupService.GetByIdAsync(id) ?? throw new InvalidOperationException("Group no longer exists.");
            group.Name = name;
            group.Description = description;
            await _groupService.UpdateAsync(group);
            return id;
        }

        var newGroup = new ProfileGroup { Name = name, Description = description };
        return await _groupService.CreateAsync(newGroup, "store-wizard");
    }

    private async Task<int?> SaveDestinationAsync(RecipeStepState? existing, Dictionary<string, object?> p, Dictionary<string, RecipeStepState> allSteps, CancellationToken ct)
    {
        // Destination-reuse: if the user explicitly chose to reuse an Email destination
        // already created earlier in *this run* (Flow A's "destination" step), just point
        // this step's state at that same entity instead of saving a second Destination.
        // This only ever looks at the current run's own StepStateJson - no cross-run search.
        if (p.GetValueOrDefault("reuseExistingDestinationId") is { } reuseRaw && TryToInt(reuseRaw, out var reuseId))
        {
            var reused = await _destinationService.GetByIdForExecutionAsync(reuseId, ct)
                ?? throw new InvalidOperationException($"Destination {reuseId} not found.");
            return reused.Id;
        }

        var name = RequireString(p, "name");
        var configurationJson = RequireString(p, "configurationJson");

        if (existing?.EntityId is { } id)
        {
            var dest = await _destinationService.GetByIdForExecutionAsync(id, ct) ?? throw new InvalidOperationException("Destination no longer exists.");
            dest.Name = name;
            dest.ConfigurationJson = configurationJson;
            await _destinationService.UpdateAsync(dest, ct);
            return id;
        }

        var newDest = new Destination { Name = name, Type = DestinationType.Email, ConfigurationJson = configurationJson };
        var created = await _destinationService.CreateAsync(newDest, ct);
        return created.Id;
    }

    /// <summary>
    /// Finds an Email Destination already created earlier in this same run (Flow A's shared
    /// "destination" step), so the wizard can offer "reuse it?" instead of forcing the user
    /// to re-enter SMTP credentials when starting Flow B. Pure "did THIS run already create
    /// one" lookup - no cross-run search, since the "destination" step is IsShared=true and
    /// every flow in this recipe writes to the same StepStateJson["destination"] entry.
    /// </summary>
    public async Task<int?> GetReusableDestinationIdAsync(int runId, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var stepState = DeserializeStepState(run.StepStateJson);
        var destStep = stepState.GetValueOrDefault("destination");
        return destStep?.EntityId;
    }

    private static bool TryToInt(object? value, out int result)
    {
        switch (value)
        {
            case int i: result = i; return true;
            case JsonElement je when je.TryGetInt32(out var i): result = i; return true;
            case string s when int.TryParse(s, out var i): result = i; return true;
            default: result = 0; return false;
        }
    }

    private static bool IsShipmentsStep(string stepKey) => stepKey.StartsWith("shipments-", StringComparison.OrdinalIgnoreCase);

    private async Task<int?> CreateStagingTableAsync(string stepKey, Dictionary<string, object?> p, CancellationToken ct)
    {
        var isShipments = IsShipmentsStep(stepKey);
        var defaultTableName = isShipments ? WooCommerceRecipe.ShipmentsStagingTableName : WooCommerceRecipe.StagingTableName;

        var connectionId = RequireInt(p, "connectionId");
        var tableName = p.GetValueOrDefault("tableName") as string ?? defaultTableName;

        var connection = await _connectionService.GetByIdAsync(connectionId, ct)
            ?? throw new InvalidOperationException($"Connection {connectionId} not found.");

        var exists = await TableExistsAsync(connection, tableName, ct);
        if (!exists)
        {
            var baseDdl = isShipments
                ? WooCommerceRecipe.GetShipmentsStagingTableDdl(connection.Type)
                : WooCommerceRecipe.GetStagingTableDdl(connection.Type);
            var ddl = baseDdl.Replace(defaultTableName, tableName);
            await ExecuteDdlAsync(connection, ddl, ct);
            Log.Information("Recipe wizard created staging table {Table} on connection {ConnectionId}", tableName, connectionId);
        }

        // StagingTable has no single owned entity id - we key its state on the Connection used.
        return connectionId;
    }

    private async Task<int?> SaveImportProfileAsync(string stepKey, RecipeStepState? existing, Dictionary<string, object?> p, int? userId, CancellationToken ct)
    {
        var profile = BuildImportProfileFromParams(stepKey, p);

        if (existing?.EntityId is { } id)
        {
            profile.Id = id;
            await _importProfileService.UpdateAsync(profile);
            return id;
        }

        return await _importProfileService.CreateAsync(profile, userId);
    }

    private static ImportProfile BuildImportProfileFromParams(string stepKey, Dictionary<string, object?> p)
    {
        var defaultTableName = IsShipmentsStep(stepKey) ? WooCommerceRecipe.ShipmentsStagingTableName : WooCommerceRecipe.StagingTableName;
        return new()
        {
            Name = RequireString(p, "name"),
            GroupId = p.GetValueOrDefault("groupId") is int gi ? gi : (p.GetValueOrDefault("groupId") as int?),
            SourceType = "Http",
            SourceConfig = p.GetValueOrDefault("sourceConfig") as string,
            HttpMethod = p.GetValueOrDefault("httpMethod") as string ?? "GET",
            HttpPaginationEnabled = p.GetValueOrDefault("httpPaginationEnabled") is bool b && b,
            HttpPaginationConfig = p.GetValueOrDefault("httpPaginationConfig") as string,
            HttpDataRootPath = p.GetValueOrDefault("httpDataRootPath") as string,
            SourceFormat = "JSON",
            TargetType = "Database",
            TargetConnectionId = RequireInt(p, "targetConnectionId"),
            TargetTable = p.GetValueOrDefault("targetTable") as string ?? defaultTableName,
            LoadStrategy = "Upsert",
            UpsertKeyColumns = p.GetValueOrDefault("upsertKeyColumns") as string ?? "WooOrderId",
            ColumnMappingsJson = p.GetValueOrDefault("columnMappingsJson") as string,
            Hash = ""
        };
    }

    private async Task<int?> SaveQueryTemplateAsync(string recipeKey, string stepKey, RecipeStepState? existing, Dictionary<string, object?> p, CancellationToken ct)
    {
        var isShipments = IsShipmentsStep(stepKey);
        var name = RequireString(p, "name");
        var (defaultTemplate, description, tags) = (recipeKey, isShipments) switch
        {
            (ErrorDigestRecipe.Key, _) => (ErrorDigestEmailTemplate.DailyDigestHtml, "Created by the Store System Error Daily Digest recipe", "store,digest"),
            (_, true) => (WooCommerceEmailTemplates.TrackingUpdateHtml, "Created by the Store WooCommerce Tracking Link recipe", "store,woocommerce"),
            (_, false) => (WooCommerceEmailTemplates.OrderConfirmationHtml, "Created by the Store WooCommerce Order Confirmation recipe", "store,woocommerce")
        };
        var template = p.GetValueOrDefault("template") as string ?? defaultTemplate;

        if (existing?.EntityId is { } id)
        {
            var existingTemplate = await _queryTemplateService.GetByIdAsync(id) ?? throw new InvalidOperationException("QueryTemplate no longer exists.");
            existingTemplate.Name = name;
            existingTemplate.Template = template;
            await _queryTemplateService.UpdateAsync(existingTemplate);
            return id;
        }

        var newTemplate = new QueryTemplate
        {
            Name = name,
            Description = description,
            Type = QueryTemplateType.ScribanTemplate,
            Template = template,
            OutputFormat = "HTML",
            Tags = tags
        };
        var created = await _queryTemplateService.CreateAsync(newTemplate);
        return created.Id;
    }

    private async Task<int?> SaveProfileAsync(string recipeKey, string stepKey, RecipeStepState? existing, Dictionary<string, object?> p, int? userId, CancellationToken ct)
    {
        var profile = BuildExportProfileFromParams(recipeKey, stepKey, p);

        if (existing?.EntityId is { } id)
        {
            profile.Id = id;
            await _profileService.UpdateAsync(profile, ct);
            return id;
        }

        return await _profileService.CreateAsync(profile, userId, ct);
    }

    private static Profile BuildExportProfileFromParams(string recipeKey, string stepKey, Dictionary<string, object?> p)
    {
        var isShipments = IsShipmentsStep(stepKey);
        var isErrorDigest = recipeKey.Equals(ErrorDigestRecipe.Key, StringComparison.OrdinalIgnoreCase);

        var defaultQuery = (isErrorDigest, isShipments) switch
        {
            (true, _) => ErrorDigestRecipe.DefaultExportQuery,
            (false, true) => WooCommerceRecipe.DefaultShipmentsExportQuery,
            (false, false) => WooCommerceRecipe.DefaultExportQuery
        };

        var profile = new Profile
        {
            Name = RequireString(p, "name"),
            ConnectionId = RequireInt(p, "connectionId"),
            GroupId = p.GetValueOrDefault("groupId") is int gi ? gi : (p.GetValueOrDefault("groupId") as int?),
            Query = p.GetValueOrDefault("query") as string ?? defaultQuery,
            OutputFormat = "HTML",
            OutputDestinationType = "Email",
            OutputDestinationId = RequireInt(p, "destinationId"),
            IsEmailExport = true,
            EmailTemplateId = RequireInt(p, "emailTemplateId"),
            Hash = ""
        };

        // The digest recipe's query already projects an "email"/"subject" column per row
        // (see ErrorDigestRecipe.DefaultExportQuery) rather than using a fixed customer
        // address column and a single hardcoded subject like the WooCommerce flows do.
        if (isErrorDigest)
        {
            profile.EmailRecipientsColumn = p.GetValueOrDefault("emailRecipientsColumn") as string ?? "email";
            profile.EmailSubjectColumn = p.GetValueOrDefault("emailSubjectColumn") as string ?? "subject";
            profile.UseHardcodedSubject = false;
        }
        else
        {
            profile.EmailRecipientsColumn = p.GetValueOrDefault("emailRecipientsColumn") as string ?? "CustomerEmail";
            profile.EmailSubjectHardcoded = p.GetValueOrDefault("emailSubject") as string ?? (isShipments ? "Your order is on its way" : "Your order is confirmed");
            profile.UseHardcodedSubject = true;
        }

        return profile;
    }

    private async Task<int?> SaveJobAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
        // Optional step: only creates Jobs when the user explicitly asked for scheduling.
        // Recipes with both an import and an export side (WooCommerce's Flow A/B) chain the
        // export job OnDependency after the import job. Recipes with no import side at all
        // (ErrorDigestRecipe queries the user's own existing tables - nothing to import) still
        // need their export Profile schedulable on its own Interval, so that path is handled
        // here too rather than silently no-op'ing just because importProfileId is absent.
        int? lastId = null;
        var hasImportProfile = p.GetValueOrDefault("importProfileId") is int;

        if (hasImportProfile)
        {
            var importProfileId = (int)p["importProfileId"]!;
            var importJob = new Job
            {
                Name = RequireString(p, "importJobName"),
                Type = JobType.ProfileExecution,
                ImportProfileId = importProfileId,
                ScheduleType = Models.ScheduleType.Interval,
                IntervalMinutes = p.GetValueOrDefault("importIntervalMinutes") is int im ? im : 15
            };
            var created = await _jobService.CreateAsync(importJob);
            lastId = created.Id;

            if (p.GetValueOrDefault("profileId") is int profileId)
            {
                var exportJob = new Job
                {
                    Name = RequireString(p, "exportJobName"),
                    Type = JobType.ProfileExecution,
                    ProfileId = profileId,
                    ScheduleType = Models.ScheduleType.OnDependency,
                    DependsOnJobIds = created.Id.ToString()
                };
                var createdExport = await _jobService.CreateAsync(exportJob);
                lastId = createdExport.Id;
            }
        }
        else if (p.GetValueOrDefault("profileId") is int standaloneProfileId)
        {
            // No import side - schedule the export Profile directly on its own interval.
            var exportJob = new Job
            {
                Name = RequireString(p, "exportJobName"),
                Type = JobType.ProfileExecution,
                ProfileId = standaloneProfileId,
                ScheduleType = Models.ScheduleType.Interval,
                IntervalMinutes = p.GetValueOrDefault("importIntervalMinutes") is int sim ? sim : 1440
            };
            var created = await _jobService.CreateAsync(exportJob);
            lastId = created.Id;
        }

        return lastId;
    }

    // Webhook registration (Tracking Link "fast path" - belt-and-suspenders on top of the polling Import Job)

    /// <summary>
    /// Creates a WebhookTrigger pointed at the Tracking Link's ImportProfile, reusing the
    /// existing WebhookService/WebhookTriggers mechanism (Api/WebhooksEndpoints.cs's Create
    /// handler, WebhookService.CreateWebhookForImportProfileAsync) rather than duplicating it.
    /// Returns the webhook URL for the user to paste into WooCommerce's webhook settings.
    /// This runs on top of the polling Import Job created by SaveJobAsync, not instead of it -
    /// the webhook just fast-tracks an immediate re-pull instead of waiting for the interval.
    /// </summary>
    public async Task<(int WebhookId, string Url)> RegisterTrackingWebhookAsync(int runId, string requestScheme, string requestHost, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var stepState = DeserializeStepState(run.StepStateJson);
        var importStep = stepState.GetValueOrDefault("shipments-import-profile")
            ?? throw new InvalidOperationException("The Tracking Link import step has not been saved yet.");

        var importProfileId = importStep.EntityId
            ?? throw new InvalidOperationException("The Tracking Link ImportProfile has not been saved yet.");

        var (webhookId, token) = await _webhookService.CreateWebhookForImportProfileAsync(importProfileId);
        var url = $"{requestScheme}://{requestHost}/webhooks/{token}";

        Log.Information("Recipe run {RunId} registered Tracking Link webhook {WebhookId} for ImportProfile {ImportProfileId}", runId, webhookId, importProfileId);

        return (webhookId, url);
    }

    // DDL execution against the target Connection (SqlServer/MySQL/PostgreSQL)

    private async Task<bool> TableExistsAsync(Connection connection, string tableName, CancellationToken ct)
    {
        await using var db = OpenTargetConnection(connection);
        await db.OpenAsync(ct);

        var sql = connection.Type switch
        {
            "MySQL" or "MariaDB" => "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName",
            "PostgreSQL" => "SELECT COUNT(*) FROM pg_tables WHERE tablename = @TableName",
            _ => "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName"
        };

        var count = await db.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { TableName = tableName }, commandTimeout: 15, cancellationToken: ct));
        return count > 0;
    }

    private async Task ExecuteDdlAsync(Connection connection, string ddl, CancellationToken ct)
    {
        await using var db = OpenTargetConnection(connection);
        await db.OpenAsync(ct);
        await db.ExecuteAsync(new CommandDefinition(ddl, commandTimeout: 30, cancellationToken: ct));
    }

    private System.Data.Common.DbConnection OpenTargetConnection(Connection connection)
    {
        // Connection.ConnectionString is encrypted at rest; ConnectionService always returns it
        // encrypted from GetByIdAsync, so it must be decrypted before use here.
        var cs = _encryptionService.IsEncrypted(connection.ConnectionString)
            ? _encryptionService.Decrypt(connection.ConnectionString)
            : connection.ConnectionString;

        return connection.Type switch
        {
            "SqlServer" => new Microsoft.Data.SqlClient.SqlConnection(cs),
            "MySQL" or "MariaDB" => new MySqlConnector.MySqlConnection(cs),
            "PostgreSQL" => new Npgsql.NpgsqlConnection(cs),
            _ => new Microsoft.Data.SqlClient.SqlConnection(cs)
        };
    }

    // Persistence helpers

    private async Task PersistStepStateAsync(int runId, string stepKey, RecipeDefinition recipe, Dictionary<string, RecipeStepState> stepState, CancellationToken ct, bool advanceOnSuccess = false)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        string? nextCurrentStepKey = null;
        if (advanceOnSuccess)
        {
            var stepIndex = recipe.Steps.FindIndex(s => s.StepKey == stepKey);
            var next = recipe.Steps.Skip(stepIndex + 1).FirstOrDefault();
            nextCurrentStepKey = next?.StepKey;
        }

        var json = JsonSerializer.Serialize(stepState, JsonOptions);

        if (nextCurrentStepKey is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE RecipeRuns SET StepStateJson = @Json, CurrentStepKey = @NextStep, UpdatedAt = @Now WHERE Id = @Id",
                new { Id = runId, Json = json, NextStep = nextCurrentStepKey, Now = DateTime.UtcNow }, cancellationToken: ct));
        }
        else
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE RecipeRuns SET StepStateJson = @Json, UpdatedAt = @Now WHERE Id = @Id",
                new { Id = runId, Json = json, Now = DateTime.UtcNow }, cancellationToken: ct));
        }
    }

    private async Task<RecipeRun> GetRunOrThrowAsync(int runId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var run = await conn.QueryFirstOrDefaultAsync<RecipeRun>(
            new CommandDefinition("SELECT * FROM RecipeRuns WHERE Id = @Id", new { Id = runId }, cancellationToken: ct));

        return run ?? throw new InvalidOperationException($"Recipe run {runId} not found.");
    }

    private static Dictionary<string, RecipeStepState> DeserializeStepState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, RecipeStepState>>(json, JsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static readonly HashSet<string> SecretParamKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "connectionString", "configurationJson", "smtpPassword", "consumerSecret", "sourceConfig"
    };

    private static RecipeStepStateDto ToDto(RecipeStepDefinition step, RecipeStepState state) => new()
    {
        StepKey = step.StepKey,
        Title = step.Title,
        EntityType = step.EntityType.ToString(),
        IsOptional = step.IsOptional,
        IsShared = step.IsShared,
        FlowGroup = step.FlowGroup,
        HasVerifier = step.VerifierKind is not null,
        EntityId = state.EntityId,
        Verified = state.Verified,
        Skipped = state.Skipped,
        LastVerifiedAt = state.LastVerifiedAt,
        LastVerifyMessage = state.LastVerifyMessage,
        // Never echo secrets back to the client - the wizard's "leave unchanged to keep
        // existing value" placeholders cover the UX for password-style fields on resume.
        Params = state.Params.Where(kv => !SecretParamKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)
    };

    private static string RequireString(Dictionary<string, object?> p, string key) =>
        p.GetValueOrDefault(key) as string ?? throw new InvalidOperationException($"'{key}' is required.");

    private static int RequireInt(Dictionary<string, object?> p, string key)
    {
        var value = p.GetValueOrDefault(key);
        return value switch
        {
            int i => i,
            JsonElement je when je.TryGetInt32(out var i) => i,
            string s when int.TryParse(s, out var i) => i,
            _ => throw new InvalidOperationException($"'{key}' is required.")
        };
    }
}

// DTOs for the Recipes API

public class RecipeListItem
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int StepCount { get; set; }
    public int? ExistingRunId { get; set; }
    public int? LastCompletedRunId { get; set; }
}

public class RecipeRunStateDto
{
    public int RunId { get; set; }
    public required string RecipeKey { get; set; }
    public required string RecipeName { get; set; }
    public required string Status { get; set; }
    public string? CurrentStepKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<RecipeStepStateDto> Steps { get; set; } = new();
}

public class RecipeStepStateDto
{
    public required string StepKey { get; set; }
    public required string Title { get; set; }
    public required string EntityType { get; set; }
    public bool IsOptional { get; set; }
    public bool IsShared { get; set; }
    public string? FlowGroup { get; set; }
    public bool HasVerifier { get; set; }
    public int? EntityId { get; set; }
    public bool Verified { get; set; }
    public bool Skipped { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public string? LastVerifyMessage { get; set; }

    /// <summary>
    /// The params this step was last saved with - lets the wizard UI re-populate its
    /// form fields on resume (e.g. after closing the wizard mid-setup and reopening it).
    /// Note: never carries raw secrets back to the client beyond what the entity service
    /// itself would already return (e.g. SMTP password fields are write-only by convention).
    /// </summary>
    public Dictionary<string, object?> Params { get; set; } = new();
}
