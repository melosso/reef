using System.Text;
using System.Text.Json;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Targets;

/// <summary>
/// Writes imported rows to a local file (CSV, JSON, or JSONL).
/// Used when ImportProfile.TargetType = "LocalFile".
/// </summary>
public class LocalFileImportTarget : IImportTarget
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<LocalFileImportTarget>();

    public async Task<ImportBatchResult> WriteBatchAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct = default)
    {
        if (!rows.Any()) return new ImportBatchResult();

        var path = context.TargetFilePath
            ?? throw new InvalidOperationException("LocalFileImportTarget: TargetFilePath is not configured");

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        bool append = context.TargetWriteMode?.Equals("Append", StringComparison.OrdinalIgnoreCase) == true;
        var format = ResolveFormat(context.TargetFormat, path);

        int written = format switch
        {
            "JSON"  => await AppendJsonAsync(path, rows, append, ct),
            "JSONL" => await AppendJsonLinesAsync(path, rows, append, ct),
            _       => await AppendCsvAsync(path, rows, append, ct)
        };

        Log.Debug("LocalFileImportTarget: wrote {Count} rows to {Path} ({Format})", written, path, format);

        return new ImportBatchResult { RowsInserted = written };
    }

    public async Task<ImportBatchResult> FullReplaceAsync(
        List<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct = default)
    {
        var path = context.TargetFilePath
            ?? throw new InvalidOperationException("LocalFileImportTarget: TargetFilePath is not configured");

        // Full replace = always overwrite
        var overwriteCtx = new ImportWriteContext
        {
            TargetType = context.TargetType,
            TargetFilePath = path,
            TargetFormat = context.TargetFormat,
            TargetWriteMode = "Overwrite"
        };

        return await WriteBatchAsync(rows, overwriteCtx, ct);
    }

    public Task<int> ApplyDeletesAsync(
        IReadOnlyList<string> deletedReefIds,
        string reefIdColumn,
        ImportProfile profile,
        CancellationToken ct = default)
    {
        // File targets do not support row-level deletes
        Log.Warning("LocalFileImportTarget: ApplyDeletesAsync not supported for file targets");
        return Task.FromResult(0);
    }

    public Task<List<TargetColumnInfo>> GetTableSchemaAsync(
        Connection connection,
        string tableName,
        CancellationToken ct = default)
    {
        // No schema for file targets
        return Task.FromResult(new List<TargetColumnInfo>());
    }

    public Task<(bool Success, string? Message)> TestAsync(
        Connection connection,
        string tableName,
        CancellationToken ct = default)
    {
        // tableName is used as the file path for LocalFile targets
        if (string.IsNullOrWhiteSpace(tableName))
            return Task.FromResult((false, (string?)"No file path configured"));

        try
        {
            var dir = Path.GetDirectoryName(tableName);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                return Task.FromResult((false, (string?)$"Directory does not exist: {dir}"));

            // Try a write test
            var testPath = tableName + ".reef_test";
            File.WriteAllText(testPath, "test");
            File.Delete(testPath);
            return Task.FromResult((true, (string?)$"Path is writable: {tableName}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, (string?)$"Cannot write to path: {ex.Message}"));
        }
    }

    // ── Private helpers ────────────────────────────────────────────────

    private static async Task<int> AppendCsvAsync(
        string path,
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool append,
        CancellationToken ct)
    {
        bool fileExists = File.Exists(path) && append;
        var headers = rows.SelectMany(r => r.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await using var writer = new StreamWriter(path, append: append, Encoding.UTF8);

        // Write header row only for new files (not appending)
        if (!fileExists)
            await writer.WriteLineAsync(string.Join(",", headers.Select(QuoteCsv)));

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var values = headers.Select(h => QuoteCsv(row.GetValueOrDefault(h)?.ToString() ?? ""));
            await writer.WriteLineAsync(string.Join(",", values));
        }

        return rows.Count;
    }

    private static async Task<int> AppendJsonAsync(
        string path,
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool append,
        CancellationToken ct)
    {
        List<Dictionary<string, object?>> allRows;

        if (append && File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path, ct);
            try
            {
                allRows = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(existing)
                          ?? new List<Dictionary<string, object?>>();
            }
            catch { allRows = new List<Dictionary<string, object?>>(); }
            allRows.AddRange(rows);
        }
        else
        {
            allRows = rows.ToList();
        }

        var json = JsonSerializer.Serialize(allRows, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
        return rows.Count;
    }

    private static async Task<int> AppendJsonLinesAsync(
        string path,
        IReadOnlyList<Dictionary<string, object?>> rows,
        bool append,
        CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, append: append, Encoding.UTF8);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(row));
        }
        return rows.Count;
    }

    /// <summary>
    /// Resolves the output format. When the configured format is the default CSV,
    /// infers JSON or JSONL from the file extension so users don't have to set both.
    /// An explicitly configured non-CSV format always wins.
    /// </summary>
    private static string ResolveFormat(string? configuredFormat, string? filePath)
    {
        var fmt = (configuredFormat ?? "CSV").ToUpperInvariant();
        if (fmt != "CSV") return fmt;

        return Path.GetExtension(filePath ?? "").ToLowerInvariant() switch
        {
            ".json"  => "JSON",
            ".jsonl" => "JSONL",
            _        => "CSV"
        };
    }

    private static string QuoteCsv(string? value)
    {
        value ??= "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
