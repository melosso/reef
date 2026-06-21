using Reef.Core.Scripting;

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
            results.Add(await CheckOneAsync(def.Name, ct));
        }
        return results;
    }

    public async Task<InterpreterStatus> CheckOneAsync(string name, CancellationToken ct = default)
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
            TimeoutSeconds = 10
        }, ct);

        var available = result.Success && result.Stdout.Contains("ok", StringComparison.Ordinal);
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
