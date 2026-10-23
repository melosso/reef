using FluentAssertions;
using Reef.Core.Scripting;
using Reef.Core.Services;

namespace Reef.Tests.Scripting;

public class InterpreterServiceTests
{
    private readonly InterpreterService _service = new(new ProcessScriptRunner());

    [Fact]
    public async Task CheckOneAsyncRunnableInterpreterReportsAvailableWithOutput()
    {
        var status = await _service.CheckOneAsync("sh", TestContext.Current.CancellationToken);

        status.Available.Should().BeTrue();
        status.Output.Should().Be("ok");
        status.Error.Should().BeNull();
    }

    [Fact]
    public async Task CheckOneAsyncUnknownInterpreterReportsUnavailableWithError()
    {
        var status = await _service.CheckOneAsync("not-a-real-interpreter", TestContext.Current.CancellationToken);

        status.Available.Should().BeFalse();
        status.Error.Should().Contain("Unknown interpreter");
    }

    [Fact]
    public async Task CheckAllAsyncReturnsOneStatusPerKnownInterpreter()
    {
        var statuses = await _service.CheckAllAsync(TestContext.Current.CancellationToken);

        statuses.Should().HaveCount(InterpreterService.KnownInterpreters.Count);
        statuses.Select(s => s.Name).Should().BeEquivalentTo(
            InterpreterService.KnownInterpreters.Select(d => d.Name));
    }

    [Fact]
    public async Task CheckAllAsyncOnThisHostFindsAtLeastShAndBashAvailable()
    {
        // sh/bash are present on every Linux/macOS CI runner and dev box this
        // project targets - a sane baseline so a regression in the probe
        // logic itself doesn't silently report everything as unavailable.
        var statuses = await _service.CheckAllAsync(TestContext.Current.CancellationToken);

        statuses.First(s => s.Name == "sh").Available.Should().BeTrue();
        statuses.First(s => s.Name == "bash").Available.Should().BeTrue();
    }
}
