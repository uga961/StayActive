using StayActive.Core;

namespace StayActive.Core.Tests;

public class TimerEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CollectDue_EmitsOnlyElapsedEnabledRemindersOnce()
    {
        var engine = new TimerEngine(Start);

        Assert.Empty(engine.CollectDue(Start + TimeSpan.FromMinutes(19)));
        Assert.Equal(new[] { ReminderKind.EyeBreak }, engine.CollectDue(Start + TimeSpan.FromMinutes(20)));
        Assert.Empty(engine.CollectDue(Start + TimeSpan.FromMinutes(21)));
    }

    [Fact]
    public void CompletingOneReminder_DoesNotResetTheOthers()
    {
        var engine = new TimerEngine(Start);
        var completionTime = Start + TimeSpan.FromMinutes(20);
        engine.CollectDue(completionTime);

        engine.Complete(ReminderKind.EyeBreak, completionTime);

        var snapshots = engine.GetSnapshots().ToDictionary(snapshot => snapshot.Kind);
        Assert.Equal(completionTime + TimeSpan.FromMinutes(20), snapshots[ReminderKind.EyeBreak].NextDueUtc);
        Assert.Equal(Start + TimeSpan.FromMinutes(45), snapshots[ReminderKind.Water].NextDueUtc);
        Assert.Equal(Start + TimeSpan.FromMinutes(60), snapshots[ReminderKind.Walking].NextDueUtc);
    }

    [Fact]
    public void PauseAndResume_ShiftsEveryDeadlineByPausedDuration()
    {
        var engine = new TimerEngine(Start);
        var pauseTime = Start + TimeSpan.FromMinutes(10);
        var resumeTime = pauseTime + TimeSpan.FromMinutes(30);

        engine.Pause(pauseTime);
        Assert.Empty(engine.CollectDue(Start + TimeSpan.FromHours(2)));
        engine.Resume(resumeTime);

        var snapshots = engine.GetSnapshots().ToDictionary(snapshot => snapshot.Kind);
        Assert.Equal(Start + TimeSpan.FromMinutes(50), snapshots[ReminderKind.EyeBreak].NextDueUtc);
        Assert.Empty(engine.CollectDue(resumeTime));
        Assert.Equal(new[] { ReminderKind.EyeBreak }, engine.CollectDue(Start + TimeSpan.FromMinutes(50)));
    }

    [Fact]
    public void DisabledReminder_DoesNotTrigger()
    {
        var engine = new TimerEngine(Start);
        var disabledAt = Start + TimeSpan.FromMinutes(1);
        engine.SetEnabled(ReminderKind.Water, false, disabledAt);

        Assert.DoesNotContain(ReminderKind.Water, engine.CollectDue(Start + TimeSpan.FromHours(2)));
    }

    [Fact]
    public void IntervalChange_ReschedulesOnlyThatReminder()
    {
        var engine = new TimerEngine(Start);
        var changedAt = Start + TimeSpan.FromMinutes(5);
        engine.SetInterval(ReminderKind.Water, TimeSpan.FromMinutes(75), changedAt);

        var snapshots = engine.GetSnapshots().ToDictionary(snapshot => snapshot.Kind);
        Assert.Equal(changedAt + TimeSpan.FromMinutes(75), snapshots[ReminderKind.Water].NextDueUtc);
        Assert.Equal(Start + TimeSpan.FromMinutes(20), snapshots[ReminderKind.EyeBreak].NextDueUtc);
        Assert.Equal(Start + TimeSpan.FromMinutes(60), snapshots[ReminderKind.Walking].NextDueUtc);
    }

    [Fact]
    public void Snooze_ReschedulesOnlyTheSelectedReminder()
    {
        var engine = new TimerEngine(Start);
        engine.CollectDue(Start + TimeSpan.FromMinutes(20));
        var snoozeAt = Start + TimeSpan.FromMinutes(21);

        engine.Snooze(ReminderKind.EyeBreak, TimeSpan.FromMinutes(5), snoozeAt);

        var snapshots = engine.GetSnapshots().ToDictionary(snapshot => snapshot.Kind);
        Assert.Equal(snoozeAt + TimeSpan.FromMinutes(5), snapshots[ReminderKind.EyeBreak].NextDueUtc);
        Assert.Equal(Start + TimeSpan.FromMinutes(45), snapshots[ReminderKind.Water].NextDueUtc);
        Assert.False(snapshots[ReminderKind.EyeBreak].IsPending);
    }

    [Fact]
    public void SessionClock_UsesOneMonotonicSessionOrigin()
    {
        var clock = new SessionClock(Start);

        Assert.Equal(Start, clock.StartedAtUtc);
        Assert.True(clock.Elapsed >= TimeSpan.Zero);
        Assert.True(clock.UtcNow >= Start);
    }

    [Theory]
    [InlineData(20, 0, "20:00")]
    [InlineData(75, 9, "75:09")]
    [InlineData(0, 0, "00:00")]
    public void TimerFormatter_UsesTotalMinutesAndSeconds(int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, TimerFormatter.FormatMinutesSeconds(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void TimerFormatter_ClampsExpiredTimeToZero()
    {
        Assert.Equal("00:00", TimerFormatter.FormatMinutesSeconds(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void PauseReasonSet_RemainsPausedUntilEveryReasonClears()
    {
        var pauseReasons = new PauseReasonSet();
        pauseReasons.Set(PauseReason.Manual, true);
        pauseReasons.Set(PauseReason.SessionLocked, true);
        pauseReasons.Set(PauseReason.SystemSuspended, true);

        pauseReasons.Set(PauseReason.SessionLocked, false);
        pauseReasons.Set(PauseReason.SystemSuspended, false);

        Assert.True(pauseReasons.IsPaused);
        Assert.True(pauseReasons.Contains(PauseReason.Manual));

        pauseReasons.Set(PauseReason.Manual, false);

        Assert.False(pauseReasons.IsPaused);
    }

    [Fact]
    public void PauseReasonSet_RejectsCombinedReasonsInSingleUpdate()
    {
        var pauseReasons = new PauseReasonSet();

        Assert.Throws<ArgumentException>(() => pauseReasons.Set(PauseReason.SessionLocked | PauseReason.LidClosed, true));
    }

    [Fact]
    public void SessionClock_PauseFreezesElapsedTimeUntilResumed()
    {
        var clock = new SessionClock(Start);
        clock.Pause();
        var pausedElapsed = clock.Elapsed;
        var pausedUtc = clock.UtcNow;

        clock.Pause();

        Assert.True(clock.IsPaused);
        Assert.Equal(pausedElapsed, clock.Elapsed);
        Assert.Equal(pausedUtc, clock.UtcNow);

        clock.Resume();

        Assert.False(clock.IsPaused);
        Assert.True(clock.Elapsed >= pausedElapsed);
    }
}