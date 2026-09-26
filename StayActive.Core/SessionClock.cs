using System.Diagnostics;

namespace StayActive.Core;

public sealed class SessionClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public SessionClock(DateTimeOffset? startedAtUtc = null)
    {
        StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset StartedAtUtc { get; }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public DateTimeOffset UtcNow => StartedAtUtc + _stopwatch.Elapsed;
}