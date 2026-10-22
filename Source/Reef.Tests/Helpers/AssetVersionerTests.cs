using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Reef.Helpers;
using Xunit;

namespace Reef.Tests.Helpers;

public class AssetVersionerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("reef-assets").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Versions_existing_assets_by_content_and_leaves_others_untouched()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets", "js"));
        File.WriteAllText(Path.Combine(_root, "assets", "js", "app.js"), "one");
        var versioner = new AssetVersioner(new PhysicalFileProvider(_root));
        const string html = "<script src=\"/assets/js/app.js\"></script><link href=\"/assets/css/missing.css\"><a href=\"/profiles\">";

        var first = versioner.Apply(html);

        first.Should().MatchRegex("src=\"/assets/js/app\\.js\\?v=[0-9a-f]{10}\"");
        first.Should().Contain("href=\"/assets/css/missing.css\"");
        first.Should().Contain("href=\"/profiles\"");
        new AssetVersioner(new PhysicalFileProvider(_root)).Apply(html).Should().Be(first);
    }
}
