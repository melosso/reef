using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Reef.Tests.Destinations;

// Plain SMTP server for delivery tests, the reply hook returns a custom reply for a command or null for the default.
public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accept;
    private readonly Func<string, string?> _reply;
    private int _connections;

    public FakeSmtpServer(Func<string, string?>? reply = null)
    {
        _reply = reply ?? (_ => null);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
    }

    public int Port { get; }
    public int Connections => _connections;
    public ConcurrentQueue<string> Recipients { get; } = new();
    public ConcurrentQueue<string> Messages { get; } = new();

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                _ = ServeAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        string? recipient = null;
        try
        {
            await writer.WriteLineAsync("220 fake ready");
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                var command = line.ToUpperInvariant();
                if (_reply(line) is { } custom)
                {
                    await writer.WriteLineAsync(custom);
                    if (custom.StartsWith("421")) break;
                    continue;
                }
                if (command.StartsWith("EHLO")) await writer.WriteLineAsync("250-fake\r\n250 8BITMIME");
                else if (command.StartsWith("RCPT TO:")) { recipient = line[8..].Trim(' ', '<', '>'); await writer.WriteLineAsync("250 ok"); }
                else if (command == "DATA")
                {
                    await writer.WriteLineAsync("354 go");
                    var body = new StringBuilder();
                    while (await reader.ReadLineAsync(_stop.Token) is { } data && data != ".") body.AppendLine(data);
                    Messages.Enqueue(body.ToString());
                    if (recipient is not null) Recipients.Enqueue(recipient);
                    await writer.WriteLineAsync("250 queued");
                }
                else if (command.StartsWith("QUIT")) { await writer.WriteLineAsync("221 bye"); break; }
                else await writer.WriteLineAsync("250 ok");
            }
        }
        catch (IOException) { }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accept;
        _stop.Dispose();
    }
}
