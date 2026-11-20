using System.Diagnostics;
using System.Net.Http.Json;
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
    public async Task RejectedRecipientDoesNotStopOtherDocuments()
    {
        var recipients = 0;
        await using var smtp = new FakeSmtpServer(line => line.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref recipients) == 2 ? "550 5.1.1 mailbox unavailable" : null);
        var admin = await factory.CreateAdminClientAsync();
        var mail = new System.Text.Json.Nodes.JsonObject { ["UseHardcodedRecipients"] = true, ["EmailRecipientsHardcoded"] = "ops@example.com" };
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1" }), EmailProfiles.Rows(5), mail,
            "{{ for row in rows }}<!doctype html><html><body>{{ row.Id }}</body></html>{{ end }}");

        await EmailProfiles.RunAsync(admin, profileId);

        smtp.Messages.Should().HaveCount(4);
    }

    [Fact]
    public async Task RateLimitedServerReceivesEveryMailOnce()
    {
        var senders = 0;
        await using var smtp = new FakeSmtpServer(line => line.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref senders) <= 2 ? "421 4.7.0 rate limited, try later" : null);
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1", RetryDelaySeconds = 0 }), EmailProfiles.Rows(3));

        var execution = await EmailProfiles.RunAsync(admin, profileId);

        execution["status"]!.GetValue<string>().Should().Be("Success");
        smtp.Recipients.Should().BeEquivalentTo("user1@example.com", "user2@example.com", "user3@example.com");
    }

    [Fact]
    public async Task RunSendsEveryRowOverOneConnection()
    {
        await using var smtp = new FakeSmtpServer();
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1" }), EmailProfiles.Rows(50));

        await EmailProfiles.RunAsync(admin, profileId);

        smtp.Messages.Should().HaveCount(50);
        smtp.Connections.Should().Be(1);
    }

    [Fact]
    public async Task ApprovedQueueIsSentWithinOnePollCycle()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var smtp = new FakeSmtpServer();
        var admin = await factory.CreateAdminClientAsync();
        var mail = new System.Text.Json.Nodes.JsonObject { ["EmailApprovalRequired"] = true, ["EmailApprovalRoles"] = "[\"Admin\"]" };
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1" }), EmailProfiles.Rows(25), mail);
        await EmailProfiles.RunAsync(admin, profileId);
        var pending = await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"/api/email-approvals/profile/{profileId}", ct);
        var guids = pending!["data"]!.AsArray().Select(a => a!["guid"]!.GetValue<string>()).ToList();
        guids.Should().HaveCount(25);

        foreach (var guid in guids)
            (await admin.PostAsJsonAsync($"/api/email-approvals/{guid}/approve", new { notes = "ok" }, ct)).EnsureSuccessStatusCode();

        var clock = Stopwatch.StartNew();
        while (smtp.Messages.Count < 25 && clock.Elapsed < TimeSpan.FromSeconds(45))
            await Task.Delay(250, ct);
        smtp.Messages.Should().HaveCount(25);
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
