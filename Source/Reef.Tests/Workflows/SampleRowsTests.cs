using FluentAssertions;
using Reef.Core.Workflows.Engine;

namespace Reef.Tests.Workflows;

public class SampleRowsTests
{
    [Fact]
    public void SampleSummarizesBinaryAndLongText()
    {
        var rows = new[] { new Dictionary<string, object?> { ["Doc"] = new byte[5_000_000], ["Note"] = new string('x', 2000), ["Id"] = 1 } };

        var sample = SampleRows.Take(rows, 20).Single();

        sample["Doc"].Should().Be("[binary 5000000 bytes]");
        ((string)sample["Note"]!).Length.Should().Be(501);
        sample["Id"].Should().Be(1);
    }
}
