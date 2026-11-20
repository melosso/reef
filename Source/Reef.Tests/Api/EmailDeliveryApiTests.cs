using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Reef.Tests.Destinations;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class EmailDeliveryApiTests(ReefApiFactory factory)
{
    private static object Smtp(int port, object? extra = null)
    {
        var config = new Dictionary<string, object?> { ["EmailProvider"] = "SMTP", ["SmtpHost"] = "127.0.0.1", ["SmtpPort"] = port, ["SecurityMode"] = "None", ["FromAddress"] = "reef@example.com", ["TimeoutSeconds"] = 2 };
        foreach (var property in extra?.GetType().GetProperties() ?? []) config[property.Name] = property.GetValue(extra);
        return config;
    }

    [Fact]
    public async Task DestinationWithSmtpHostDeliversEveryRow()
    {
        await using var smtp = new FakeSmtpServer();
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port), EmailProfiles.Rows(3));

        var execution = await EmailProfiles.RunAsync(admin, profileId);

        execution["status"]!.GetValue<string>().Should().Be("Success");
        smtp.Recipients.Should().BeEquivalentTo("user1@example.com", "user2@example.com", "user3@example.com");
    }

    [Fact]
    public async Task SilentServerFailsWithinTheDestinationTimeout()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(port, new { SmtpServer = "127.0.0.1" }), EmailProfiles.Rows(1));
        var clock = Stopwatch.StartNew();

        var execution = await EmailProfiles.RunAsync(admin, profileId);

        listener.Stop();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        execution["status"]!.GetValue<string>().Should().Be("Failed");
    }

    [Fact]
    public async Task OAuthWithoutTokenFailsBeforeSending()
    {
        await using var smtp = new FakeSmtpServer();
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1", AuthType = "OAuth2", SmtpAuthType = "OAuth2", OauthUsername = "reef@example.com" }), EmailProfiles.Rows(2));

        var execution = await EmailProfiles.RunAsync(admin, profileId);

        execution["status"]!.GetValue<string>().Should().Be("Failed");
        smtp.Messages.Should().BeEmpty();
    }
}
