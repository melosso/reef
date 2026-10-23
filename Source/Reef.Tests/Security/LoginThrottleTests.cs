using FluentAssertions;
using Reef.Core.Security;
using Xunit;

namespace Reef.Tests.Security;

public class LoginThrottleTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly Clock _clock = new();
    private LoginThrottle Throttle() => new(_clock);

    [Fact]
    public void Five_failures_lock_the_account_on_that_address_only()
    {
        var throttle = Throttle();
        for (var i = 0; i < LoginThrottle.MaxAccountFailures; i++) throttle.RecordFailure("10.0.0.1", "alice");

        throttle.IsLocked("10.0.0.1", "alice").Should().BeTrue();
        throttle.IsLocked("10.0.0.1", "bob").Should().BeFalse();
        throttle.IsLocked("10.0.0.2", "alice").Should().BeFalse();
    }

    [Fact]
    public void Usernames_match_without_case()
    {
        var throttle = Throttle();
        for (var i = 0; i < LoginThrottle.MaxAccountFailures; i++) throttle.RecordFailure("10.0.0.1", i % 2 == 0 ? "Alice" : "alice");

        throttle.IsLocked("10.0.0.1", "ALICE").Should().BeTrue();
    }

    [Fact]
    public void Spraying_many_accounts_locks_the_address()
    {
        var throttle = Throttle();
        for (var i = 0; i < LoginThrottle.MaxAddressFailures; i++) throttle.RecordFailure("10.0.0.1", $"user{i}");

        throttle.IsLocked("10.0.0.1", "someone-else").Should().BeTrue();
        throttle.IsLocked("10.0.0.2", "someone-else").Should().BeFalse();
    }

    [Fact]
    public void Failures_expire_after_the_window()
    {
        var throttle = Throttle();
        for (var i = 0; i < LoginThrottle.MaxAccountFailures - 1; i++) throttle.RecordFailure("10.0.0.1", "alice");

        _clock.Now += LoginThrottle.Window + TimeSpan.FromSeconds(1);
        throttle.RecordFailure("10.0.0.1", "alice");

        throttle.IsLocked("10.0.0.1", "alice").Should().BeFalse();
    }

    [Fact]
    public void Lockout_ends_after_the_lockout_period()
    {
        var throttle = Throttle();
        for (var i = 0; i < LoginThrottle.MaxAccountFailures; i++) throttle.RecordFailure("10.0.0.1", "alice");

        _clock.Now += LoginThrottle.Lockout + TimeSpan.FromSeconds(1);

        throttle.IsLocked("10.0.0.1", "alice").Should().BeFalse();
    }

    [Fact]
    public void Success_clears_the_account_but_not_the_address()
    {
        var throttle = Throttle();
        for (var i = 0; i < LoginThrottle.MaxAccountFailures - 1; i++) throttle.RecordFailure("10.0.0.1", "alice");
        for (var i = 0; i < LoginThrottle.MaxAddressFailures - LoginThrottle.MaxAccountFailures - 1; i++) throttle.RecordFailure("10.0.0.1", $"user{i}");

        throttle.RecordSuccess("10.0.0.1", "alice");
        throttle.RecordFailure("10.0.0.1", "alice");
        throttle.IsLocked("10.0.0.1", "alice").Should().BeFalse();

        throttle.RecordFailure("10.0.0.1", "carol");
        throttle.IsLocked("10.0.0.1", "carol").Should().BeTrue();
    }
}
