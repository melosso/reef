using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Token bucket rate limiter for managing API request rate limits.
/// Allows for controlled request throttling with support for burst requests.
/// </summary>
public class RateLimiter
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<RateLimiter>();

    private readonly double _tokensPerSecond;
    private readonly double _maxTokens;
    private double _availableTokens;
    private DateTime _lastRefillTime;
    private readonly object _lock = new object();

    /// <summary>
    /// Creates a new RateLimiter instance
    /// </summary>
    /// <param name="requestsPerSecond">Maximum requests allowed per second</param>
    /// <param name="burstSize">Maximum number of requests that can be burst (optional, defaults to tokensPerSecond)</param>
    public RateLimiter(double requestsPerSecond, double burstSize = 0)
    {
        if (requestsPerSecond <= 0)
            throw new ArgumentException("Requests per second must be positive", nameof(requestsPerSecond));

        _tokensPerSecond = requestsPerSecond;
        _maxTokens = burstSize > 0 ? burstSize : requestsPerSecond;
        _availableTokens = _maxTokens;
        _lastRefillTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Asynchronously acquires a token, waiting if necessary to respect the rate limit
    /// </summary>
    /// <param name="tokensNeeded">Number of tokens to acquire (default 1)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task AcquireAsync(int tokensNeeded = 1, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            lock (_lock)
            {
                RefillTokens();

                if (_availableTokens >= tokensNeeded)
                {
                    _availableTokens -= tokensNeeded;
                    return;
                }
            }

            // Calculate wait time
            var tokensShort = tokensNeeded - _availableTokens;
            var waitTimeMs = (int)((tokensShort / _tokensPerSecond) * 1000);

            Log.Debug("Rate limit reached. Waiting {WaitTimeMs}ms before next request", waitTimeMs);
            await Task.Delay(Math.Max(10, waitTimeMs), cancellationToken);
        }
    }

    /// <summary>
    /// Synchronously tries to acquire a token without waiting
    /// </summary>
    /// <param name="tokensNeeded">Number of tokens to acquire (default 1)</param>
    /// <returns>True if tokens were acquired, false if rate limit would be exceeded</returns>
    public bool TryAcquire(int tokensNeeded = 1)
    {
        lock (_lock)
        {
            RefillTokens();

            if (_availableTokens >= tokensNeeded)
            {
                _availableTokens -= tokensNeeded;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Refills tokens based on elapsed time since last refill
    /// </summary>
    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = (now - _lastRefillTime).TotalSeconds;

        if (elapsedSeconds > 0)
        {
            _availableTokens = Math.Min(_maxTokens, _availableTokens + (elapsedSeconds * _tokensPerSecond));
            _lastRefillTime = now;
        }
    }
}
