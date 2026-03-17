using System.Text;
using System.Text.Json;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Serialises collected import rows to a temporary file in the requested format,
/// then delivers the file to each fan-out output target via the existing DestinationService.
/// </summary>
public class ImportFileOutputService(DestinationService destinationService)
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ImportFileOutputService>();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<List<ImportOutputTargetResult>> FanOutAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        IReadOnlyList<ImportOutputTarget> targets,
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var results = new List<ImportOutputTargetResult>();

        foreach (var target in targets.Where(t => t.IsEnabled).OrderBy(t => t.SortOrder))
        {
            ct.ThrowIfCancellationRequested();
            string? tempPath = null;

            try
            {
                tempPath = await SerializeToTempFileAsync(rows, target.OutputFormat, target.FilenameTemplate, profile, ct);

                var dest = await destinationService.GetByIdForExecutionAsync(target.DestinationId);
                if (dest is null)
                {
                    results.Add(new ImportOutputTargetResult(
                        target.DestinationId,
                        target.DestinationName ?? target.DestinationId.ToString(),
                        "Failed",
                        null,
                        $"Destination {target.DestinationId} not found",
                        0));
                    continue;
                }

                var (success, finalPath, error) = await destinationService.SaveToDestinationAsync(
                    tempPath,
                    dest.Type,
                    dest.ConfigurationJson);

                results.Add(new ImportOutputTargetResult(
                    target.DestinationId,
                    dest.Name,
                    success ? "Success" : "Failed",
                    finalPath,
                    error,
                    rows.Count));

                if (success)
                    Log.Information("ImportFileOutputService: target {DestName} ({DestType}) → {Path}",
                        dest.Name, dest.Type, finalPath);
                else
                    Log.Warning("ImportFileOutputService: target {DestName} failed: {Error}", dest.Name, error);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ImportFileOutputService: unexpected error for destination {DestId}", target.DestinationId);
                results.Add(new ImportOutputTargetResult(
                    target.DestinationId,
                    target.DestinationName ?? target.DestinationId.ToString(),
                    "Failed",
                    null,
                    ex.Message,
                    0));
            }
            finally
            {
                if (tempPath is not null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* best-effort */ }
                }
            }
        }

        return results;
    }

    // ── Binary Raw Fan-Out ────────────────────────────────────────────────

    public async Task<List<ImportOutputTargetResult>> FanOutRawAsync(
        IReadOnlyList<ImportSourceFile> files,
        IReadOnlyList<ImportOutputTarget> targets,
        ImportProfile profile,
        CancellationToken ct = default)
    {
        var results = new List<ImportOutputTargetResult>();

        foreach (var target in targets.Where(t => t.IsEnabled).OrderBy(t => t.SortOrder))
        {
            ct.ThrowIfCancellationRequested();

            var dest = await destinationService.GetByIdForExecutionAsync(target.DestinationId);
            if (dest is null)
            {
                results.Add(new ImportOutputTargetResult(
                    target.DestinationId,
                    target.DestinationName ?? target.DestinationId.ToString(),
                    "Failed",
                    null,
                    $"Destination {target.DestinationId} not found",
                    0));
                continue;
            }

            int filesDelivered = 0;
            string? lastPath = null;
            string? lastError = null;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                string? tempPath = null;
                try
                {
                    var filename = ResolveFilename(target.FilenameTemplate, profile, Path.GetExtension(file.Identifier).TrimStart('.'));
                    if (string.IsNullOrEmpty(filename)) filename = Path.GetFileName(file.Identifier);
                    var uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
                    var ext = Path.GetExtension(filename);
                    tempPath = Path.Combine(Path.GetTempPath(),
                        $"{Path.GetFileNameWithoutExtension(filename)}_{uniqueSuffix}{ext}");

                    file.Content.Position = 0;
                    await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await file.Content.CopyToAsync(fs, ct);
                    }

                    var (success, finalPath, error) = await destinationService.SaveToDestinationAsync(
                        tempPath, dest.Type, dest.ConfigurationJson);

                    if (success)
                    {
                        filesDelivered++;
                        lastPath = finalPath;
                        Log.Information("ImportFileOutputService (raw): target {DestName} → {Path}", dest.Name, finalPath);
                    }
                    else
                    {
                        lastError = error;
                        Log.Warning("ImportFileOutputService (raw): target {DestName} failed: {Error}", dest.Name, error);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "ImportFileOutputService (raw): unexpected error for destination {DestId}", target.DestinationId);
                    lastError = ex.Message;
                }
                finally
                {
                    if (tempPath is not null && File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); } catch { /* best-effort */ }
                    }
                }
            }

            results.Add(new ImportOutputTargetResult(
                target.DestinationId,
                dest.Name,
                lastError is null || filesDelivered == files.Count ? "Success" : "Failed",
                lastPath,
                lastError,
                filesDelivered));
        }

        return results;
    }

    // ── Serialisation ─────────────────────────────────────────────────────

    private async Task<string> SerializeToTempFileAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        string format,
        string? filenameTemplate,
        ImportProfile profile,
        CancellationToken ct)
    {
        var filename = ResolveFilename(filenameTemplate, profile, format);
        // Add a random suffix to guarantee uniqueness across concurrent executions
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
        var ext = Path.GetExtension(filename);
        var tempPath = Path.Combine(Path.GetTempPath(),
            $"{Path.GetFileNameWithoutExtension(filename)}_{uniqueSuffix}{ext}");

        switch (format.ToUpperInvariant())
        {
            case "JSON":
                await WriteJsonAsync(tempPath, rows, ct);
                break;

            case "JSONL":
                await WriteJsonLAsync(tempPath, rows, ct);
                break;

            case "CSV":
                await WriteCsvAsync(tempPath, rows, ct);
                break;

            case "XML":
                await WriteXmlAsync(tempPath, rows, ct);
                break;

            default:
                await WriteJsonAsync(tempPath, rows, ct);
                break;
        }

        return tempPath;
    }

    private static string ResolveFilename(string? template, ImportProfile profile, string format)
    {
        var ext = format.ToLowerInvariant() switch
        {
            "json" or "jsonl" => "json",
            "csv" => "csv",
            "xml" => "xml",
            _ => "json"
        };

        var ts = DateTime.UtcNow;
        var raw = string.IsNullOrWhiteSpace(template)
            ? $"{{profile}}_{{timestamp}}.{ext}"
            : template;

        return raw
            .Replace("{profile}", SanitizeFilename(profile.Name))
            .Replace("{timestamp}", ts.ToString("yyyyMMddHHmmss"))
            .Replace("{date}", ts.ToString("yyyyMMdd"))
            .Replace("{guid}", Guid.NewGuid().ToString("N")[..8]);
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static async Task WriteJsonAsync(string path, IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(fs, rows, _jsonOpts, ct);
    }

    private static async Task WriteJsonLAsync(string path, IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var line = JsonSerializer.Serialize(row, _jsonOpts);
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }

    private static async Task WriteCsvAsync(string path, IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            await File.WriteAllTextAsync(path, "", Encoding.UTF8, ct);
            return;
        }

        var columns = rows[0].Keys.ToList();
        await using var writer = new StreamWriter(path, append: false, Encoding.UTF8);

        // Header
        await writer.WriteLineAsync(string.Join(",", columns.Select(CsvEscape)));

        // Rows
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var values = columns.Select(c => row.TryGetValue(c, out var v) ? CsvEscape(v?.ToString() ?? "") : "");
            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static async Task WriteXmlAsync(string path, IReadOnlyList<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<Rows>");

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            sb.AppendLine("  <Row>");
            foreach (var kvp in row)
            {
                var tagName = System.Text.RegularExpressions.Regex.Replace(kvp.Key, @"[^\w]", "_");
                var val = System.Security.SecurityElement.Escape(kvp.Value?.ToString() ?? "");
                sb.AppendLine($"    <{tagName}>{val}</{tagName}>");
            }
            sb.AppendLine("  </Row>");
        }

        sb.AppendLine("</Rows>");
        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct);
    }
}
