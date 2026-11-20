using MailKit;
using MailKit.Net.Smtp;
using MimeKit;

namespace Reef.Core.Destinations;

// shortcut dropped reply retry can duplicate
public sealed class SmtpSender(EmailDestinationConfiguration config) : IAsyncDisposable
{
    private const int MessagesPerConnection = 100;
    private SmtpClient? _client;
    private int _sent;

    public async Task SendAsync(MimeMessage message, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (_client is not { IsConnected: true } || _sent >= MessagesPerConnection)
                    await ReconnectAsync(ct);
                await _client!.SendAsync(message, ct);
                _sent++;
                return;
            }
            catch (Exception ex) when (attempt <= config.RetryCount && IsTemporary(ex))
            {
                await CloseAsync();
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, config.RetryDelaySeconds) * Math.Pow(2, attempt - 1)), ct);
            }
        }
    }

    public static bool IsTemporary(Exception ex) => ex switch
    {
        SmtpCommandException command => (int)command.StatusCode is >= 400 and < 500,
        SmtpProtocolException or ServiceNotConnectedException or IOException => true,
        _ => false
    };

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await CloseAsync();
        _client = await SmtpEmailProvider.ConnectAsync(config, ct);
        _sent = 0;
    }

    private async Task CloseAsync()
    {
        if (_client is null) return;
        try
        {
            if (_client.IsConnected) await _client.DisconnectAsync(true);
        }
        catch (Exception ex) when (ex is IOException or SmtpProtocolException or ServiceNotConnectedException)
        {
            // Server already dropped the connection
        }
        _client.Dispose();
        _client = null;
    }

    public ValueTask DisposeAsync() => new(CloseAsync());
}
