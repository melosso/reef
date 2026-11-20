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
    public async Task OversizedAttachmentFailsOnlyItsOwnEmail()
    {
        await using var smtp = new FakeSmtpServer();
        var admin = await factory.CreateAdminClientAsync();
        const string query = "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 3) " +
            "SELECT i AS Id, 'user' || i || '@example.com' AS Email, randomblob(CASE WHEN i = 2 THEN 26214400 ELSE 16 END) AS Doc, 'file-' || i || '.bin' AS Fname FROM n";
        var mail = new System.Text.Json.Nodes.JsonObject { ["EmailAttachmentConfig"] = """{"Enabled":true,"Mode":"Binary","Binary":{"ContentColumnName":"Doc","FilenameColumnName":"Fname"}}""" };
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1" }), query, mail);

        var execution = await EmailProfiles.RunAsync(admin, profileId);

        smtp.Recipients.Should().BeEquivalentTo("user1@example.com", "user3@example.com");
        execution["status"]!.GetValue<string>().Should().Be("Partial");
    }

    private static System.Text.Json.Nodes.JsonObject DirectPath(string folder) => new()
    {
        ["EmailAttachmentConfig"] = $$$"""{"Enabled":true,"Mode":"DirectPath","DirectPath":{"BaseDirectory":"{{{folder}}}","FilenameTemplate":"invoice-{Id}.txt"}}"""
    };

    [Fact]
    public async Task DirectPathAttachesFilesFromTheExportsFolder()
    {
        var folder = $"attach-{Guid.NewGuid():N}";
        Directory.CreateDirectory(Path.Combine(Reef.Helpers.ExportRoot.Path, folder));
        await File.WriteAllTextAsync(Path.Combine(Reef.Helpers.ExportRoot.Path, folder, "invoice-1.txt"), "invoice one", TestContext.Current.CancellationToken);
        await using var smtp = new FakeSmtpServer();
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1" }), EmailProfiles.Rows(2), DirectPath(folder));

        await EmailProfiles.RunAsync(admin, profileId);

        smtp.Messages.Should().HaveCount(2);
        smtp.Messages.Should().ContainSingle(m => m.Contains("invoice-1.txt"));
    }

    [Fact]
    public async Task DirectPathOutsideTheExportsFolderFailsTheRun()
    {
        await using var smtp = new FakeSmtpServer();
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, new { SmtpServer = "127.0.0.1" }), EmailProfiles.Rows(1), DirectPath("../.."));

        var execution = await EmailProfiles.RunAsync(admin, profileId);

        execution["status"]!.GetValue<string>().Should().Be("Failed");
        smtp.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task OAuthClientCredentialsFetchAccessTokenForSmtpLogin()
    {
        using var tokens = new Reef.Tests.Templates.MockHttpServer();
        tokens.SetResponse(200, """{"access_token":"fresh-token","token_type":"Bearer","expires_in":3600}""");
        var logins = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using var smtp = new FakeSmtpServer(line =>
        {
            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) return "250-fake\r\n250 AUTH XOAUTH2";
            if (!line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase)) return null;
            logins.Enqueue(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(line.Split(' ')[^1])));
            return "235 2.7.0 accepted";
        });
        var admin = await factory.CreateAdminClientAsync();
        var oauth = new
        {
            SmtpServer = "127.0.0.1", AuthType = "OAuth2", SmtpAuthType = "OAuth2", OauthUsername = "reef@example.com",
            OauthTokenEndpoint = tokens.BaseUrl + "token", OauthClientId = "reef-app", OauthClientSecret = "app-secret", OauthScope = "https://outlook.office365.com/.default"
        };
        var profileId = await EmailProfiles.CreateAsync(admin, factory.DataFolder, Smtp(smtp.Port, oauth), EmailProfiles.Rows(2));

        var execution = await EmailProfiles.RunAsync(admin, profileId);

        execution["status"]!.GetValue<string>().Should().Be("Success");
        logins.Should().ContainSingle().Which.Should().Contain("auth=Bearer fresh-token");
        tokens.LastRequestBody.Should().Contain("grant_type=client_credentials").And.Contain("client_id=reef-app");
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
