using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Reef.Core.Destinations;

// Shared client for email http providers
public static class EmailApiClient
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    // Retries 408, 429, 5xx and timeouts, honors Retry-After
    public static async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> request, int timeoutSeconds, CancellationToken ct = default)
    {
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true, Delay = TimeSpan.FromSeconds(1) })
            .AddTimeout(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)))
            .Build();
        return await pipeline.ExecuteAsync(async token => await Http.SendAsync(request(), token), ct);
    }
}
