using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Security;
using Xunit;

namespace Reef.Tests.Security;

public class TrustedProxySettingsValidatorTests
{
    private static string[] Failures(string[] proxies, string[] networks) =>
        new TrustedProxySettingsValidator().Validate(null, new TrustedProxySettings { KnownProxies = proxies, KnownNetworks = networks }).Failures?.ToArray() ?? [];

    [Fact]
    public void ValidAddressesAndNetworksPass() =>
        Failures(["10.0.0.1", "::1"], ["172.16.0.0/12", "fd00::/8"]).Should().BeEmpty();

    [Fact]
    public void NothingConfiguredPasses() =>
        Failures([], []).Should().BeEmpty();

    [Fact]
    public void MalformedEntriesNameTheVariableAndValue()
    {
        var failures = Failures(["10.0.0.300"], ["172.16.0.0"]);

        failures.Should().HaveCount(2);
        failures[0].Should().Contain("KNOWN_PROXIES").And.Contain("10.0.0.300");
        failures[1].Should().Contain("KNOWN_NETWORKS").And.Contain("172.16.0.0");
    }
}
