namespace Reef.Core.Scripting;

/// <summary>
/// Request to execute a script as a child process.
/// The script receives <see cref="StdinJson"/> on stdin and is expected to
/// write its result to stdout. Stderr is captured for diagnostics only.
/// </summary>
public class ScriptExecutionRequest
{
    /// <summary>
    /// Interpreter to use: pwsh, python, node, bash, sh, cmd
    /// </summary>
    public required string Interpreter { get; init; }

    /// <summary>
    /// Either a path to an existing script file, or the inline script source
    /// (see <see cref="IsInline"/>).
    /// </summary>
    public required string ScriptPathOrInline { get; init; }

    /// <summary>
    /// When true, <see cref="ScriptPathOrInline"/> is the script source and is
    /// written to a temp file before execution. When false it is a path.
    /// </summary>
    public bool IsInline { get; init; }

    /// <summary>
    /// JSON payload written to the process stdin.
    /// </summary>
    public required string StdinJson { get; init; }

    /// <summary>
    /// Max time to let the process run before it (and its process tree) is killed.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Names of environment variables to copy from the current process into the
    /// child process. The child otherwise gets a minimal environment (PATH only).
    /// </summary>
    public List<string>? EnvAllowlist { get; init; }

    /// <summary>
    /// Working directory for the script process. Defaults to a temp directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

public class ScriptExecutionResult
{
    public required bool Success { get; init; }
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public long ElapsedMs { get; init; }
}

public interface IScriptRunner
{
    Task<ScriptExecutionResult> RunAsync(ScriptExecutionRequest request, CancellationToken ct = default);
}
