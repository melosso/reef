using FluentAssertions;
using Reef.Core.Scripting;

namespace Reef.Tests.Scripting;

public class ProcessScriptRunnerTests
{
    private readonly ProcessScriptRunner _runner = new();

    [Fact]
    public async Task RunAsync_InlineScriptExitingZero_ReturnsSuccessWithStdout()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "echo hello-from-script",
            IsInline = true,
            StdinJson = "{}"
        });

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("hello-from-script");
    }

    [Fact]
    public async Task RunAsync_InlineScriptExitingNonZero_ReturnsFailure()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "exit 7",
            IsInline = true,
            StdinJson = "{}"
        });

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_ScriptReadsStdin_ReceivesPayload()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "cat",
            IsInline = true,
            StdinJson = "{\"executionId\":42}"
        });

        result.Success.Should().BeTrue();
        result.Stdout.Should().Contain("\"executionId\":42");
    }

    [Fact]
    public async Task RunAsync_ScriptIgnoresStdin_DoesNotFailOnBrokenPipe()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "echo done",
            IsInline = true,
            StdinJson = new string('x', 1024 * 1024) // large payload the script never reads
        });

        result.Success.Should().BeTrue();
        result.Stdout.Should().Contain("done");
    }

    [Fact]
    public async Task RunAsync_ScriptExceedsTimeout_IsKilledAndReportsTimeout()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "sleep 30",
            IsInline = true,
            StdinJson = "{}",
            TimeoutSeconds = 1
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task RunAsync_NonExistentScriptPath_ReturnsFailureWithoutThrowing()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "/no/such/script.sh",
            IsInline = false,
            StdinJson = "{}"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task RunAsync_EnvAllowlist_PassesOnlyAllowedVariables()
    {
        Environment.SetEnvironmentVariable("REEF_TEST_ALLOWED", "visible");
        Environment.SetEnvironmentVariable("REEF_TEST_BLOCKED", "secret");

        try
        {
            var result = await _runner.RunAsync(new ScriptExecutionRequest
            {
                Interpreter = "sh",
                ScriptPathOrInline = "echo \"A=$REEF_TEST_ALLOWED B=$REEF_TEST_BLOCKED\"",
                IsInline = true,
                StdinJson = "{}",
                EnvAllowlist = new List<string> { "REEF_TEST_ALLOWED" }
            });

            result.Stdout.Should().Contain("A=visible");
            result.Stdout.Should().Contain("B=");
            result.Stdout.Should().NotContain("secret");
        }
        finally
        {
            Environment.SetEnvironmentVariable("REEF_TEST_ALLOWED", null);
            Environment.SetEnvironmentVariable("REEF_TEST_BLOCKED", null);
        }
    }
}
