namespace StayActive.Core;

[Flags]
public enum PauseReason
{
    None = 0,
    Manual = 1,
    SessionLocked = 2,
    SystemSuspended = 4,
    LidClosed = 8,
    Communication = 16
}

public sealed class PauseReasonSet
{
    public PauseReason ActiveReasons { get; private set; }

    public bool IsPaused => ActiveReasons != PauseReason.None;

    public bool Contains(PauseReason reason) => (ActiveReasons & reason) == reason;

    public void Set(PauseReason reason, bool isActive)
    {
        if (reason == PauseReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        var value = (int)reason;
        if ((value & (value - 1)) != 0)
        {
            throw new ArgumentException("Set one pause reason at a time.", nameof(reason));
        }

        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        ActiveReasons = isActive ? ActiveReasons | reason : ActiveReasons & ~reason;
    }
}