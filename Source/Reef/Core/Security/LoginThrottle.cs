using System.Collections.Concurrent;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Security;

/// <summary>
/// Limits failed sign-ins per address and account, and per address across accounts against password spraying
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    public const int MaxAccountFailures = 5;
    public const int MaxAddressFailures = 20;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, LoginFailures> _failures = new();
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    public bool IsLocked(string address, string username)
    {
        var now = time.GetUtcNow();
        return Locked(AddressKey(address), now) || Locked(AccountKey(address, username), now);
    }

    public void RecordFailure(string address, string username)
    {
        var now = time.GetUtcNow();
        Prune(now);
        Count(AddressKey(address), MaxAddressFailures, now);
        Count(AccountKey(address, username), MaxAccountFailures, now);
    }

    // the address counter stays so a known password cannot reset spraying attempts on other accounts
    public void RecordSuccess(string address, string username) => _failures.TryRemove(AccountKey(address, username), out _);

    private bool Locked(string key, DateTimeOffset now) =>
        _failures.TryGetValue(key, out var entry) && entry.LockedUntil > now;

    private void Count(string key, int limit, DateTimeOffset now)
    {
        var entry = _failures.AddOrUpdate(key,
            _ => new LoginFailures(1, now, null),
            (_, current) => current.WindowStart + Window < now && current.LockedUntil is null
                ? new LoginFailures(1, now, null)
                : current with { Count = current.Count + 1 });

        if (entry.Count < limit || entry.LockedUntil > now) return;
        _failures[key] = entry with { LockedUntil = now + Lockout };
        Log.Warning("Sign-in locked for {Key} after {Count} failed attempts", key, entry.Count);
    }

    private void Prune(DateTimeOffset now)
    {
        if (now - _lastPrune < Window) return;
        _lastPrune = now;
        foreach (var (key, entry) in _failures)
            if (entry.LockedUntil is { } until ? until < now : entry.WindowStart + Window < now)
                _failures.TryRemove(key, out _);
    }

    private static string AddressKey(string address) => address;

    private static string AccountKey(string address, string username) => $"{address}|{username.Trim().ToLowerInvariant()}";
}
