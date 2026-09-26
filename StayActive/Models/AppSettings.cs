namespace StayActive.Models;

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool StartPausedForExam { get; set; }
    public string AppearanceMode { get; set; } = "System";
    public bool IsPaused { get; set; }
    public bool EyeBreakEnabled { get; set; } = true;
    public int EyeBreakIntervalMinutes { get; set; } = 20;
    public int EyeBreakDurationSeconds { get; set; } = 20;
    public bool WaterEnabled { get; set; } = true;
    public int WaterIntervalMinutes { get; set; } = 45;
    public bool WalkingEnabled { get; set; } = true;
    public int WalkingIntervalMinutes { get; set; } = 60;
    public int WalkingDurationMinutes { get; set; } = 2;

    public void Normalize()
    {
        EyeBreakIntervalMinutes = Math.Clamp(EyeBreakIntervalMinutes, 5, 60);
        EyeBreakDurationSeconds = Math.Clamp(EyeBreakDurationSeconds, 5, 120);
        WaterIntervalMinutes = Math.Clamp(WaterIntervalMinutes, 30, 120);
        WalkingIntervalMinutes = Math.Clamp(WalkingIntervalMinutes, 15, 180);
        WalkingDurationMinutes = Math.Clamp(WalkingDurationMinutes, 2, 3);
        if (AppearanceMode is not ("System" or "Light" or "Dark"))
        {
            AppearanceMode = "System";
        }
    }
}