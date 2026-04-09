using FluentAssertions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Reef.Tests.Security;

public class CorsConfigurationTests
{
    [Fact]
    public void GetCorsPolicy_WhenAllowedOriginsIsEmpty_AllowsAnyOrigin()
    {
        var config = BuildConfiguration(new[] { "AllowedOrigins", "" });
        var policy = CreateCorsPolicy(config);

        policy.Origins.Should().Contain("*");
    }

    [Fact]
    public void GetCorsPolicy_WhenAllowedOriginsHasValues_RestrictsToConfiguredOrigins()
    {
        var config = BuildConfiguration(new[] { "AllowedOrigins:0", "http://localhost:8085", "AllowedOrigins:1", "http://localhost:3000" });
        var policy = CreateCorsPolicy(config);

        policy.Origins.Should().Contain("http://localhost:8085");
        policy.Origins.Should().Contain("http://localhost:3000");
        policy.Origins.Should().NotContain("*");
    }

    [Fact]
    public void GetCorsPolicy_WhenAllowedOriginsIsNull_AllowsAnyOrigin()
    {
        var config = BuildConfiguration(Array.Empty<string>());
        var policy = CreateCorsPolicy(config);

        policy.Origins.Should().Contain("*");
    }

    [Fact]
    public void GetCorsPolicy_WhenAllowedOriginsIsCommaSeparatedString_RestrictsToConfiguredOrigins()
    {
        var config = BuildConfiguration(new[] { "AllowedOrigins", "http://localhost:8085,http://localhost:3000" });
        var policy = CreateCorsPolicy(config);

        policy.Origins.Should().Contain("http://localhost:8085");
        policy.Origins.Should().Contain("http://localhost:3000");
        policy.Origins.Should().NotContain("*");
    }

    [Theory]
    [InlineData("http://localhost:8085")]
    [InlineData("https://reef.mysite.com")]
    [InlineData("http://192.168.1.100:8085")]
    public void GetCorsPolicy_AcceptsVariousOriginFormats(string origin)
    {
        var config = BuildConfiguration(new[] { "AllowedOrigins:0", origin });
        var policy = CreateCorsPolicy(config);

        policy.Origins.Should().Contain(origin);
        policy.Origins.Should().NotContain("*");
    }

    private static IConfiguration BuildConfiguration(string[] keys)
    {
        var memory = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            { "Reef:ListenPort", "8085" },
            { "Reef:AllowedHosts", "localhost" }
        };

        for (int i = 0; i < keys.Length; i++)
        {
            memory[$"Reef:{keys[i]}"] = keys[++i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(memory)
            .Build();
    }

    private static CorsPolicy CreateCorsPolicy(IConfiguration config)
    {
        var allowedOrigins = config.GetSection("Reef:AllowedOrigins").Get<List<string>>();
        var corsOriginsString = config.GetValue<string>("Reef:AllowedOrigins")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
        var allOrigins = (allowedOrigins ?? new List<string>()).Concat(corsOriginsString).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        var policy = new CorsPolicyBuilder();

        if (allOrigins.Count > 0)
        {
            policy.WithOrigins(allOrigins.ToArray());
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy.AllowAnyMethod();
        policy.AllowAnyHeader();

        return policy.Build();
    }
}
