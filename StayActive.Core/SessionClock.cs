using System.Diagnostics;

namespace StayActive.Core;

public sealed class SessionClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private bool _isPaused;

    public SessionClock(DateTimeOffset? startedAtUtc = null)
    {
        StartedAtUtc = startedAtUtc ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset StartedAtUtc { get; }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public DateTimeOffset UtcNow => StartedAtUtc + _stopwatch.Elapsed;

    public bool IsPaused => _isPaused;

    public void Pause()
    {
        if (_isPaused)
        {
            return;
        }

        _stopwatch.Stop();
        _isPaused = true;
    }

    public void Resume()
    {
        if (!_isPaused)
        {
            return;
        }

        _stopwatch.Start();
        _isPaused = false;
    }
}