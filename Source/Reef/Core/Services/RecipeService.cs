using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Recipes;
using Serilog;

namespace Reef.Core.Services;

// Pure orchestration for the Store wizard - delegates every create/update to the existing
// entity services. The one new piece of logic is the staging-table DDL step: Reef's import
// pipeline never auto-creates target tables, so the wizard issues CREATE TABLE itself.
public class RecipeService
{
    // Stable, recognizable name for the auto-provisioned Sqlite staging Connection - "find"
    // half of find-or-create. A user who renames it breaks the auto-reuse (they've opted out
    // by touching it), which is an acceptable edge case for a name-based marker.
    public const string StagingConnectionName = "Store Staging Database";
    private const string StagingDbFileName = "store-staging.db";

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

    public async Task<List<RecipeListItem>> GetAvailableRecipesAsync(int? userId, CancellationToken ct = default)
    {
        var runs = await GetRunsForUserAsync(userId, ct);

        return RecipeRegistry.All.Select(recipe =>
        {
            var existingRun = runs
                .Where(r => r.RecipeKey.Equals(recipe.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefault();

            // Independent of existingRun so Reconfigure stays offered even after a newer
            // InProgress run exists for the same recipe.
            var lastCompletedRun = runs
                .Where(r => r.RecipeKey.Equals(recipe.Key, StringComparison.OrdinalIgnoreCase) && r.Status == "Completed")
                .OrderByDescending(r => r.CompletedAt)
                .FirstOrDefault();

            return new RecipeListItem
            {
                Key = recipe.Key,
                Name = recipe.Name,
                Description = recipe.Description,
                Category = recipe.Category,
                Icon = recipe.Icon,
                StepCount = recipe.Steps.Count,
                ExistingRunId = existingRun?.Status == "InProgress" ? existingRun.Id : null,
                LastCompletedRunId = lastCompletedRun?.Id,
                IsInstalled = lastCompletedRun is not null
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

    // cloneFromRunId (the "Reconfigure" action) pre-seeds the new run's StepStateJson from a
    // prior Completed run's entity ids/params, but never the Verified flag - pointing an
    // existing entity at new credentials means it's unverified until re-checked.
    public async Task<RecipeRun> StartRecipeAsync(string recipeKey, int? userId, int? cloneFromRunId = null, CancellationToken ct = default)
    {
        var recipe = RecipeRegistry.GetByKey(recipeKey)
            ?? throw new InvalidOperationException($"Unknown recipe '{recipeKey}'.");

        var seedStepState = await BuildCloneSeedStateAsync(recipe, cloneFromRunId, ct);
        await ApplyAutoProvisioningAsync(recipe, seedStepState, userId, ct);

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
            // Skip past any already-auto-provisioned leading steps - simple mode hides them
            // from the rail entirely, so landing the wizard on one of them on first load would
            // show a step the user can't navigate to. Falls back to the very first step if
            // every step happens to be auto-provisioned (defensive; not true for any recipe today).
            CurrentStepKey = (recipe.Steps.FirstOrDefault(s => !s.CanAutoProvision || !seedStepState.ContainsKey(s.StepKey)) ?? recipe.Steps.First()).StepKey,
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

    // "Simple mode" - for every CanAutoProvision step the run doesn't already have a value
    // for (fresh start, or not carried over by Reconfigure-cloning), silently find-or-create
    // a sensible default entity and seed it into the run's initial StepStateJson as Verified.
    // These are real Connections/Groups the user can find on the normal pages afterward -
    // "simple mode" only means the wizard doesn't ask up front. The Advanced toggle in
    // store.js reveals these steps again so a user can override with their own entity, which
    // just flows through the existing ExecuteStepAsync save path like any manual step.
    private async Task ApplyAutoProvisioningAsync(RecipeDefinition recipe, Dictionary<string, RecipeStepState> seedStepState, int? userId, CancellationToken ct)
    {
        foreach (var step in recipe.Steps)
        {
            if (!step.CanAutoProvision || seedStepState.ContainsKey(step.StepKey))
                continue;

            int? entityId = step.EntityType switch
            {
                RecipeEntityType.Connection => await FindOrCreateStagingConnectionAsync(userId, ct),
                RecipeEntityType.Group => await FindOrCreateRecipeGroupAsync(recipe, ct),
                _ => null
            };

            if (entityId is null)
                continue;

            seedStepState[step.StepKey] = new RecipeStepState
            {
                EntityId = entityId,
                Params = AutoProvisionedParams(recipe, step, entityId.Value),
                // We control exactly what we just created/found, so it doesn't need the live
                // ConnectionVerifier check a user-supplied entity would - mark it Verified so
                // the rail's "done" status and canAdvance() work without the user clicking.
                Verified = true,
                Skipped = false
            };

            Log.Information("Recipe '{RecipeKey}' auto-provisioned {EntityType} {EntityId} for step '{StepKey}'",
                recipe.Key, step.EntityType, entityId, step.StepKey);
        }
    }

    private static Dictionary<string, object?> AutoProvisionedParams(RecipeDefinition recipe, RecipeStepDefinition step, int entityId) => step.EntityType switch
    {
        RecipeEntityType.Connection => new Dictionary<string, object?>
        {
            ["name"] = StagingConnectionName,
            ["type"] = "Sqlite",
            ["existingConnectionId"] = entityId
        },
        RecipeEntityType.Group => new Dictionary<string, object?>
        {
            ["name"] = $"{recipe.Name} (Store)"
        },
        _ => new Dictionary<string, object?>()
    };

    // Find-or-create by the stable StagingConnectionName marker - idempotent and reusable
    // across every recipe run/recipe that opts into auto-provisioning, never a duplicate per run.
    private async Task<int?> FindOrCreateStagingConnectionAsync(int? userId, CancellationToken ct)
    {
        var existing = await _connectionService.GetByNameAsync(StagingConnectionName, ct);
        if (existing is not null)
            return existing.Id;

        var path = ResolveStagingDbPath();
        var newConn = new Connection
        {
            Name = StagingConnectionName,
            Type = "Sqlite",
            ConnectionString = $"Data Source={path}",
            Tags = "",
            Hash = ""
        };

        var id = await _connectionService.CreateAsync(newConn, userId, ct);
        Log.Information("Auto-provisioned Sqlite staging Connection '{Name}' (ID: {Id}) at {Path}", StagingConnectionName, id, path);
        return id;
    }

    // Sibling file next to Reef's own app database (never that file itself) - e.g. app db at
    // /data/Reef.db -> staging file at /data/store-staging.db. Microsoft.Data.Sqlite creates
    // the file on first use, so there's no need to touch the filesystem here.
    private string ResolveStagingDbPath()
    {
        var appDbPath = ExtractSqliteDataSource(_connectionString);
        var directory = Path.GetDirectoryName(Path.GetFullPath(appDbPath));
        return string.IsNullOrEmpty(directory)
            ? StagingDbFileName
            : Path.Combine(directory, StagingDbFileName);
    }

    private static string ExtractSqliteDataSource(string connectionString)
    {
        // Reef's own connection string is plain SQLite "Data Source=..." (e.g. "Data Source=Reef.db"),
        // never a hybrid-encrypted user Connection string - safe to parse with the builder directly.
        try
        {
            return new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch
        {
            return "Reef.db";
        }
    }

    // Find-or-create by name "{Recipe.Name} (Store)" - one Group per recipe, reused across
    // every run of that recipe rather than spawned anew each time.
    private async Task<int?> FindOrCreateRecipeGroupAsync(RecipeDefinition recipe, CancellationToken ct)
    {
        var name = $"{recipe.Name} (Store)";
        var existing = await _groupService.GetByNameAsync(name);
        if (existing is not null)
            return existing.Id;

        var newGroup = new ProfileGroup
        {
            Name = name,
            Description = $"Created automatically by the Store {recipe.Name} recipe."
        };

        var id = await _groupService.CreateAsync(newGroup, "store-wizard");
        Log.Information("Auto-provisioned Group '{Name}' (ID: {Id}) for recipe '{RecipeKey}'", name, id, recipe.Key);
        return id;
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
            Steps = recipe.Steps.Select(step => ToDto(step, stepState.GetValueOrDefault(step.StepKey) ?? new RecipeStepState(), recipe.Key)).ToList()
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
            RecipeEntityType.StagingTable => await CreateStagingTableAsync(recipe.Key, stepKey, stepParams, ct),
            RecipeEntityType.ImportProfile => await SaveImportProfileAsync(recipe.Key, stepKey, existing, stepParams, userId, ct),
            RecipeEntityType.QueryTemplate => await SaveQueryTemplateAsync(recipe.Key, stepKey, existing, stepParams, ct),
            RecipeEntityType.Profile => await SaveProfileAsync(recipe.Key, stepKey, existing, stepParams, userId, ct),
            RecipeEntityType.Job => await SaveJobAsync(stepParams, ct),
            _ => existing?.EntityId
        };

        var newState = new RecipeStepState
        {
            EntityId = entityId,
            // No verifier (Group, optional Jobs) means nothing left to live-check; steps
            // with a verifier require a fresh VerifyStepAsync call after every save.
            Verified = step.VerifierKind is null,
            LastVerifiedAt = step.VerifierKind is null ? existing?.LastVerifiedAt : null,
            LastVerifyMessage = step.VerifierKind is null ? existing?.LastVerifyMessage : null,
            Params = stepParams,
            Skipped = false
        };

        stepState[stepKey] = newState;
        await PersistStepStateAsync(runId, stepKey, recipe, stepState, ct);

        return ToDto(step, newState, recipe.Key);
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

        return await MarkStepSkippedAsync(runId, run, recipe, step, ct);
    }

    // Shared by both the per-step "Skip this step" button (optional steps only) and
    // SkipFlowGroupAsync's bulk skip of a whole declined flow - the actual state mutation
    // (write Skipped=true, persist) never duplicates, only the eligibility check above differs.
    private async Task<RecipeStepStateDto> MarkStepSkippedAsync(int runId, RecipeRun run, RecipeDefinition recipe, RecipeStepDefinition step, CancellationToken ct)
    {
        var stepState = DeserializeStepState(run.StepStateJson);
        var newState = new RecipeStepState { Skipped = true };
        stepState[step.StepKey] = newState;
        await PersistStepStateAsync(runId, step.StepKey, recipe, stepState, ct);

        return ToDto(step, newState, recipe.Key);
    }

    // Generic flow-group skip: any recipe with >1 distinct FlowGroup among its non-shared
    // steps is eligible (checked by callers via HasMultipleFlowGroups), so this never needs
    // to know which recipe it's skipping for. IsShared steps are never touched - they're
    // skipped via FlowGroup filtering (shared steps have no FlowGroup) rather than a hardcoded
    // exclusion. Required (non-optional) steps in a declined flow are skipped too, since
    // "the user opted out of this entire flow" is a different, deliberate decision than the
    // per-step "Skip this step" affordance IsOptional gates.
    public async Task<List<RecipeStepStateDto>> SkipFlowGroupAsync(int runId, string flowGroup, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var recipe = RecipeRegistry.GetByKey(run.RecipeKey)
            ?? throw new InvalidOperationException($"Recipe '{run.RecipeKey}' no longer exists.");

        var steps = recipe.Steps.Where(s => !s.IsShared && s.FlowGroup == flowGroup).ToList();
        if (steps.Count == 0)
            throw new InvalidOperationException($"Flow group '{flowGroup}' is not part of recipe '{run.RecipeKey}'.");

        var results = new List<RecipeStepStateDto>();
        foreach (var step in steps)
        {
            // Re-fetch so each iteration sees the previous iteration's persisted state.
            run = await GetRunOrThrowAsync(runId, ct);
            results.Add(await MarkStepSkippedAsync(runId, run, recipe, step, ct));
        }

        Log.Information("Recipe run {RunId} skipped flow group '{FlowGroup}' ({Count} steps)", runId, flowGroup, steps.Count);
        return results;
    }

    public static bool HasMultipleFlowGroups(RecipeDefinition recipe) =>
        recipe.Steps.Where(s => !s.IsShared).Select(s => s.FlowGroup).Distinct().Count() > 1;

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

        return ToDto(step, current, recipe.Key);
    }

    private RecipeVerifyContext BuildVerifyContext(string recipeKey, RecipeStepDefinition step, RecipeStepState current, Dictionary<string, RecipeStepState> allSteps)
    {
        var connectionId = allSteps.GetValueOrDefault("connection")?.EntityId;

        // Falls back to the recipe's own staging-table step's saved name, never a hardcoded
        // default - otherwise a recipe with no staging table (ErrorDigestRecipe) would get
        // defaulted to "StoreOrders" just because it reuses the same step keys. Plain step
        // keys are shared by WooCommerceRecipe, WooCommerceTrackingRecipe, and MagentoRecipe -
        // each is a single-flow (or now-single-flow) recipe, so no "shipments-" prefix needed.
        var tableName = step.StepKey switch
        {
            "staging-table" => current.Params.GetValueOrDefault("tableName") as string,
            "query-template" => allSteps.GetValueOrDefault("staging-table")?.Params.GetValueOrDefault("tableName") as string,
            "export-profile" => allSteps.GetValueOrDefault("staging-table")?.Params.GetValueOrDefault("tableName") as string,
            _ => null
        };

        var mockTemplateRow = recipeKey.Equals(ErrorDigestRecipe.Key, StringComparison.OrdinalIgnoreCase)
            ? ErrorDigestRecipe.MockDigestRow()
            : recipeKey.Equals(ExactGlobeRecipe.Key, StringComparison.OrdinalIgnoreCase)
                ? (IsItemsStep(step.StepKey) ? ExactGlobeRecipe.MockItemRow() : ExactGlobeRecipe.MockDebtorRow())
                : recipeKey.Equals(UblExportRecipe.Key, StringComparison.OrdinalIgnoreCase)
                    ? (IsOrderStep(step.StepKey) ? UblExportRecipe.MockOrderRow()
                        : IsDespatchStep(step.StepKey) ? UblExportRecipe.MockDespatchAdviceRow()
                        : IsInventoryStep(step.StepKey) ? UblExportRecipe.MockInventoryRow()
                        : UblExportRecipe.MockInvoiceRow())
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

        var newConn = new Connection { Name = name, Type = type, ConnectionString = connectionString, Tags = "", Hash = "" };
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

    // "Did this run already create a Destination?" - no cross-run search, just this run's
    // shared StepStateJson["destination"] entry.
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

    // WooCommerceTrackingRecipe is now its own RecipeDefinition (split out of WooCommerceRecipe's
    // former Flow B) - dispatch on recipe.Key, the same way MagentoRecipe is told apart from
    // WooCommerceRecipe, rather than a "shipments-" step-key prefix that no longer exists.
    private static bool IsWooCommerceTrackingRecipe(string recipeKey) =>
        recipeKey.Equals(WooCommerceTrackingRecipe.Key, StringComparison.OrdinalIgnoreCase);

    // ExactGlobeRecipe's two flows (Debtors/Items) are a different two-flow shape than
    // WooCommerce's Order Confirmation/Tracking Link - same "(recipeKey, topic)" dispatch
    // mechanism, distinct helper since "shipments" semantics don't apply here.
    private static bool IsDebtorsStep(string stepKey) => stepKey.StartsWith("debtors-", StringComparison.OrdinalIgnoreCase);
    private static bool IsItemsStep(string stepKey) => stepKey.StartsWith("items-", StringComparison.OrdinalIgnoreCase);

    // UblExportRecipe's four flows (Invoice/Order/Despatch Advice/Inventory) - same
    // "(recipeKey, topic)" dispatch mechanism as ExactGlobeRecipe's Debtors/Items pair, just
    // four prefixes instead of two.
    private static bool IsInvoiceStep(string stepKey) => stepKey.StartsWith("invoice-", StringComparison.OrdinalIgnoreCase);
    private static bool IsOrderStep(string stepKey) => stepKey.StartsWith("order-", StringComparison.OrdinalIgnoreCase);
    private static bool IsDespatchStep(string stepKey) => stepKey.StartsWith("despatch-", StringComparison.OrdinalIgnoreCase);
    private static bool IsInventoryStep(string stepKey) => stepKey.StartsWith("inventory-", StringComparison.OrdinalIgnoreCase);

    private async Task<int?> CreateStagingTableAsync(string recipeKey, string stepKey, Dictionary<string, object?> p, CancellationToken ct)
    {
        var isTracking = IsWooCommerceTrackingRecipe(recipeKey);
        var isMagento = recipeKey.Equals(MagentoRecipe.Key, StringComparison.OrdinalIgnoreCase);
        var defaultTableName = isMagento
            ? MagentoRecipe.StagingTableName
            : isTracking ? WooCommerceRecipe.ShipmentsStagingTableName : WooCommerceRecipe.StagingTableName;

        var connectionId = RequireInt(p, "connectionId");
        var tableName = p.GetValueOrDefault("tableName") as string ?? defaultTableName;

        var connection = await _connectionService.GetByIdAsync(connectionId, ct)
            ?? throw new InvalidOperationException($"Connection {connectionId} not found.");

        var exists = await TableExistsAsync(connection, tableName, ct);
        if (!exists)
        {
            var baseDdl = isMagento
                ? MagentoRecipe.GetStagingTableDdl(connection.Type)
                : isTracking
                    ? WooCommerceRecipe.GetShipmentsStagingTableDdl(connection.Type)
                    : WooCommerceRecipe.GetStagingTableDdl(connection.Type);
            var ddl = baseDdl.Replace(defaultTableName, tableName);
            await ExecuteDdlAsync(connection, ddl, ct);
            Log.Information("Recipe wizard created staging table {Table} on connection {ConnectionId}", tableName, connectionId);
        }

        return connectionId;
    }

    private async Task<int?> SaveImportProfileAsync(string recipeKey, string stepKey, RecipeStepState? existing, Dictionary<string, object?> p, int? userId, CancellationToken ct)
    {
        var profile = BuildImportProfileFromParams(recipeKey, stepKey, p);

        if (existing?.EntityId is { } id)
        {
            profile.Id = id;
            await _importProfileService.UpdateAsync(profile);
            return id;
        }

        return await _importProfileService.CreateAsync(profile, userId);
    }

    private static ImportProfile BuildImportProfileFromParams(string recipeKey, string stepKey, Dictionary<string, object?> p)
    {
        var isMagento = recipeKey.Equals(MagentoRecipe.Key, StringComparison.OrdinalIgnoreCase);
        var defaultTableName = isMagento
            ? MagentoRecipe.StagingTableName
            : IsWooCommerceTrackingRecipe(recipeKey) ? WooCommerceRecipe.ShipmentsStagingTableName : WooCommerceRecipe.StagingTableName;
        var defaultUpsertKey = isMagento ? "MagentoOrderId" : "WooOrderId";
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
            UpsertKeyColumns = p.GetValueOrDefault("upsertKeyColumns") as string ?? defaultUpsertKey,
            ColumnMappingsJson = p.GetValueOrDefault("columnMappingsJson") as string,
            Hash = ""
        };
    }

    private static (string Template, string Description, string Tags, string OutputFormat) GetQueryTemplateDefaults(string recipeKey, string stepKey)
    {
        var isItems = IsItemsStep(stepKey);
        return recipeKey switch
        {
            ErrorDigestRecipe.Key => (ErrorDigestEmailTemplate.DailyDigestHtml, "Created by the Store System Error Daily Digest recipe", "store,digest", "HTML"),
            MagentoRecipe.Key => (MagentoEmailTemplates.TrackingUpdateHtml, "Created by the Store Magento Tracking Link recipe", "store,magento", "HTML"),
            WooCommerceTrackingRecipe.Key => (WooCommerceEmailTemplates.TrackingUpdateHtml, "Created by the Store WooCommerce Tracking Link recipe", "store,woocommerce", "HTML"),
            ExactGlobeRecipe.Key when isItems => (ExactGlobeTemplates.ItemsXmlTemplate, "Created by the Store Exact Globe+ Items Export recipe", "store,exact-globe", "XML"),
            ExactGlobeRecipe.Key => (ExactGlobeTemplates.DebtorsXmlTemplate, "Created by the Store Exact Globe+ Debtors Export recipe", "store,exact-globe", "XML"),
            UblExportRecipe.Key when IsOrderStep(stepKey) => (UblExportTemplates.OrderXmlTemplate, "Created by the Store UBL Standard Order Export recipe", "store,ubl", "XML"),
            UblExportRecipe.Key when IsDespatchStep(stepKey) => (UblExportTemplates.DespatchAdviceXmlTemplate, "Created by the Store UBL Standard Despatch Advice Export recipe", "store,ubl", "XML"),
            UblExportRecipe.Key when IsInventoryStep(stepKey) => (UblExportTemplates.InventoryXmlTemplate, "Created by the Store UBL Standard Inventory Export recipe", "store,ubl", "XML"),
            UblExportRecipe.Key => (UblExportTemplates.InvoiceXmlTemplate, "Created by the Store UBL Standard Invoice Export recipe", "store,ubl", "XML"),
            _ => (WooCommerceEmailTemplates.OrderConfirmationHtml, "Created by the Store WooCommerce Order Confirmation recipe", "store,woocommerce", "HTML")
        };
    }

    private async Task<int?> SaveQueryTemplateAsync(string recipeKey, string stepKey, RecipeStepState? existing, Dictionary<string, object?> p, CancellationToken ct)
    {
        var name = RequireString(p, "name");
        var (defaultTemplate, description, tags, outputFormat) = GetQueryTemplateDefaults(recipeKey, stepKey);
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
            OutputFormat = outputFormat,
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
        if (recipeKey.Equals(ExactGlobeRecipe.Key, StringComparison.OrdinalIgnoreCase))
            return BuildExactGlobeExportProfileFromParams(stepKey, p);

        if (recipeKey.Equals(UblExportRecipe.Key, StringComparison.OrdinalIgnoreCase))
            return BuildUblExportProfileFromParams(stepKey, p);

        var isTracking = IsWooCommerceTrackingRecipe(recipeKey);
        var isErrorDigest = recipeKey.Equals(ErrorDigestRecipe.Key, StringComparison.OrdinalIgnoreCase);
        var isMagento = recipeKey.Equals(MagentoRecipe.Key, StringComparison.OrdinalIgnoreCase);

        var defaultQuery = (isErrorDigest, isMagento, isTracking) switch
        {
            (true, _, _) => ErrorDigestRecipe.DefaultExportQuery,
            (_, true, _) => MagentoRecipe.DefaultExportQuery,
            (_, _, true) => WooCommerceRecipe.DefaultShipmentsExportQuery,
            (_, _, false) => WooCommerceRecipe.DefaultExportQuery
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

        if (isErrorDigest)
        {
            profile.EmailRecipientsColumn = p.GetValueOrDefault("emailRecipientsColumn") as string ?? "email";
            profile.EmailSubjectColumn = p.GetValueOrDefault("emailSubjectColumn") as string ?? "subject";
            profile.UseHardcodedSubject = false;
        }
        else
        {
            profile.EmailRecipientsColumn = p.GetValueOrDefault("emailRecipientsColumn") as string ?? "CustomerEmail";
            profile.EmailSubjectHardcoded = p.GetValueOrDefault("emailSubject") as string ?? (isTracking || isMagento ? "Your order is on its way" : "Your order is confirmed");
            profile.UseHardcodedSubject = true;
        }

        return profile;
    }

    // File export, not email: OutputDestinationType="Local" + an inline OutputDestinationConfig
    // JSON path (ExecutionService's "Priority 3: fall back to profile's inline configuration")
    // writes straight to a configured folder, no separate Destination entity required.
    private static Profile BuildExactGlobeExportProfileFromParams(string stepKey, Dictionary<string, object?> p)
    {
        var isItems = IsItemsStep(stepKey);
        var defaultQuery = isItems ? ExactGlobeRecipe.DefaultItemsExportQuery : ExactGlobeRecipe.DefaultDebtorsExportQuery;
        var defaultPath = isItems ? "exports/exact-globe/items" : "exports/exact-globe/debtors";

        var outputPath = p.GetValueOrDefault("outputPath") as string ?? defaultPath;
        var destinationConfig = JsonSerializer.Serialize(new { path = outputPath }, JsonOptions);

        return new Profile
        {
            Name = RequireString(p, "name"),
            ConnectionId = RequireInt(p, "connectionId"),
            GroupId = p.GetValueOrDefault("groupId") is int gi ? gi : (p.GetValueOrDefault("groupId") as int?),
            Query = p.GetValueOrDefault("query") as string ?? defaultQuery,
            OutputFormat = "XML",
            OutputDestinationType = "Local",
            OutputDestinationConfig = destinationConfig,
            IsEmailExport = false,
            TemplateId = RequireInt(p, "templateId"),
            Hash = ""
        };
    }

    // Same file-export shape as BuildExactGlobeExportProfileFromParams (no email, inline
    // OutputDestinationType="Local"/OutputDestinationConfig) but kept as its own method rather
    // than folded into that one - ExactGlobe's is a 2-way isItems branch, UBL needs a 4-way
    // branch across Invoice/Order/Despatch Advice/Inventory, and merging them would mean every
    // call site juggling both step-key vocabularies at once for no shared benefit.
    private static Profile BuildUblExportProfileFromParams(string stepKey, Dictionary<string, object?> p)
    {
        var (defaultQuery, defaultPathSegment) = (IsOrderStep(stepKey), IsDespatchStep(stepKey), IsInventoryStep(stepKey)) switch
        {
            (true, _, _) => (UblExportRecipe.DefaultOrderExportQuery, "orders"),
            (_, true, _) => (UblExportRecipe.DefaultDespatchAdviceExportQuery, "despatch-advices"),
            (_, _, true) => (UblExportRecipe.DefaultInventoryExportQuery, "inventory"),
            _ => (UblExportRecipe.DefaultInvoiceExportQuery, "invoices")
        };
        var defaultPath = $"exports/ubl-standard-export/{defaultPathSegment}";

        var outputPath = p.GetValueOrDefault("outputPath") as string ?? defaultPath;
        var destinationConfig = JsonSerializer.Serialize(new { path = outputPath }, JsonOptions);

        return new Profile
        {
            Name = RequireString(p, "name"),
            ConnectionId = RequireInt(p, "connectionId"),
            GroupId = p.GetValueOrDefault("groupId") is int gi ? gi : (p.GetValueOrDefault("groupId") as int?),
            Query = p.GetValueOrDefault("query") as string ?? defaultQuery,
            OutputFormat = "XML",
            OutputDestinationType = "Local",
            OutputDestinationConfig = destinationConfig,
            IsEmailExport = false,
            TemplateId = RequireInt(p, "templateId"),
            Hash = ""
        };
    }

    private async Task<int?> SaveJobAsync(Dictionary<string, object?> p, CancellationToken ct)
    {
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

    // Belt-and-suspenders fast path on top of the polling Import Job from SaveJobAsync -
    // fast-tracks an immediate re-pull instead of waiting for the interval.
    public async Task<(int WebhookId, string Url)> RegisterTrackingWebhookAsync(int runId, string requestScheme, string requestHost, CancellationToken ct = default)
    {
        var run = await GetRunOrThrowAsync(runId, ct);
        var stepState = DeserializeStepState(run.StepStateJson);
        var importStep = stepState.GetValueOrDefault("import-profile")
            ?? throw new InvalidOperationException("The Tracking Link import step has not been saved yet.");

        var importProfileId = importStep.EntityId
            ?? throw new InvalidOperationException("The Tracking Link ImportProfile has not been saved yet.");

        var (webhookId, token) = await _webhookService.CreateWebhookForImportProfileAsync(importProfileId);
        var url = $"{requestScheme}://{requestHost}/webhooks/{token}";

        Log.Information("Recipe run {RunId} registered Tracking Link webhook {WebhookId} for ImportProfile {ImportProfileId}", runId, webhookId, importProfileId);

        return (webhookId, url);
    }

    private async Task<bool> TableExistsAsync(Connection connection, string tableName, CancellationToken ct)
    {
        await using var db = OpenTargetConnection(connection);
        await db.OpenAsync(ct);

        var sql = connection.Type switch
        {
            "MySQL" or "MariaDB" => "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName",
            "PostgreSQL" => "SELECT COUNT(*) FROM pg_tables WHERE tablename = @TableName",
            "Sqlite" => "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name = @TableName",
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
        var cs = _encryptionService.IsEncrypted(connection.ConnectionString)
            ? _encryptionService.Decrypt(connection.ConnectionString)
            : connection.ConnectionString;

        return connection.Type switch
        {
            "SqlServer" => new Microsoft.Data.SqlClient.SqlConnection(cs),
            "MySQL" or "MariaDB" => new MySqlConnector.MySqlConnection(cs),
            "PostgreSQL" => new Npgsql.NpgsqlConnection(cs),
            // The Sqlite Connection type here is the user's chosen staging file (from `cs`),
            // never Reef's own app database (`_connectionString`).
            "Sqlite" => new SqliteConnection(cs),
            _ => new Microsoft.Data.SqlClient.SqlConnection(cs)
        };
    }

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

    private static RecipeStepStateDto ToDto(RecipeStepDefinition step, RecipeStepState state, string recipeKey)
    {
        var displayParams = state.Params.Where(kv => !SecretParamKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        // Show the built-in default in the editor up front instead of leaving it blank with
        // just an explanatory note - the user can see and tweak it before saving, rather than
        // saving blind and hoping the "leave blank to use the built-in template" note was right.
        if (step.EntityType == RecipeEntityType.QueryTemplate && state.EntityId is null && !displayParams.ContainsKey("template"))
        {
            displayParams["template"] = GetQueryTemplateDefaults(recipeKey, step.StepKey).Template;
        }

        return new()
        {
            StepKey = step.StepKey,
            Title = step.Title,
            EntityType = step.EntityType.ToString(),
            IsOptional = step.IsOptional,
            IsShared = step.IsShared,
            FlowGroup = step.FlowGroup,
            CanAutoProvision = step.CanAutoProvision,
            HasVerifier = step.VerifierKind is not null,
            EntityId = state.EntityId,
            Verified = state.Verified,
            Skipped = state.Skipped,
            LastVerifiedAt = state.LastVerifiedAt,
            LastVerifyMessage = state.LastVerifyMessage,
            Params = displayParams
        };
    }

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

public class RecipeListItem
{
    public required string Key { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public required string Icon { get; set; }
    public int StepCount { get; set; }
    public int? ExistingRunId { get; set; }
    public int? LastCompletedRunId { get; set; }
    public bool IsInstalled { get; set; }
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
    public bool CanAutoProvision { get; set; }
    public bool HasVerifier { get; set; }
    public int? EntityId { get; set; }
    public bool Verified { get; set; }
    public bool Skipped { get; set; }
    public DateTime? LastVerifiedAt { get; set; }
    public string? LastVerifyMessage { get; set; }

    public Dictionary<string, object?> Params { get; set; } = new();
}
