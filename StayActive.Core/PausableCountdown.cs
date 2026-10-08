namespace StayActive.Core;

public sealed class PausableCountdown
{
    private DateTimeOffset _deadlineUtc;
    private TimeSpan _remaining;

    public PausableCountdown(TimeSpan duration, DateTimeOffset startedAtUtc)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _remaining = duration;
        _deadlineUtc = startedAtUtc + duration;
    }

    public bool IsPaused { get; private set; }

    public TimeSpan GetRemaining(DateTimeOffset nowUtc)
    {
        if (IsPaused)
        {
            return _remaining;
        }

        var remaining = _deadlineUtc - nowUtc;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void Pause(DateTimeOffset nowUtc)
    {
        if (IsPaused)
        {
            return;
        }

        _remaining = GetRemaining(nowUtc);
        IsPaused = true;
    }

    public void Resume(DateTimeOffset nowUtc)
    {
        if (!IsPaused)
        {
            return;
        }

        _deadlineUtc = nowUtc + _remaining;
        IsPaused = false;
    }
}
