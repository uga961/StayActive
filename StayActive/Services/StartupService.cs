using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace StayActive.Services;

internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StayActive";

    public static void SetEnabled(bool enabled, bool minimized = true, bool paused = false)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (!enabled)
        {
            runKey.DeleteValue(ValueName, false);
            return;
        }

        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The application path could not be determined.");
        var command = $"\"{executablePath}\"";
        if (minimized)
        {
            command += " --minimized";
        }

        if (paused)
        {
            command += " --paused";
        }

        runKey.SetValue(ValueName, command, RegistryValueKind.String);
    }

    public static void CreateDesktopShortcut()
    {
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The application path could not be determined.");
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktopPath, "StayActive.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is not available.");
        object shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("The shortcut service could not be started.");

        try
        {
            dynamic shortcutShell = shell;
            dynamic shortcut = shortcutShell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(executablePath);
            shortcut.IconLocation = $"{executablePath},0";
            shortcut.Description = "StayActive reminders";
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}