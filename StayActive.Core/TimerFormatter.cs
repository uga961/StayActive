namespace StayActive.Core;

public static class TimerFormatter
{
    public static string FormatMinutesSeconds(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}