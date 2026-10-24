using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Reef.Helpers;
using Xunit;

namespace Reef.Tests.Helpers;

public class EnvAliasesTests
{
    private static readonly Dictionary<string, string?> Defaults = new()
    {
        ["Reef:DatabasePath"] = "Reef.db",
        ["Reef:ListenPort"] = "8085",
        ["Reef:Jwt:SecretKey"] = "default-secret",
        ["Reef:Jobs:CheckIntervalSeconds"] = "10",
        ["Reef:Scheduler:CheckIntervalSeconds"] = "60",
        ["Reef:AllowedOrigins:0"] = "http://localhost:8085",
        ["Reef:AllowedOrigins:1"] = "http://localhost:3000",
    };

    // Mirrors AddEnvironmentVariables followed by ApplyConfigAliases.
    private static IConfiguration Configure(params (string Name, string Value)[] env)
    {
        var vars = env.ToDictionary(e => e.Name, e => e.Value);
        var defaults = new ConfigurationBuilder().AddInMemoryCollection(Defaults).Build();
        return new ConfigurationBuilder()
            .AddInMemoryCollection(Defaults)
            .AddInMemoryCollection(vars.Where(v => v.Key.Contains("__")).ToDictionary(v => v.Key.Replace("__", ":"), v => (string?)v.Value))
            .AddInMemoryCollection(EnvAliases.Resolve(defaults, vars))
            .Build();
    }

    [Theory]
    [InlineData("DATABASE_PATH")]
    [InlineData("DatabasePath")]
    [InlineData("DATABASEPATH")]
    [InlineData("REEF_DATABASE_PATH")]
    [InlineData("Reef__DatabasePath")]
    public void EveryNameFormSetsTheSetting(string name) =>
        Configure((name, "/data/Reef.db"))["Reef:DatabasePath"].Should().Be("/data/Reef.db");

    [Fact]
    public void PrefixedShortNameWinsOverBareAndBareWinsOverLegacy() =>
        Configure(("Reef__DatabasePath", "legacy"), ("DATABASE_PATH", "bare"), ("REEF_DATABASE_PATH", "prefixed"))["Reef:DatabasePath"].Should().Be("prefixed");

    [Fact]
    public void ShortNameWinsOverLegacy() =>
        Configure(("Reef__DatabasePath", "legacy"), ("DATABASE_PATH", "bare"))["Reef:DatabasePath"].Should().Be("bare");

    [Fact]
    public void NestedSettingsTakeTheirSectionAsPrefix()
    {
        var config = Configure(("JWT_SECRET_KEY", "s3cret"), ("JOBS_CHECK_INTERVAL_SECONDS", "5"), ("CHECK_INTERVAL_SECONDS", "1"));

        config["Reef:Jwt:SecretKey"].Should().Be("s3cret");
        config["Reef:Jobs:CheckIntervalSeconds"].Should().Be("5");
        config["Reef:Scheduler:CheckIntervalSeconds"].Should().Be("60");
    }

    [Theory]
    [InlineData("ALLOWED_ORIGINS")]
    [InlineData("Reef__AllowedOrigins")]
    public void ListsSplitOnCommasAndReplaceTheDefaults(string name) =>
        Configure((name, "https://a.example, https://b.example;https://c.example"))
            .GetList("Reef:AllowedOrigins")
            .Should().Equal("https://a.example", "https://b.example", "https://c.example");

    [Fact]
    public void ShorterListDropsTheRemainingDefaults() =>
        Configure(("ALLOWED_ORIGINS", "https://a.example")).GetList("Reef:AllowedOrigins")
            .Should().Equal("https://a.example");

    [Fact]
    public void UnrelatedVariablesAreIgnored() =>
        EnvAliases.Resolve(new ConfigurationBuilder().AddInMemoryCollection(Defaults).Build(),
            new Dictionary<string, string> { ["PATH"] = "/usr/bin", ["HOME"] = "/root", ["ASPNETCORE_URLS"] = "http://+:80" })
            .Should().BeEmpty();

    [Theory]
    [InlineData("Reef__DatabasePath", "DATABASE_PATH")]
    [InlineData("Reef__Jwt__SecretKey", "JWT_SECRET_KEY")]
    [InlineData("Reef__AllowedOrigins__0", "ALLOWED_ORIGINS")]
    [InlineData("REEF__LISTENPORT", "LISTENPORT")]
    public void LegacyNamesSuggestTheirShortName(string legacy, string expected) =>
        EnvAliases.ShortNameFor(legacy).Should().Be(expected);

    [Fact]
    public void DirectVariablesPreferThePrefixedName()
    {
        EnvAliases.GetDirect("ENCRYPTION_KEY", n => n switch { "REEF_ENCRYPTION_KEY" => "prefixed", "ENCRYPTION_KEY" => "bare", _ => null }).Should().Be("prefixed");
        EnvAliases.GetDirect("ENCRYPTION_KEY", n => n == "ENCRYPTION_KEY" ? "bare" : null).Should().Be("bare");
        EnvAliases.GetDirect("ENCRYPTION_KEY", _ => null).Should().BeNull();
    }

    // The Dockerfile declares REEF_ENCRYPTION_KEY with an empty value.
    [Fact]
    public void EmptyValuesCountAsUnset()
    {
        EnvAliases.GetDirect("ENCRYPTION_KEY", n => n == "REEF_ENCRYPTION_KEY" ? "" : "bare").Should().Be("bare");
        Configure(("REEF_DATABASE_PATH", ""), ("DATABASE_PATH", "/data/Reef.db"))["Reef:DatabasePath"].Should().Be("/data/Reef.db");
    }

    [Fact]
    public void EmptyListDefaultsStillTakeAShortName()
    {
        var json = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream("""{ "Reef": { "KnownProxies": [], "Jwt": { "Issuer": "reef" } } }"""u8.ToArray()))
            .Build();

        var overrides = EnvAliases.Resolve(json, new Dictionary<string, string> { ["KNOWN_PROXIES"] = "10.0.0.1,10.0.0.2", ["JWT"] = "x" });

        overrides.Keys.Should().Equal("Reef:KnownProxies");
        new ConfigurationBuilder().AddConfiguration(json).AddInMemoryCollection(overrides).Build()
            .GetList("Reef:KnownProxies").Should().Equal("10.0.0.1", "10.0.0.2");
    }
}
