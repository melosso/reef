using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace Reef.Core.Scripting;

/// <summary>
/// Runs scripts as child processes (no shell), feeding context as JSON on stdin
/// and capturing stdout/stderr/exit code. Cross-platform interpreter resolution,
/// process-tree kill on timeout, and a minimal inherited environment so secrets
/// in the host process env don't leak into the script unless explicitly allowed.
/// </summary>
public class ProcessScriptRunner : IScriptRunner
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ProcessScriptRunner>();

    public async Task<ScriptExecutionResult> RunAsync(ScriptExecutionRequest request, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        string? tempScriptPath = null;

        try
        {
            var scriptPath = request.IsInline
                ? tempScriptPath = WriteInlineScript(request.Interpreter, request.ScriptPathOrInline)
                : request.ScriptPathOrInline;

            if (!request.IsInline && !File.Exists(scriptPath))
            {
                return new ScriptExecutionResult
                {
                    Success = false,
                    ExitCode = -1,
                    ErrorMessage = $"Script file not found: {scriptPath}",
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                };
            }

            var (fileName, arguments) = ResolveInterpreter(request.Interpreter, scriptPath);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = request.WorkingDirectory ?? Path.GetTempPath()
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            ApplyMinimalEnvironment(psi, request.EnvAllowlist);

            using var process = new Process { StartInfo = psi };
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

            try
            {
                await process.StandardInput.WriteAsync(request.StdinJson.AsMemory(), linkedCts.Token);
            }
            catch (IOException)
            {
                // Script may not read stdin at all (broken pipe) - not fatal.
            }
            finally
            {
                try { process.StandardInput.Close(); } catch (IOException) { /* already closed by the child */ }
            }

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);

                var timedOut = timeoutCts.IsCancellationRequested;
                stopwatch.Stop();
                return new ScriptExecutionResult
                {
                    Success = false,
                    ExitCode = -1,
                    Stdout = await SafeAwait(stdoutTask),
                    Stderr = await SafeAwait(stderrTask),
                    ErrorMessage = timedOut
                        ? $"Script timed out after {request.TimeoutSeconds}s and was killed"
                        : "Script execution was cancelled",
                    ElapsedMs = stopwatch.ElapsedMilliseconds
                };
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            stopwatch.Stop();

            return new ScriptExecutionResult
            {
                Success = process.ExitCode == 0,
                ExitCode = process.ExitCode,
                Stdout = stdout,
                Stderr = stderr,
                ErrorMessage = process.ExitCode == 0 ? null : $"Script exited with code {process.ExitCode}",
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Win32Exception ex)
        {
            // Interpreter binary not found/not runnable (e.g. pwsh/cmd missing
            // on this host). One line, no stack trace - and only logged when
            // the caller wants it (see LogIfUnavailable doc comment).
            stopwatch.Stop();
            if (request.LogIfUnavailable)
                Log.Warning("Interpreter '{Interpreter}' unavailable: {Message}", request.Interpreter, ex.Message);
            return new ScriptExecutionResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = $"Interpreter '{request.Interpreter}' unavailable: {ex.Message}",
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log.Error(ex, "Script execution failed: {Message}", ex.Message);
            return new ScriptExecutionResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = $"Script execution exception: {ex.Message}",
                ElapsedMs = stopwatch.ElapsedMilliseconds
            };
        }
        finally
        {
            if (tempScriptPath != null)
            {
                try { File.Delete(tempScriptPath); } catch { /* best effort cleanup */ }
            }
        }
    }

    private static async Task<string> SafeAwait(Task<string> task)
    {
        try { return await task; } catch { return ""; }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have exited between the check and the kill - ignore.
        }
    }

    private static string WriteInlineScript(string interpreter, string source)
    {
        var ext = interpreter.ToLowerInvariant() switch
        {
            "pwsh" or "powershell" => ".ps1",
            "python" or "python3" => ".py",
            "node" or "nodejs" => ".js",
            "bash" => ".sh",
            "sh" => ".sh",
            "cmd" => ".bat",
            _ => ".txt"
        };

        var path = Path.Combine(Path.GetTempPath(), $"reef-script-{Guid.NewGuid():N}{ext}");
        File.WriteAllText(path, source);
        return path;
    }

    private static (string FileName, List<string> Arguments) ResolveInterpreter(string interpreter, string scriptPath)
    {
        return interpreter.ToLowerInvariant() switch
        {
            "pwsh" or "powershell" => ("pwsh", new List<string> { "-NoProfile", "-NonInteractive", "-File", scriptPath }),
            "python" or "python3" => ("python3", new List<string> { scriptPath }),
            "node" or "nodejs" => ("node", new List<string> { scriptPath }),
            "bash" => ("bash", new List<string> { scriptPath }),
            "sh" => ("sh", new List<string> { scriptPath }),
            "cmd" => ("cmd", new List<string> { "/c", scriptPath }),
            _ => throw new ArgumentException($"Unsupported script interpreter: {interpreter}")
        };
    }

    private static void ApplyMinimalEnvironment(ProcessStartInfo psi, List<string>? envAllowlist)
    {
        psi.EnvironmentVariables.Clear();

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
            psi.EnvironmentVariables["PATH"] = path;

        if (OperatingSystem.IsWindows())
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrEmpty(systemRoot))
                psi.EnvironmentVariables["SystemRoot"] = systemRoot;
        }

        if (envAllowlist == null) return;

        foreach (var name in envAllowlist)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value != null)
                psi.EnvironmentVariables[name] = value;
        }
    }
}
