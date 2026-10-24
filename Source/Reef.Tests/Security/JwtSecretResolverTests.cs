using FluentAssertions;
using Reef.Core.Security;
using Xunit;

namespace Reef.Tests.Security;

public class JwtSecretResolverTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("reef-jwt").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(JwtSecretResolver.ShippedDefault)]
    public void MissingOrShippedSecretsAreReplacedByAStoredRandomSecret(string? configured)
    {
        var first = JwtSecretResolver.Resolve(configured, _folder);
        var second = JwtSecretResolver.Resolve(configured, _folder);

        first.Should().NotBe(JwtSecretResolver.ShippedDefault).And.HaveLength(86);
        second.Should().Be(first);
    }

    [Fact]
    public void AConfiguredSecretIsKept() =>
        JwtSecretResolver.Resolve("my-own-secret-that-is-long-enough-for-hmac-sha256", _folder)
            .Should().Be("my-own-secret-that-is-long-enough-for-hmac-sha256");
}
