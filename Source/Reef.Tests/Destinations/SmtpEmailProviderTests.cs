using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Reef.Core.Destinations;

namespace Reef.Tests.Destinations;

public class SmtpEmailProviderTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    // Plain SMTP server that advertises AUTH PLAIN and accepts one user, enough for MailKit to connect and authenticate.
    private static (int Port, Task Server) StartServer(string user, string password, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{user}\0{password}"));
        var server = Task.Run(async () =>
        {
            using var _ = listener.Server;
            using var client = await listener.AcceptTcpClientAsync(ct);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 test ready");
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                var command = line.ToUpperInvariant();
                if (command.StartsWith("EHLO")) await writer.WriteLineAsync("250-test\r\n250 AUTH PLAIN");
                else if (command.StartsWith("AUTH PLAIN")) await writer.WriteLineAsync(line[11..] == expected ? "235 ok" : "535 bad credentials");
                else if (command.StartsWith("QUIT")) { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 ok");
            }
        }, ct);
        return (port, server);
    }

    private static EmailDestinationConfiguration Config(int port, string user, string password) => new()
    {
        SmtpHost = "127.0.0.1",
        SmtpPort = port,
        SecurityMode = "None",
        SmtpUsername = user,
        SmtpPassword = password,
        TimeoutSeconds = 5
    };

    [Fact]
    public async Task VerifyAcceptsValidCredentials()
    {
        var (port, server) = StartServer("reef", "secret", _ct);

        var error = await new SmtpEmailProvider().VerifyAsync(Config(port, "reef", "secret"), _ct);

        error.Should().BeNull();
        await server;
    }

    [Fact]
    public async Task VerifyRejectsWrongPassword()
    {
        var (port, _) = StartServer("reef", "secret", _ct);

        var error = await new SmtpEmailProvider().VerifyAsync(Config(port, "reef", "wrong"), _ct);

        error.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifySkipsAuthenticationWhenAuthTypeIsNone()
    {
        var (port, server) = StartServer("reef", "secret", _ct);
        var config = Config(port, "reef", "wrong");
        config.AuthType = "None";

        var error = await new SmtpEmailProvider().VerifyAsync(config, _ct);

        error.Should().BeNull();
        await server;
    }

    [Fact]
    public async Task VerifyReportsUnreachableServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var error = await new SmtpEmailProvider().VerifyAsync(Config(port, "", ""), _ct);

        error.Should().NotBeNull();
    }
}
