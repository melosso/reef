namespace Reef.Core.Models;

public readonly record struct LoginFailures(int Count, DateTimeOffset WindowStart, DateTimeOffset? LockedUntil);
