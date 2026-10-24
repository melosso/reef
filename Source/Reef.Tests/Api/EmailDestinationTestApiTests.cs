using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class EmailDestinationTestApiTests(ReefApiFactory factory)
{
    [Fact]
    public async Task UnreachableMailServerFailsTheTest()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var client = await factory.CreateAdminClientAsync();
        var config = JsonSerializer.Serialize(new { EmailProvider = "SMTP", SmtpHost = "127.0.0.1", SmtpPort = port, SecurityMode = "None", FromAddress = "reef@example.com", TimeoutSeconds = 5 });

        var result = await client.PostAsJsonAsync("/api/destinations/test", new { type = "Email", configurationJson = config }, TestContext.Current.CancellationToken);

        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
