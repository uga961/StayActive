namespace StayActive.Core;

public enum ReminderKind
{
    EyeBreak,
    Water,
    Walking
}

public sealed record ReminderOptions(TimeSpan Interval, bool IsEnabled = true, DateTimeOffset? NextDueUtc = null);

public sealed record ReminderSnapshot(ReminderKind Kind, TimeSpan Interval, bool IsEnabled, DateTimeOffset NextDueUtc, bool IsPending);

public sealed class TimerEngine
{
    private readonly Dictionary<ReminderKind, ReminderState> _states;
    private readonly PauseReasonSet _globalPauseReasons = new();

    public TimerEngine(DateTimeOffset nowUtc, IReadOnlyDictionary<ReminderKind, ReminderOptions>? options = null)
    {
        options ??= new Dictionary<ReminderKind, ReminderOptions>();
        _states = Enum.GetValues<ReminderKind>().ToDictionary(
            kind => kind,
            kind =>
            {
                var defaults = GetDefaultInterval(kind);
                var setting = options.GetValueOrDefault(kind);
                var interval = setting?.Interval > TimeSpan.Zero ? setting.Interval : defaults;
                return new ReminderState(
                    interval,
                    setting?.IsEnabled ?? true,
                    setting?.NextDueUtc ?? nowUtc + interval);
            });
    }

    public bool IsPaused => _globalPauseReasons.IsPaused;

    public IReadOnlyList<ReminderSnapshot> GetSnapshots() => _states
        .Select(pair => new ReminderSnapshot(
            pair.Key,
            pair.Value.Interval,
            pair.Value.IsEnabled,
            pair.Value.NextDueUtc,
            pair.Value.IsPending))
        .ToArray();

    public IReadOnlyList<ReminderKind> CollectDue(DateTimeOffset nowUtc)
    {
        if (IsPaused)
        {
            return Array.Empty<ReminderKind>();
        }

        var due = new List<ReminderKind>();
        foreach (var (kind, state) in _states)
        {
            if (state.IsEnabled && !state.PauseReasons.IsPaused && !state.IsPending && nowUtc >= state.NextDueUtc)
            {
                state.IsPending = true;
                due.Add(kind);
            }
        }

        return due;
    }

    public TimeSpan GetRemaining(ReminderKind kind, DateTimeOffset nowUtc)
    {
        var state = _states[kind];
        var effectiveNow = state.PausedAtUtc ?? nowUtc;
        var remaining = state.NextDueUtc - effectiveNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public void Complete(ReminderKind kind, DateTimeOffset nowUtc)
    {
        var state = _states[kind];
        state.NextDueUtc = GetDeadline(state, nowUtc, state.Interval);
        state.IsPending = false;
    }

    public void Snooze(ReminderKind kind, TimeSpan duration, DateTimeOffset nowUtc)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var state = _states[kind];
        state.NextDueUtc = GetDeadline(state, nowUtc, duration);
        state.IsPending = false;
    }

    public void SetEnabled(ReminderKind kind, bool enabled, DateTimeOffset nowUtc)
    {
        var state = _states[kind];
        state.IsEnabled = enabled;
        state.IsPending = false;
        state.NextDueUtc = GetDeadline(state, nowUtc, state.Interval);
    }

    public void SetInterval(ReminderKind kind, TimeSpan interval, DateTimeOffset nowUtc)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        var state = _states[kind];
        state.Interval = interval;
        state.IsPending = false;
        state.NextDueUtc = GetDeadline(state, nowUtc, interval);
    }

    public void Pause(DateTimeOffset nowUtc)
    {
        SetPauseReason(PauseReason.Manual, true, nowUtc);
    }

    public void Resume(DateTimeOffset nowUtc)
    {
        SetPauseReason(PauseReason.Manual, false, nowUtc);
    }

    public void SetPauseReason(PauseReason reason, bool isActive, DateTimeOffset nowUtc)
    {
        if (reason == PauseReason.Communication)
        {
            SetStatePauseReason(_states[ReminderKind.EyeBreak], reason, isActive, nowUtc);
            SetStatePauseReason(_states[ReminderKind.Walking], reason, isActive, nowUtc);
            return;
        }

        _globalPauseReasons.Set(reason, isActive);
        foreach (var state in _states.Values)
        {
            SetStatePauseReason(state, reason, isActive, nowUtc);
        }
    }

    private static DateTimeOffset GetDeadline(ReminderState state, DateTimeOffset nowUtc, TimeSpan delay) =>
        (state.PausedAtUtc ?? nowUtc) + delay;

    private static void SetStatePauseReason(ReminderState state, PauseReason reason, bool isActive, DateTimeOffset nowUtc)
    {
        var wasPaused = state.PauseReasons.IsPaused;
        state.PauseReasons.Set(reason, isActive);
        var isPaused = state.PauseReasons.IsPaused;

        if (!wasPaused && isPaused)
        {
            state.PausedAtUtc = nowUtc;
        }
        else if (wasPaused && !isPaused && state.PausedAtUtc is { } pausedAtUtc)
        {
            var pausedDuration = nowUtc - pausedAtUtc;
            if (pausedDuration > TimeSpan.Zero)
            {
                state.NextDueUtc += pausedDuration;
            }

            state.PausedAtUtc = null;
        }
    }

    public static TimeSpan GetDefaultInterval(ReminderKind kind) => kind switch
    {
        ReminderKind.EyeBreak => TimeSpan.FromMinutes(20),
        ReminderKind.Water => TimeSpan.FromMinutes(45),
        ReminderKind.Walking => TimeSpan.FromMinutes(60),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class ReminderState(TimeSpan interval, bool isEnabled, DateTimeOffset nextDueUtc)
    {
        public PauseReasonSet PauseReasons { get; } = new();
        public TimeSpan Interval { get; set; } = interval;
        public bool IsEnabled { get; set; } = isEnabled;
        public DateTimeOffset NextDueUtc { get; set; } = nextDueUtc;
        public bool IsPending { get; set; }
        public DateTimeOffset? PausedAtUtc { get; set; }
    }
}