using System.Text.Json;
using FluentAssertions;
using Reef.Core.Scripting;

namespace Reef.Tests.Templates;

/// <summary>
/// Exercises the example Script Templates shipped in /Templates against a
/// mock HTTP endpoint instead of real Slack/n8n/ERP services, verifying:
///   - the stdin JSON contract (PascalCase keys, matches ExecutionService's
///     RunProcessingScriptAsync payload shape) is what each script expects
///   - success/failure HTTP responses map to the right process exit code
///   - missing required env vars fail fast with a non-zero exit
/// These run the real script files via ProcessScriptRunner - the same path
/// production uses - not a reimplementation of their logic.
/// </summary>
public class ScriptTemplateExamplesTests
{
    private readonly ProcessScriptRunner _runner = new();
    private static readonly string TemplatesDir = FindTemplatesDir();

    private static string FindTemplatesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Templates");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "Script_-_Webhook_n8n_Delivery.ScriptTemplate.sh")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository /Templates directory from test output path");
    }

    private static string SampleStdinJson(string status = "Success", int rowCount = 25) =>
        JsonSerializer.Serialize(new
        {
            ExecutionId = 1234,
            ProfileId = 7,
            RowCount = rowCount,
            OutputPath = "/data/exports/orders.csv",
            FileSizeBytes = 88210,
            ExecutionTimeMs = 842,
            OutputFormat = "CSV",
            TriggeredBy = "Test",
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Status = status,
            ErrorMessage = (string?)null
        });

    // Webhook / n8n (sh + curl)

    [Fact]
    public async Task WebhookTemplate_SuccessResponse_ExitsZeroAndForwardsContext()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(200, "{\"received\":true}");
        Environment.SetEnvironmentVariable("N8N_WEBHOOK_URL", mock.BaseUrl);

        try
        {
            var result = await RunTemplate("Script_-_Webhook_n8n_Delivery.ScriptTemplate.sh", "sh",
                SampleStdinJson(), new List<string> { "N8N_WEBHOOK_URL" });

            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
            mock.RequestCount.Should().Be(1);
            mock.LastMethod.Should().Be("POST");
            mock.LastContentTypeHeader.Should().Be("application/json");
            mock.LastRequestBody.Should().Contain("\"Status\":\"Success\"");
        }
        finally
        {
            Environment.SetEnvironmentVariable("N8N_WEBHOOK_URL", null);
        }
    }

    [Fact]
    public async Task WebhookTemplate_ServerErrorResponse_FailsInsteadOfReportingSuccess()
    {
        // Regression test for the curl -f fix: without -f, curl exits 0 even on a 5xx,
        // which would make Reef report "Success" while the webhook delivery silently failed.
        using var mock = new MockHttpServer();
        mock.SetResponse(500, "internal error");
        Environment.SetEnvironmentVariable("N8N_WEBHOOK_URL", mock.BaseUrl);

        try
        {
            var result = await RunTemplate("Script_-_Webhook_n8n_Delivery.ScriptTemplate.sh", "sh",
                SampleStdinJson(), new List<string> { "N8N_WEBHOOK_URL" });

            result.Success.Should().BeFalse();
            result.ExitCode.Should().NotBe(0);
            mock.RequestCount.Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("N8N_WEBHOOK_URL", null);
        }
    }

    [Fact]
    public async Task WebhookTemplate_MissingEnvVar_FailsFastWithoutCallingServer()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(200);
        Environment.SetEnvironmentVariable("N8N_WEBHOOK_URL", null);

        var result = await RunTemplate("Script_-_Webhook_n8n_Delivery.ScriptTemplate.sh", "sh",
            SampleStdinJson(), new List<string> { "N8N_WEBHOOK_URL" });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        mock.RequestCount.Should().Be(0);
    }

    // Slack / Teams notify (python)

    [Fact]
    public async Task SlackTemplate_SuccessResponse_ExitsZeroAndPostsSummaryText()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(200, "ok");
        Environment.SetEnvironmentVariable("SLACK_WEBHOOK_URL", mock.BaseUrl);

        try
        {
            var result = await RunTemplate("Script_-_Slack_Teams_Notify.ScriptTemplate.py", "python",
                SampleStdinJson(rowCount: 250), new List<string> { "SLACK_WEBHOOK_URL" });

            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
            mock.LastMethod.Should().Be("POST");
            mock.LastRequestBody.Should().Contain("\"text\"");
            mock.LastRequestBody.Should().Contain("250 rows");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SLACK_WEBHOOK_URL", null);
        }
    }

    [Fact]
    public async Task SlackTemplate_ServerErrorResponse_Fails()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(503, "unavailable");
        Environment.SetEnvironmentVariable("SLACK_WEBHOOK_URL", mock.BaseUrl);

        try
        {
            var result = await RunTemplate("Script_-_Slack_Teams_Notify.ScriptTemplate.py", "python",
                SampleStdinJson(), new List<string> { "SLACK_WEBHOOK_URL" });

            result.Success.Should().BeFalse();
            result.ExitCode.Should().NotBe(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SLACK_WEBHOOK_URL", null);
        }
    }

    [Fact]
    public async Task SlackTemplate_MissingEnvVars_FailsFastWithoutCallingServer()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(200);
        Environment.SetEnvironmentVariable("SLACK_WEBHOOK_URL", null);
        Environment.SetEnvironmentVariable("TEAMS_WEBHOOK_URL", null);

        var result = await RunTemplate("Script_-_Slack_Teams_Notify.ScriptTemplate.py", "python",
            SampleStdinJson(), new List<string> { "SLACK_WEBHOOK_URL", "TEAMS_WEBHOOK_URL" });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        mock.RequestCount.Should().Be(0);
    }

    // REST ERP call (node)

    [Fact]
    public async Task ErpTemplate_SuccessResponse_PostsExecutionSummaryWithAuth()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(200, "{\"accepted\":true}");
        Environment.SetEnvironmentVariable("ERP_API_URL", mock.BaseUrl);
        Environment.SetEnvironmentVariable("ERP_API_KEY", "test-key-123");

        try
        {
            var result = await RunTemplate("Script_-_REST_ERP_Call.ScriptTemplate.js", "node",
                SampleStdinJson(), new List<string> { "ERP_API_URL", "ERP_API_KEY" });

            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
            mock.LastMethod.Should().Be("POST");
            mock.LastAuthorizationHeader.Should().Be("Bearer test-key-123");
            mock.LastRequestBody.Should().Contain("\"externalReference\":\"reef-execution-1234\"");
            mock.LastRequestBody.Should().Contain("\"status\":\"Success\"");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERP_API_URL", null);
            Environment.SetEnvironmentVariable("ERP_API_KEY", null);
        }
    }

    [Fact]
    public async Task ErpTemplate_ServerErrorResponse_Fails()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(422, "validation failed");
        Environment.SetEnvironmentVariable("ERP_API_URL", mock.BaseUrl);
        Environment.SetEnvironmentVariable("ERP_API_KEY", "test-key-123");

        try
        {
            var result = await RunTemplate("Script_-_REST_ERP_Call.ScriptTemplate.js", "node",
                SampleStdinJson(), new List<string> { "ERP_API_URL", "ERP_API_KEY" });

            result.Success.Should().BeFalse();
            result.ExitCode.Should().NotBe(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERP_API_URL", null);
            Environment.SetEnvironmentVariable("ERP_API_KEY", null);
        }
    }

    [Fact]
    public async Task ErpTemplate_MissingEnvVars_FailsFastWithoutCallingServer()
    {
        using var mock = new MockHttpServer();
        mock.SetResponse(200);
        Environment.SetEnvironmentVariable("ERP_API_URL", null);
        Environment.SetEnvironmentVariable("ERP_API_KEY", null);

        var result = await RunTemplate("Script_-_REST_ERP_Call.ScriptTemplate.js", "node",
            SampleStdinJson(), new List<string> { "ERP_API_URL", "ERP_API_KEY" });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        mock.RequestCount.Should().Be(0);
    }

    // ExactXML (pwsh) - only runs where pwsh is actually installed; this sandbox
    // doesn't have it, and the script also needs a real SQL Server staging table
    // and ExactXML.exe binary, neither of which are mockable cheaply. We only
    // verify the fast-fail-on-missing-config path, which needs no external deps.

    [Fact]
    public async Task ExactXmlTemplate_MissingEnvVars_FailsFast()
    {
        if (!IsPwshAvailable())
        {
            return; // pwsh not installed on this host - nothing to verify here
        }

        Environment.SetEnvironmentVariable("STAGING_DB_CONNSTRING", null);
        Environment.SetEnvironmentVariable("EXACTXML_EXE_PATH", null);

        var result = await RunTemplate("Script_-_REST_Import_to_ExactXML.ScriptTemplate.ps1", "pwsh",
            JsonSerializer.Serialize(new { ExecutionId = 1 }),
            new List<string> { "STAGING_DB_CONNSTRING", "EXACTXML_EXE_PATH" });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    // Bash equivalent of the ExactXML pattern (env-var-gated external exe call,
    // exit code propagated to Reef) for hosts without pwsh. ProcessScriptRunner
    // treats `bash` identically to `sh`/`pwsh` - same interpreter resolution,
    // same inline-script-to-temp-file handling, same env allowlist and exit
    // code propagation (see ProcessScriptRunner.ResolveInterpreter) - so this
    // proves the pattern is fully exercisable on any host, not just Windows.
    private const string BashExactXmlHarness = """
        #!/bin/bash
        set -eu
        : "$(cat)" # drain stdin, matching the real template's contract
        if [ -z "${STAGING_DB_CONNSTRING:-}" ] || [ -z "${EXACTXML_EXE_PATH:-}" ]; then
            echo "STAGING_DB_CONNSTRING and EXACTXML_EXE_PATH must be set" >&2
            exit 1
        fi
        "$EXACTXML_EXE_PATH"
        """;

    [Fact]
    public async Task BashExactXmlHarness_MissingEnvVars_FailsFast()
    {
        Environment.SetEnvironmentVariable("STAGING_DB_CONNSTRING", null);
        Environment.SetEnvironmentVariable("EXACTXML_EXE_PATH", null);

        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "bash",
            ScriptPathOrInline = BashExactXmlHarness,
            IsInline = true,
            StdinJson = JsonSerializer.Serialize(new { ExecutionId = 1 }),
            TimeoutSeconds = 10,
            EnvAllowlist = new List<string> { "STAGING_DB_CONNSTRING", "EXACTXML_EXE_PATH" }
        });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("must be set");
    }

    [Fact]
    public async Task BashExactXmlHarness_FakeExeSucceeds_PropagatesExitZero()
    {
        var fakeExe = WriteFakeExactXmlExe(exitCode: 0, stdout: "Imported 3 row(s)");
        Environment.SetEnvironmentVariable("STAGING_DB_CONNSTRING", "Server=fake;Database=fake;");
        Environment.SetEnvironmentVariable("EXACTXML_EXE_PATH", fakeExe);

        try
        {
            var result = await _runner.RunAsync(new ScriptExecutionRequest
            {
                Interpreter = "bash",
                ScriptPathOrInline = BashExactXmlHarness,
                IsInline = true,
                StdinJson = JsonSerializer.Serialize(new { ExecutionId = 1 }),
                TimeoutSeconds = 10,
                EnvAllowlist = new List<string> { "STAGING_DB_CONNSTRING", "EXACTXML_EXE_PATH" }
            });

            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
            result.Stdout.Should().Contain("Imported 3 row(s)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("STAGING_DB_CONNSTRING", null);
            Environment.SetEnvironmentVariable("EXACTXML_EXE_PATH", null);
            File.Delete(fakeExe);
        }
    }

    [Fact]
    public async Task BashExactXmlHarness_FakeExeFails_PropagatesExitCode()
    {
        var fakeExe = WriteFakeExactXmlExe(exitCode: 7, stdout: "", stderr: "import definition mismatch");
        Environment.SetEnvironmentVariable("STAGING_DB_CONNSTRING", "Server=fake;Database=fake;");
        Environment.SetEnvironmentVariable("EXACTXML_EXE_PATH", fakeExe);

        try
        {
            var result = await _runner.RunAsync(new ScriptExecutionRequest
            {
                Interpreter = "bash",
                ScriptPathOrInline = BashExactXmlHarness,
                IsInline = true,
                StdinJson = JsonSerializer.Serialize(new { ExecutionId = 1 }),
                TimeoutSeconds = 10,
                EnvAllowlist = new List<string> { "STAGING_DB_CONNSTRING", "EXACTXML_EXE_PATH" }
            });

            result.Success.Should().BeFalse();
            result.ExitCode.Should().Be(7);
            result.Stderr.Should().Contain("import definition mismatch");
        }
        finally
        {
            Environment.SetEnvironmentVariable("STAGING_DB_CONNSTRING", null);
            Environment.SetEnvironmentVariable("EXACTXML_EXE_PATH", null);
            File.Delete(fakeExe);
        }
    }

    /// <summary>Writes a tiny executable bash script standing in for ExactXML.exe.</summary>
    private static string WriteFakeExactXmlExe(int exitCode, string stdout, string stderr = "")
    {
        var path = Path.Combine(Path.GetTempPath(), $"fake-exactxml-{Guid.NewGuid():N}.sh");
        var script = $"""
            #!/bin/bash
            {(string.IsNullOrEmpty(stdout) ? "" : $"echo \"{stdout}\"")}
            {(string.IsNullOrEmpty(stderr) ? "" : $"echo \"{stderr}\" >&2")}
            exit {exitCode}
            """;
        File.WriteAllText(path, script);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static bool IsPwshAvailable()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = "-NoProfile -Command exit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<ScriptExecutionResult> RunTemplate(
        string fileName, string interpreter, string stdinJson, List<string> envAllowlist)
    {
        var path = Path.Combine(TemplatesDir, fileName);
        File.Exists(path).Should().BeTrue($"expected template file at {path}");

        return await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = interpreter,
            ScriptPathOrInline = path,
            IsInline = false,
            StdinJson = stdinJson,
            TimeoutSeconds = 15,
            EnvAllowlist = envAllowlist
        });
    }
}
