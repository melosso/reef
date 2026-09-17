using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Security;

namespace Reef.Tests.Security;

public class OidcFlowTests
{
    private static OidcSettings Settings() => new()
    {
        IsEnabled = true,
        ClientId = "test-client",
        Scopes = "openid profile email"
    };

    [Fact]
    public void Claim_ReturnsNullForUnknownState()
    {
        OidcFlow.Claim("nonexistent-state").Should().BeNull();
    }

    [Fact]
    public void Claim_IsSingleUse()
    {
        var document = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
        {
            AuthorizationEndpoint = "https://idp.example.com/authorize"
        };

        var start = OidcFlow.Begin(document, Settings(), "https://reef.example.com/api/auth/oidc/callback");

        var first = OidcFlow.Claim(start.State);
        first.Should().NotBeNull();

        var second = OidcFlow.Claim(start.State);
        second.Should().BeNull();
    }

    [Fact]
    public void Begin_GeneratesDistinctStateNoncePkceVerifierPerCall()
    {
        var document = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
        {
            AuthorizationEndpoint = "https://idp.example.com/authorize"
        };

        var first = OidcFlow.Begin(document, Settings(), "https://reef.example.com/api/auth/oidc/callback");
        var second = OidcFlow.Begin(document, Settings(), "https://reef.example.com/api/auth/oidc/callback");

        first.State.Should().NotBe(second.State);
        first.AuthorizeUrl.Should().Contain("client_id=test-client");
        first.AuthorizeUrl.Should().Contain("code_challenge_method=S256");

        OidcFlow.Claim(first.State);
        OidcFlow.Claim(second.State);
    }

    [Fact]
    public void Begin_ThrowsWhenDiscoveryDocumentHasNoAuthorizationEndpoint()
    {
        var document = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();

        var act = () => OidcFlow.Begin(document, Settings(), "https://reef.example.com/api/auth/oidc/callback");

        act.Should().Throw<Microsoft.IdentityModel.Protocols.Configuration.InvalidConfigurationException>();
    }

    [Theory]
    [InlineData("http://localhost", true)]
    [InlineData("http://127.0.0.1:8080", true)]
    [InlineData("http://idp.example.com", false)]
    [InlineData("https://idp.example.com", false)]
    public void AllowsPlainHttp_OnlyAllowsLoopbackAddresses(string authority, bool expected)
    {
        OidcFlow.AllowsPlainHttp(authority).Should().Be(expected);
    }

    [Fact]
    public void Prune_RemovesOnlyExpiredFlows()
    {
        var document = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
        {
            AuthorizationEndpoint = "https://idp.example.com/authorize"
        };

        var start = OidcFlow.Begin(document, Settings(), "https://reef.example.com/api/auth/oidc/callback");

        OidcFlow.Prune(DateTime.UtcNow).Should().Be(0);
        OidcFlow.Prune(DateTime.UtcNow.AddMinutes(11)).Should().Be(1);
        OidcFlow.Claim(start.State).Should().BeNull();
    }
}
