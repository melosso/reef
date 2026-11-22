using FluentAssertions;
using Reef.Helpers;
using Xunit;

namespace Reef.Tests.Helpers;

public class TempPathsTests
{
    private static string Temp(params string[] parts) => Path.Combine([Path.GetTempPath(), .. parts]);

    [Theory]
    [InlineData(new[] { "reef-workflows", "12", "file", "orders", "out.csv" }, "orders/out.csv")]
    [InlineData(new[] { "reef_splits", "north", "out.csv" }, "north/out.csv")]
    [InlineData(new[] { "1", "out.csv" }, "out.csv")]
    [InlineData(new[] { "exports", "out.csv" }, "exports/out.csv")]
    public void RelativeDropsTheStagingFolders(string[] parts, string expected) =>
        TempPaths.Relative(Temp(parts)).Should().Be(expected.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void RelativeKeepsOnlyTheNameOutsideTheTempFolder() =>
        TempPaths.Relative(Path.Combine(Path.GetFullPath("/srv"), "data", "out.csv")).Should().Be("out.csv");
}
