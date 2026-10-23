using FluentAssertions;
using Reef.Helpers;
using Xunit;

namespace Reef.Tests.Helpers;

public class DownloadTokenTests
{
    [Theory]
    [InlineData("exports/2026-10-23/a1b2c3d4/orders.csv", "a1b2c3d4", true)]
    [InlineData("exports/2026-10-23/a1b2c3d4/orders.csv", "A1B2C3D4", false)]
    [InlineData("exports/2026-10-23/a1b2c3d4/orders.csv", "a1b2", false)]
    [InlineData("exports/2026-10-23/a1b2c3d4/orders.csv", "-", false)]
    [InlineData("exports/2026-10-23/a1b2c3d4/orders.csv", "orders.csv", false)]
    [InlineData("C:/data/out.csv", "out", false)]
    public void TokenMatchesOnlyItsOwnPathSegment(string outputPath, string token, bool expected) =>
        DownloadToken.Matches(outputPath, token).Should().Be(expected);
}
