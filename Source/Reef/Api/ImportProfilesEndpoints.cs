using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Parsers;
using Reef.Core.Services;
using Reef.Core.Sources;
using Reef.Core.Targets;
using Serilog;

namespace Reef.Api;

/// <summary>
/// REST API for Import Profile management.
/// Base path: /api/import-profiles
/// </summary>
public static class ImportProfilesEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/import-profiles").RequireAuthorization();

        // ── CRUD ──────────────────────────────────────────────────────
        g.MapGet("/",                        GetAll);
        g.MapGet("/{id:int}",               GetById);
        g.MapPost("/",                       Create);
        g.MapPut("/{id:int}",               Update);
        g.MapDelete("/{id:int}",            Delete);
        g.MapPost("/{id:int}/enable",       Enable);
        g.MapPost("/{id:int}/disable",      Disable);
        g.MapGet("/by-group/{groupId:int}", GetByGroup);

        // ── Execution ──────────────────────────────────────────────────
        g.MapPost("/{id:int}/execute",      Execute);
        g.MapGet("/{id:int}/executions",    GetExecutions);
        g.MapGet("/executions/{execId:int}",          GetExecution);
        g.MapGet("/executions/{execId:int}/errors",   GetExecutionErrors);

        // ── Source Operations ──────────────────────────────────────────
        g.MapPost("/test-source",           TestSource);
        g.MapPost("/list-source-files",     ListSourceFiles);
        g.MapPost("/preview",               Preview);

        // ── Target Operations ──────────────────────────────────────────
        g.MapPost("/target-schema",         GetTargetSchema);
        g.MapPost("/test-target",           TestTarget);

        // ── Delta Sync ─────────────────────────────────────────────────
        g.MapGet("/{id:int}/delta-sync/stats",    GetDeltaSyncStats);
        g.MapDelete("/{id:int}/delta-sync/reset", ResetDeltaSync);
    }

    // ── CRUD Handlers ──────────────────────────────────────────────────

    private static async Task<IResult> GetAll(
        [FromServices] ImportProfileService svc)
    {
        try { return Results.Ok(await svc.GetAllAsync()); }
        catch (Exception ex) { return ServerError("retrieving import profiles", ex); }
    }

    private static async Task<IResult> GetById(
        int id,
        [FromServices] ImportProfileService svc)
    {
        try
        {
            var p = await svc.GetByIdAsync(id);
            return p is not null ? Results.Ok(p) : Results.NotFound();
        }
        catch (Exception ex) { return ServerError($"retrieving import profile {id}", ex); }
    }

    private static async Task<IResult> Create(
        [FromBody] ImportProfile profile,
        [FromServices] ImportProfileService svc,
        HttpContext ctx)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                return Results.BadRequest(new { error = "Name is required" });

            bool isLocalFile = profile.TargetType?.Equals("LocalFile", StringComparison.OrdinalIgnoreCase) == true;

            if (!isLocalFile && string.IsNullOrWhiteSpace(profile.TargetTable))
                return Results.BadRequest(new { error = "TargetTable is required" });

            if (!isLocalFile && (!profile.TargetConnectionId.HasValue || profile.TargetConnectionId.Value <= 0))
                return Results.BadRequest(new { error = "TargetConnectionId is required" });

            if (isLocalFile && string.IsNullOrWhiteSpace(profile.LocalTargetPath))
                return Results.BadRequest(new { error = "LocalTargetPath is required for LocalFile target" });

            profile.Hash = "";
            var userId = GetUserId(ctx);
            var id = await svc.CreateAsync(profile, userId);
            var created = await svc.GetByIdAsync(id);
            return Results.Created($"/api/import-profiles/{id}", created);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) { return ServerError("creating import profile", ex); }
    }

    private static async Task<IResult> Update(
        int id,
        [FromBody] ImportProfile profile,
        [FromServices] ImportProfileService svc)
    {
        try
        {
            profile.Id = id;
            profile.Hash = "";
            var ok = await svc.UpdateAsync(profile);
            if (!ok) return Results.NotFound();
            return Results.Ok(await svc.GetByIdAsync(id));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) { return ServerError($"updating import profile {id}", ex); }
    }

    private static async Task<IResult> Delete(
        int id,
        [FromServices] ImportProfileService svc)
    {
        try
        {
            var ok = await svc.DeleteAsync(id);
            return ok ? Results.NoContent() : Results.NotFound();
        }
        catch (Exception ex) { return ServerError($"deleting import profile {id}", ex); }
    }

    private static async Task<IResult> Enable(int id, [FromServices] ImportProfileService svc)
    {
        try { return await svc.SetEnabledAsync(id, true) ? Results.Ok() : Results.NotFound(); }
        catch (Exception ex) { return ServerError("enabling import profile", ex); }
    }

    private static async Task<IResult> Disable(int id, [FromServices] ImportProfileService svc)
    {
        try { return await svc.SetEnabledAsync(id, false) ? Results.Ok() : Results.NotFound(); }
        catch (Exception ex) { return ServerError("disabling import profile", ex); }
    }

    private static async Task<IResult> GetByGroup(
        int groupId,
        [FromServices] ImportProfileService svc)
    {
        try { return Results.Ok(await svc.GetByGroupIdAsync(groupId)); }
        catch (Exception ex) { return ServerError($"getting import profiles for group {groupId}", ex); }
    }

    // ── Execution Handlers ─────────────────────────────────────────────

    private static async Task<IResult> Execute(
        int id,
        [FromServices] ImportProfileService svc,
        [FromServices] ImportExecutionService execSvc,
        HttpContext ctx,
        CancellationToken ct)
    {
        try
        {
            var profile = await svc.GetByIdAsync(id);
            if (profile is null) return Results.NotFound();

            var triggeredBy = ctx.User?.Identity?.Name ?? "API";
            var exec = await execSvc.ExecuteAsync(id, triggeredBy, ct);
            return Results.Ok(exec);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(499);
        }
        catch (Exception ex) { return ServerError($"executing import profile {id}", ex); }
    }

    private static async Task<IResult> GetExecutions(
        int id,
        [FromQuery] int limit = 50,
        [FromServices] ImportProfileService? svc = null)
    {
        try
        {
            return Results.Ok(await svc!.GetExecutionsAsync(id, limit));
        }
        catch (Exception ex) { return ServerError($"getting executions for import profile {id}", ex); }
    }

    private static async Task<IResult> GetExecution(
        int execId,
        [FromServices] ImportProfileService svc)
    {
        try
        {
            var exec = await svc.GetExecutionByIdAsync(execId);
            return exec is not null ? Results.Ok(exec) : Results.NotFound();
        }
        catch (Exception ex) { return ServerError($"getting execution {execId}", ex); }
    }

    private static async Task<IResult> GetExecutionErrors(
        int execId,
        [FromServices] ImportProfileService svc)
    {
        try { return Results.Ok(await svc.GetExecutionErrorsAsync(execId)); }
        catch (Exception ex) { return ServerError($"getting errors for execution {execId}", ex); }
    }

    // ── Source Operation Handlers ──────────────────────────────────────

    private static async Task<IResult> TestSource(
        [FromBody] TestImportSourceRequest req,
        CancellationToken ct)
    {
        try
        {
            var source = ImportSourceFactory.Create(req.SourceType);

            // Build a minimal ImportProfile for the test
            var profile = new ImportProfile
            {
                Name = "test",
                TargetTable = "",
                TargetConnectionId = 0,
                Hash = "",
                SourceType = req.SourceType,
                SourceConfig = req.SourceConfig,
                SourceDestinationId = req.SourceDestinationId,
                SourceFilePath = req.SourceFilePath,
                SourceFilePattern = req.SourceFilePattern,
                SourceFileSelection = req.SourceFileSelection
            };

            var (success, message) = await source.TestAsync(profile, ct);
            return Results.Ok(new { success, message });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = ex.Message });
        }
    }

    private static async Task<IResult> ListSourceFiles(
        [FromBody] TestImportSourceRequest req,
        CancellationToken ct)
    {
        try
        {
            var source = ImportSourceFactory.Create(req.SourceType);
            var profile = new ImportProfile
            {
                Name = "list",
                TargetTable = "",
                TargetConnectionId = 0,
                Hash = "",
                SourceType = req.SourceType,
                SourceConfig = req.SourceConfig,
                SourceFilePath = req.SourceFilePath,
                SourceFilePattern = req.SourceFilePattern,
                SourceFileSelection = req.SourceFileSelection
            };

            var files = await source.ListFilesAsync(profile, ct);
            return Results.Ok(new ListSourceFilesResponse { Success = true, Files = files });
        }
        catch (Exception ex)
        {
            return Results.Ok(new ListSourceFilesResponse { Success = false, ErrorMessage = ex.Message });
        }
    }

    private static async Task<IResult> Preview(
        [FromBody] PreviewImportRequest req,
        [FromServices] ImportProfileService svc,
        CancellationToken ct)
    {
        try
        {
            ImportProfile profile;
            if (req.ImportProfileId.HasValue)
            {
                var saved = await svc.GetByIdAsync(req.ImportProfileId.Value);
                if (saved is null) return Results.NotFound();
                profile = saved;
            }
            else
            {
                profile = new ImportProfile
                {
                    Name = "preview",
                    TargetTable = "",
                    TargetConnectionId = 0,
                    Hash = "",
                    SourceType = req.SourceType,
                    SourceConfig = req.SourceConfig,
                    SourceFilePath = req.SourceFilePath,
                    SourceFilePattern = req.SourceFilePattern,
                    SourceFileSelection = req.SourceFileSelection,
                    SourceFormat = req.SourceFormat,
                    FormatConfig = req.FormatConfig,
                    HttpMethod = req.HttpMethod,
                    HttpBodyTemplate = req.HttpBodyTemplate,
                    HttpPaginationEnabled = req.HttpPaginationEnabled,
                    HttpPaginationConfig = req.HttpPaginationConfig,
                    HttpDataRootPath = req.HttpDataRootPath
                };
            }

            var source = ImportSourceFactory.Create(profile.SourceType);
            var files = await source.FetchAsync(profile, ct);

            if (!files.Any())
                return Results.Ok(new PreviewImportResponse
                {
                    Success = false,
                    ErrorMessage = "No files found at source"
                });

            var formatCfg = string.IsNullOrWhiteSpace(profile.FormatConfig)
                ? new ImportFormatConfig()
                : System.Text.Json.JsonSerializer.Deserialize<ImportFormatConfig>(profile.FormatConfig)
                  ?? new ImportFormatConfig();

            var parser = ImportParserFactory.Create(profile.SourceFormat);
            var rows = new List<Dictionary<string, object?>>();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            int totalParsed = 0;

            await foreach (var row in parser.ParseAsync(files[0].Content, formatCfg, ct))
            {
                totalParsed++;
                if (!string.IsNullOrWhiteSpace(row.ParseError))
                    warnings.Add($"Row {row.LineNumber}: {row.ParseError}");
                if (row.IsSkipped) continue;

                foreach (var col in row.Columns.Keys) columns.Add(col);

                if (rows.Count < req.MaxRows)
                    rows.Add(row.Columns);

                if (rows.Count >= req.MaxRows && totalParsed > req.MaxRows)
                    break;
            }

            return Results.Ok(new PreviewImportResponse
            {
                Success = true,
                Rows = rows,
                Columns = columns.ToList(),
                TotalRowsParsed = totalParsed,
                RowsReturned = rows.Count,
                ParseWarnings = warnings
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new PreviewImportResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
    }

    // ── Target Operation Handlers ──────────────────────────────────────

    private static async Task<IResult> GetTargetSchema(
        [FromBody] GetTargetSchemaRequest req,
        [FromServices] ConnectionService connSvc,
        [FromServices] DatabaseImportTarget target,
        CancellationToken ct)
    {
        try
        {
            if (!req.TargetConnectionId.HasValue || req.TargetConnectionId.Value <= 0)
                return Results.BadRequest(new { error = "TargetConnectionId required" });

            var conn = await connSvc.GetByIdAsync(req.TargetConnectionId.Value);
            if (conn is null) return Results.NotFound(new { error = "Connection not found" });

            var schema = await target.GetTableSchemaAsync(conn, req.TargetTable, ct);
            return Results.Ok(schema);
        }
        catch (Exception ex) { return ServerError("retrieving target schema", ex); }
    }

    private static async Task<IResult> TestTarget(
        [FromBody] GetTargetSchemaRequest req,
        [FromServices] ConnectionService connSvc,
        [FromServices] DatabaseImportTarget dbTarget,
        [FromServices] LocalFileImportTarget fileTarget,
        CancellationToken ct)
    {
        try
        {
            bool isLocalFile = req.TargetType?.Equals("LocalFile", StringComparison.OrdinalIgnoreCase) == true;

            if (isLocalFile)
            {
                var (success, message) = await fileTarget.TestAsync(null!, req.TargetTable, ct);
                return Results.Ok(new { success, message });
            }

            if (!req.TargetConnectionId.HasValue)
                return Results.Ok(new { success = false, message = "TargetConnectionId required" });

            var conn = await connSvc.GetByIdAsync(req.TargetConnectionId.Value);
            if (conn is null)
                return Results.Ok(new { success = false, message = "Connection not found" });

            var (ok, msg) = await dbTarget.TestAsync(conn, req.TargetTable, ct);
            return Results.Ok(new { success = ok, message = msg });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = ex.Message });
        }
    }

    // ── Delta Sync ─────────────────────────────────────────────────────

    private static async Task<IResult> GetDeltaSyncStats(
        int id,
        [FromServices] ImportProfileService svc)
    {
        try
        {
            var profile = await svc.GetByIdAsync(id);
            if (profile is null) return Results.NotFound();

            if (!profile.DeltaSyncEnabled)
                return Results.Ok(new { enabled = false, message = "Smart Sync is not enabled for this profile" });

            var stats = await svc.GetDeltaSyncStatsAsync(id);
            return Results.Ok(new
            {
                enabled = true,
                activeRows = stats.ActiveRows,
                deletedRows = stats.DeletedRows,
                totalTrackedRows = stats.TotalTrackedRows,
                lastSyncDate = stats.LastSyncDate,
                newRowsLastRun = stats.NewRowsLastRun,
                changedRowsLastRun = stats.ChangedRowsLastRun,
                deletedRowsLastRun = stats.DeletedRowsLastRun,
                unchangedRowsLastRun = stats.UnchangedRowsLastRun
            });
        }
        catch (Exception ex) { return ServerError($"getting delta sync stats for import profile {id}", ex); }
    }

    private static async Task<IResult> ResetDeltaSync(
        int id,
        [FromServices] ImportProfileService svc)
    {
        try
        {
            var profile = await svc.GetByIdAsync(id);
            if (profile is null) return Results.NotFound();

            var deleted = await svc.ResetDeltaSyncAsync(id);
            return Results.Ok(new { message = $"Delta sync state reset. {deleted} hash entries removed." });
        }
        catch (Exception ex) { return ServerError($"resetting delta sync for {id}", ex); }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static int? GetUserId(HttpContext ctx)
    {
        var claim = ctx.User?.FindFirst("sub") ?? ctx.User?.FindFirst("userId");
        return claim is not null && int.TryParse(claim.Value, out var id) ? id : null;
    }

    private static IResult ServerError(string context, Exception ex)
    {
        Log.Error(ex, "ImportProfilesEndpoints: error {Context}", context);
        return Results.Problem($"Error {context}: {ex.Message}");
    }
}
