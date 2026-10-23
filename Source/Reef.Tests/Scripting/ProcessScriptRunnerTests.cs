using FluentAssertions;
using Reef.Core.Scripting;

namespace Reef.Tests.Scripting;

public class ProcessScriptRunnerTests
{
    private readonly ProcessScriptRunner _runner = new();

    [Fact]
    public async Task RunAsyncInlineScriptExitingZeroReturnsSuccessWithStdout()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "echo hello-from-script",
            IsInline = true,
            StdinJson = "{}"
        }, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("hello-from-script");
    }

    [Fact]
    public async Task RunAsyncInlineScriptExitingNonZeroReturnsFailure()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "exit 7",
            IsInline = true,
            StdinJson = "{}"
        }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(7);
    }

    [Fact]
    public async Task RunAsyncScriptReadsStdinReceivesPayload()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "cat",
            IsInline = true,
            StdinJson = "{\"executionId\":42}"
        }, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Stdout.Should().Contain("\"executionId\":42");
    }

    [Fact]
    public async Task RunAsyncScriptIgnoresStdinDoesNotFailOnBrokenPipe()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "echo done",
            IsInline = true,
            StdinJson = new string('x', 1024 * 1024) // large payload the script never reads
        }, TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Stdout.Should().Contain("done");
    }

    [Fact]
    public async Task RunAsyncScriptExceedsTimeoutIsKilledAndReportsTimeout()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "sleep 30",
            IsInline = true,
            StdinJson = "{}",
            TimeoutSeconds = 1
        }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("timed out");
    }

    [Fact]
    public async Task RunAsyncNonExistentScriptPathReturnsFailureWithoutThrowing()
    {
        var result = await _runner.RunAsync(new ScriptExecutionRequest
        {
            Interpreter = "sh",
            ScriptPathOrInline = "/no/such/script.sh",
            IsInline = false,
            StdinJson = "{}"
        }, TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task RunAsyncEnvAllowlistPassesOnlyAllowedVariables()
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
            }, TestContext.Current.CancellationToken);

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

    [Theory]
    [InlineData("ENCRYPTION_KEY", true)]
    [InlineData("reef_encryption_key", true)]
    [InlineData("JWT_SECRET_KEY", true)]
    [InlineData("Reef__Jwt__SecretKey", true)]
    [InlineData("MY_API_KEY", false)]
    [InlineData("REEF_TEST_ALLOWED", false)]
    public void ReefSecretsAreNeverPassedToScripts(string name, bool blocked) =>
        ProcessScriptRunner.IsReefSecret(name).Should().Be(blocked);

    [Fact]
    public async Task RunAsyncEnvAllowlistSkipsReefSettings()
    {
        Environment.SetEnvironmentVariable("Reef__Probe__Secret", "hidden");

        try
        {
            var result = await _runner.RunAsync(new ScriptExecutionRequest
            {
                Interpreter = "sh",
                ScriptPathOrInline = "echo \"S=$Reef__Probe__Secret\"",
                IsInline = true,
                StdinJson = "{}",
                EnvAllowlist = new List<string> { "Reef__Probe__Secret" }
            }, TestContext.Current.CancellationToken);

            result.Stdout.Should().NotContain("hidden");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Reef__Probe__Secret", null);
        }
    }
}
