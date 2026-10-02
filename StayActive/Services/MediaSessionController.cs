using Windows.Media.Control;

namespace StayActive.Services;

internal sealed class MediaSessionController
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<GlobalSystemMediaTransportControlsSession> _pausedSessions = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    // Media control is best-effort; unsupported players must not interrupt reminders.
    public async Task PausePlayingSessionsAsync(bool communicationActive)
    {
        await _gate.WaitAsync();
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            foreach (var session in _manager.GetSessions())
            {
                try
                {
                    if (IsCommunicationApp(session.SourceAppUserModelId, communicationActive)
                        || session.GetPlaybackInfo()?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        continue;
                    }

                    if (await session.TryPauseAsync())
                    {
                        _pausedSessions.Add(session);
                    }
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumePausedSessionsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var session in _pausedSessions)
            {
                try
                {
                    if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        await session.TryPlayAsync();
                    }
                }
                catch (Exception)
                {
                }
            }

            _pausedSessions.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsCommunicationApp(string appId, bool communicationActive)
    {
        if (!communicationActive)
        {
            return false;
        }

        var source = appId.ToLowerInvariant();
        var conferencingApp = source.Contains("teams")
            || source.Contains("zoom")
            || source.Contains("webex")
            || source.Contains("skype")
            || source.Contains("slack");
        var browserApp = source.Contains("edge")
            || source.Contains("chrome")
            || source.Contains("firefox")
            || source.Contains("brave")
            || source.Contains("opera");
        return conferencingApp || browserApp;
    }
}
