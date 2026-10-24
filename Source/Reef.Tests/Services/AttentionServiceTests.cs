using FluentAssertions;
using Reef.Core.Models.Dashboard;
using Reef.Core.Services;
using Xunit;

namespace Reef.Tests.Services;

public class AttentionServiceTests
{
    private static AttentionRun Run(long rows, long ms, bool delta = false) => new() { Rows = rows, DurationMs = ms, DeltaSyncEnabled = delta };

    private static readonly AttentionRun[] Usual = [Run(100, 2000), Run(110, 2100), Run(90, 1900)];

    [Fact]
    public void FlagsRunsFarFromTheMedianAndIgnoresSmallOrExpectedChanges()
    {
        AttentionService.FindAnomaly(Run(5, 2000), Usual)!.Reason.Should().Be("rows");
        AttentionService.FindAnomaly(Run(100, 5000), Usual)!.Reason.Should().Be("duration");
        AttentionService.FindAnomaly(Run(0, 2000, delta: true), Usual).Should().BeNull();
        AttentionService.FindAnomaly(Run(100, 30), [Run(100, 10), Run(100, 10), Run(100, 10)]).Should().BeNull();
        AttentionService.FindAnomaly(Run(0, 9000), Usual[..2]).Should().BeNull();
    }
}
