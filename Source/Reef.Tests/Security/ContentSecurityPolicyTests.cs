using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Reef.Core.Security;
using Xunit;

namespace Reef.Tests.Security;

public class ContentSecurityPolicyTests
{
    [Fact]
    public void InlineScriptsOfTheShellAreAllowedByHash()
    {
        const string script = "document.documentElement.dataset.theme = 'dark';";
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));

        var policy = ContentSecurityPolicy.Build($"<head><script>{script}</script><script type=\"module\" src=\"/app/a.js\"></script></head>");

        policy.Should().Contain($"script-src 'self' 'sha256-{hash}'");
        policy.Should().Contain("object-src 'none'").And.Contain("frame-ancestors 'none'").And.NotContain("unsafe-eval");
    }

    [Fact]
    public void MissingShellAllowsOnlySameOriginScripts() =>
        ContentSecurityPolicy.Build(null).Should().Contain("script-src 'self';");
}
