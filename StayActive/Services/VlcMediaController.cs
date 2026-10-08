using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace StayActive.Services;

internal sealed class VlcMediaController
{
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageKeyUp = 0x0101;
    private const uint VirtualKeySpace = 0x20;
    private static readonly IntPtr SpaceKeyDownData = new(0x00390001);
    private static readonly IntPtr SpaceKeyUpData = new(unchecked((int)0xC0390001));
    private const uint SendMessageAbortIfHung = 0x0002;
    private readonly HashSet<int> _pausedProcessIds = new();

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMilliseconds,
        out IntPtr result);

    public void PausePlayingSessions()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                    {
                        using var session = sessions[sessionIndex];
                        if (session.State != AudioSessionState.AudioSessionStateActive)
                        {
                            continue;
                        }

                        var processId = session.GetProcessID;
                        if (processId == 0 || _pausedProcessIds.Contains((int)processId))
                        {
                            continue;
                        }

                        try
                        {
                            using var process = Process.GetProcessById((int)processId);
                            if (string.Equals(process.ProcessName, "vlc", StringComparison.OrdinalIgnoreCase)
                                && SendSpaceShortcut((int)processId))
                            {
                                _pausedProcessIds.Add((int)processId);
                            }
                        }
                        catch (ArgumentException)
                        {
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public void ResumePausedSessions()
    {
        foreach (var processId in _pausedProcessIds)
        {
            SendSpaceShortcut(processId);
        }

        _pausedProcessIds.Clear();
    }

    private static bool SendSpaceShortcut(int targetProcessId)
    {
        var delivered = false;
        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId != targetProcessId || !IsWindowVisible(windowHandle))
            {
                return true;
            }

            var keyDownSent = SendMessageTimeout(
                windowHandle,
                WindowMessageKeyDown,
                new IntPtr(VirtualKeySpace),
                SpaceKeyDownData,
                SendMessageAbortIfHung,
                500,
                out _);
            var keyUpSent = SendMessageTimeout(
                windowHandle,
                WindowMessageKeyUp,
                new IntPtr(VirtualKeySpace),
                SpaceKeyUpData,
                SendMessageAbortIfHung,
                500,
                out _);
            delivered |= keyDownSent != IntPtr.Zero && keyUpSent != IntPtr.Zero;
            return !delivered;
        }, IntPtr.Zero);

        return delivered;
    }
}
