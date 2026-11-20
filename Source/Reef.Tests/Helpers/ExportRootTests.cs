using FluentAssertions;
using Reef.Helpers;
using Xunit;

namespace Reef.Tests.Helpers;

public class ExportRootTests
{
    private static readonly string BaseDir = Path.Combine(Path.GetTempPath(), "reef-app", "bin");

    [Fact]
    public void ResolveDefaultsToExportsNextToTheExecutable() =>
        ExportRoot.Resolve(null, "Production", BaseDir).Should().Be(Path.Combine(BaseDir, "exports"));

    [Fact]
    public void ResolveReadsRelativePathsFromTheExecutableFolder() =>
        ExportRoot.Resolve("data/out", "Production", BaseDir).Should().Be(Path.GetFullPath(Path.Combine(BaseDir, "data/out")));

    [Fact]
    public void ResolveKeepsRootedPaths() =>
        ExportRoot.Resolve(Path.Combine(Path.GetTempPath(), "shared"), "Production", BaseDir).Should().Be(Path.Combine(Path.GetTempPath(), "shared"));

    [Fact]
    public void ResolveUsesTheProjectFolderInDevelopment() =>
        ExportRoot.Resolve("exports", "Development", Path.Combine(BaseDir, "Debug", "net11.0"))
            .Should().Be(Path.GetFullPath(Path.Combine(BaseDir, "Debug", "net11.0", "..", "..", "..", "exports")));
}
