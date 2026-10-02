using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace StayActive.Services;

internal sealed class CommunicationDetector
{
    private static readonly HashSet<string> CommunicationProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ms-teams",
        "teams",
        "msedgewebview2",
        "zoom",
        "zoomoutlookplugin",
        "cptHost",
        "zTscoder",
        "webex",
        "webexhost",
        "webexmta",
        "ciscocollabhost",
        "skype",
        "skypeapp",
        "lync",
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera"
    };

    public bool IsCommunicationCaptureActive()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
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
                        if (processId == 0)
                        {
                            continue;
                        }

                        try
                        {
                            using var process = Process.GetProcessById((int)processId);
                            if (CommunicationProcesses.Contains(process.ProcessName))
                            {
                                return true;
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

        return false;
    }
}
