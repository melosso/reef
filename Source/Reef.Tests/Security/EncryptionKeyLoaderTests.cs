using FluentAssertions;
using Reef.Core.Security;
using Xunit;

namespace Reef.Tests.Security;

public class EncryptionKeyLoaderTests
{
    [Fact]
    public void FallbackKeyIsRefusedOutsideDevelopment() =>
        FluentActions.Invoking(() => EncryptionKeyLoader.EnsureConfigured(isDevelopment: false, EncryptionKeyLoader.FallbackKey))
            .Should().Throw<InvalidOperationException>().WithMessage("*ENCRYPTION_KEY*");

    [Fact]
    public void FallbackKeyIsAllowedInDevelopment() =>
        FluentActions.Invoking(() => EncryptionKeyLoader.EnsureConfigured(isDevelopment: true, EncryptionKeyLoader.FallbackKey))
            .Should().NotThrow();

    [Fact]
    public void ConfiguredKeyIsAllowedEverywhere() =>
        FluentActions.Invoking(() => EncryptionKeyLoader.EnsureConfigured(isDevelopment: false, "ProductionKeyThatIsLongEnough0123456789"))
            .Should().NotThrow();
}
