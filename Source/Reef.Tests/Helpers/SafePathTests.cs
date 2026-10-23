using FluentAssertions;
using Reef.Helpers;
using Xunit;

namespace Reef.Tests.Helpers;

public class SafePathTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "reef-root");

    [Theory]
    [InlineData("out.csv")]
    [InlineData("orders/2026/out.csv")]
    [InlineData("a..b.csv")]
    public void RelativeNamesStayInsideTheRoot(string name) =>
        SafePath.Combine(Root, name).Should().StartWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar);

    [Theory]
    [InlineData("../out.csv")]
    [InlineData("orders/../../out.csv")]
    [InlineData("/etc/out.csv")]
    public void EscapingNamesAreRefused(string name)
    {
        SafePath.IsContainedRelative(name).Should().BeFalse();
        FluentActions.Invoking(() => SafePath.Combine(Root, name)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SiblingFoldersWithTheSamePrefixAreRefused() =>
        FluentActions.Invoking(() => SafePath.Combine(Root, "../reef-root-other/out.csv")).Should().Throw<InvalidOperationException>();
}
