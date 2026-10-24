using System.Text.Json;
using FluentAssertions;
using Reef.Core.Destinations;

namespace Reef.Tests.Destinations;

public class LocalDestinationTests
{
    [Fact]
    public async Task DestinationInTheTempFolderKeepsTheExportAndExplainsWhy()
    {
        var export = Path.Combine(Path.GetTempPath(), $"reef-local-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(export, "[]", TestContext.Current.CancellationToken);
        try
        {
            var (success, _, error) = await new LocalDestination().SaveAsync(export, JsonSerializer.Serialize(new { path = Path.GetTempPath() }));

            success.Should().BeFalse();
            error.Should().Contain("temporary folder");
            File.Exists(export).Should().BeTrue();
        }
        finally
        {
            File.Delete(export);
        }
    }
}
