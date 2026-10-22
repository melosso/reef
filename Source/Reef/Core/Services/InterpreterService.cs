using System.Collections.Concurrent;
using Reef.Core.Scripting;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Probes whether the script interpreters used by Profile pre/post-process
/// Script steps are actually runnable on this host. Runs each interpreter
/// through the real IScriptRunner path with a trivial "print ok" script -
/// the same execution path production Script steps use - rather than just
/// checking PATH for the binary name, so a "yes" here means scripts will
/// actually run, not just that something with that name exists.
/// </summary>
public class InterpreterService(IScriptRunner scriptRunner)
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<InterpreterService>();

    // CheckAllAsync runs on every Profile editor page load (it's how the UI
    // greys out unavailable interpreters) - logging "pwsh unavailable" on
    // every single load would spam the log for something that's static
    // per-host. Logged once per interpreter per process lifetime instead;
    // an explicit manual re-check (the Admin "Test" button) always logs.
    private static readonly ConcurrentDictionary<string, bool> _loggedUnavailable = new();

    public static readonly IReadOnlyList<InterpreterDefinition> KnownInterpreters = new List<InterpreterDefinition>
    {
        new("pwsh", "PowerShell", "Write-Output 'ok'"),
        new("python", "Python", "print('ok')"),
        new("node", "Node.js", "console.log('ok')"),
        new("bash", "Bash", "echo ok"),
        new("sh", "sh", "echo ok"),
        new("cmd", "cmd (Windows)", "echo ok"),
    };

    public async Task<List<InterpreterStatus>> CheckAllAsync(CancellationToken ct = default)
    {
        var results = new List<InterpreterStatus>();
        foreach (var def in KnownInterpreters)
        {
            var status = await CheckAsync(def.Name, logIfUnavailable: false, ct);
            if (!status.Available && _loggedUnavailable.TryAdd(def.Name, true))
                Log.Warning("Interpreter '{Interpreter}' unavailable: {Error}", def.Name, status.Error);

            results.Add(status);
        }
        return results;
    }

    /// <summary>Explicit single re-check (Admin's "Test" button) - always logs.</summary>
    public Task<InterpreterStatus> CheckOneAsync(string name, CancellationToken ct = default) =>
        CheckAsync(name, logIfUnavailable: true, ct);

    private async Task<InterpreterStatus> CheckAsync(string name, bool logIfUnavailable, CancellationToken ct)
    {
        var def = KnownInterpreters.FirstOrDefault(d =>
            d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (def == null)
        {
            return new InterpreterStatus(name, name, false, null, $"Unknown interpreter: {name}");
        }

        var result = await scriptRunner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = def.Name,
            ScriptPathOrInline = def.ProbeScript,
            IsInline = true,
            StdinJson = "{}",
            TimeoutSeconds = 10,
            LogIfUnavailable = logIfUnavailable
        }, ct);

        var available = result.Success && result.Stdout.Contains("ok", StringComparison.Ordinal);
        if (available) _loggedUnavailable.TryRemove(def.Name, out _);

        return new InterpreterStatus(
            def.Name,
            def.DisplayName,
            available,
            available ? result.Stdout.Trim() : null,
            available ? null : (result.ErrorMessage ?? result.Stderr.Trim()));
    }
}

public record InterpreterDefinition(string Name, string DisplayName, string ProbeScript);

public record InterpreterStatus(
    string Name,
    string DisplayName,
    bool Available,
    string? Output,
    string? Error);
