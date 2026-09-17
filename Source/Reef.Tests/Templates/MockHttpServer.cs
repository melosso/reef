using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Reef.Tests.Templates;

/// <summary>
/// Minimal loopback HTTP server for exercising the example Script Templates
/// against a fake webhook/Slack/ERP endpoint instead of the real internet.
/// </summary>
public sealed class MockHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    private int _statusCode = 200;
    private string _responseBody = "ok";

    public string BaseUrl { get; }
    public string? LastRequestBody { get; private set; }
    public string? LastMethod { get; private set; }
    public string? LastAuthorizationHeader { get; private set; }
    public string? LastContentTypeHeader { get; private set; }
    public int RequestCount { get; private set; }

    public MockHttpServer()
    {
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public void SetResponse(int statusCode, string body = "ok")
    {
        _statusCode = statusCode;
        _responseBody = body;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            try
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                LastRequestBody = await reader.ReadToEndAsync();
                LastMethod = ctx.Request.HttpMethod;
                LastAuthorizationHeader = ctx.Request.Headers["Authorization"];
                LastContentTypeHeader = ctx.Request.Headers["Content-Type"];
                RequestCount++;

                ctx.Response.StatusCode = _statusCode;
                var bytes = Encoding.UTF8.GetBytes(_responseBody);
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.OutputStream.Close();
            }
            catch
            {
                // Best effort - a flaky/aborted request shouldn't kill the accept loop.
            }
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }
        _cts.Dispose();
    }
}
