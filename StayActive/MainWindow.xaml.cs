using StayActive.Core;
using StayActive.Models;
using StayActive.Services;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace StayActive;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly TimerEngine _engine;
    private readonly SettingsStore _settingsStore;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly System.Drawing.Icon _trayIconImage;
    private readonly Queue<ReminderKind> _pendingReminders = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly DispatcherTimer _communicationTimer;
    private readonly SessionClock _sessionClock;
    private readonly PauseReasonSet _pauseReasons = new();
    private readonly MediaSessionController _mediaSessionController = new();
    private readonly VlcMediaController _vlcMediaController = new();
    private readonly CommunicationDetector _communicationDetector = new();
    private ReminderWindow? _activeReminder;
    private ReminderKind? _activeReminderKind;
    private System.Windows.Interop.HwndSource? _windowSource;
    private IntPtr _lidSwitchNotification;
    private bool _glassesConfirmed;
    private bool _glassesCheckSkipped;
    private bool _isExiting;
    private bool _isLoadingSettings;
    private long _themeTransitionVersion;
    private const uint SystemCommandClose = 0xF060;
    private const uint MenuByCommand = 0x00000000;
    private const uint MenuGrayed = 0x00000001;
    private const int WindowMessagePowerBroadcast = 0x0218;
    private const int PowerBroadcastSettingChange = 0x8013;
    private const int WindowMessageSessionChange = 0x02B1;
    private const int SessionLockMessage = 0x0007;
    private const int SessionUnlockMessage = 0x0008;
    private const uint DeviceNotifyWindowHandle = 0x00000000;
    private const uint NotifyForThisSession = 0;
    private static readonly Guid LidSwitchStateChange = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    private bool _sessionNotificationsRegistered;
    private bool _communicationActive;
    private int _communicationCheckRunning;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetSystemMenu(IntPtr windowHandle, bool revert);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnableMenuItem(IntPtr menuHandle, uint item, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr notificationHandle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr windowHandle, uint flags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr windowHandle);

    internal MainWindow(AppSettings settings, TimerEngine engine, SettingsStore settingsStore, SessionClock sessionClock)
    {
        _settings = settings;
        _engine = engine;
        _settingsStore = settingsStore;
        _sessionClock = sessionClock;
        _pauseReasons.Set(PauseReason.Manual, settings.IsPaused);
        InitializeComponent();
        ApplyAppearance(_settings.AppearanceMode, animated: false);
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        SystemEvents.SessionSwitch += SystemSessionSwitch;
        SystemEvents.PowerModeChanged += SystemPowerModeChanged;

        var iconResource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Resources/StayActive.ico"));
        if (iconResource is null)
        {
            _trayIconImage = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        }
        else
        {
            using var iconStream = iconResource.Stream;
            _trayIconImage = new System.Drawing.Icon(iconStream);
        }

        _trayIcon = new Forms.NotifyIcon();
        _trayIcon.Text = "StayActive";
        _trayIcon.Icon = _trayIconImage;
        var trayMenu = CreateTrayMenu();
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => OpenDashboard();
        _trayIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                OpenDashboard();
            }
        };

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uiTimer.Tick += (_, _) => Tick();
        _uiTimer.Start();
        _communicationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _communicationTimer.Tick += CommunicationTimer_Tick;
        _communicationTimer.Start();
        CommunicationTimer_Tick(this, EventArgs.Empty);
        LoadSettingsIntoControls();
        ClockText.Text = FormatElapsedClock();
        RefreshDashboard();
        PersistSettings();
    }

    public void HideToTray()
    {
        Hide();
    }

    internal void OpenDashboardForUser(bool skipGlassesCheck = false)
    {
        _glassesConfirmed = !skipGlassesCheck;
        _glassesCheckSkipped = skipGlassesCheck;
        GlassesPromptView.Visibility = Visibility.Collapsed;
        Topmost = false;
        OpenDashboard();
    }

    internal void ShowGlassesPrompt()
    {
        if (_isExiting || _pauseReasons.IsPaused)
        {
            return;
        }

        _glassesCheckSkipped = false;
        GlassesPromptView.Visibility = Visibility.Visible;
        Topmost = true;
        WindowState = WindowState.Maximized;
        Show();
        Activate();
        PlayGlassesAnimation();
    }

    private void ConfirmGlassesAndShowDashboard()
    {
        if (_glassesConfirmed)
        {
            return;
        }

        _glassesConfirmed = true;
        _glassesCheckSkipped = false;
        GlassesStatusText.Text = "Confirmed for this session";
        GlassesPromptView.Visibility = Visibility.Collapsed;
        Topmost = false;
        OpenDashboard();
        ShowNextReminder();
    }

    private void ConfirmGlassesButton_Click(object sender, RoutedEventArgs e) => ConfirmGlassesAndShowDashboard();

    private void PlayGlassesAnimation()
    {
        GlassesDropTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        GlassesDropTransform.Y = -72;
        var animation = new System.Windows.Media.Animation.DoubleAnimation(-72, 0, TimeSpan.FromMilliseconds(720))
        {
            EasingFunction = new System.Windows.Media.Animation.BackEase { Amplitude = 0.25, EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        GlassesDropTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, animation);
    }

    private string FormatElapsedClock()
    {
        var elapsed = _sessionClock.Elapsed;
        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    public void SetPaused(bool paused)
    {
        _settings.IsPaused = paused;
        SetPauseReason(PauseReason.Manual, paused);
        PersistSettings();
    }

    private void SetPauseReason(PauseReason reason, bool isActive)
    {
        var wasPaused = _engine.IsPaused;
        _pauseReasons.Set(reason, isActive);
        var shouldPause = _pauseReasons.IsPaused;

        if (shouldPause && !wasPaused)
        {
            _engine.Pause(_sessionClock.UtcNow);
            _sessionClock.Pause();
            if (GlassesPromptView.Visibility == Visibility.Visible && !_glassesConfirmed)
            {
                _glassesCheckSkipped = true;
                GlassesPromptView.Visibility = Visibility.Collapsed;
                Topmost = false;
                HideToTray();
            }

            _activeReminder?.PauseCountdown();
            _activeReminder?.Hide();
        }
        else if (!shouldPause && wasPaused)
        {
            _engine.Resume(_sessionClock.UtcNow);
            _sessionClock.Resume();
            if (_glassesConfirmed)
            {
                if (_activeReminder is not null)
                {
                    ResumeActiveReminderIfAllowed();
                }
                else
                {
                    ShowNextReminder();
                }
            }
            else
            {
                ShowGlassesPrompt();
            }

            ResumeMediaIfIdle();
        }

        RefreshDashboard();
        UpdateTrayPauseItem();
    }

    private async void CommunicationTimer_Tick(object? sender, EventArgs e)
    {
        if (_isExiting || Interlocked.Exchange(ref _communicationCheckRunning, 1) != 0)
        {
            return;
        }

        try
        {
            var active = await Task.Run(_communicationDetector.IsCommunicationCaptureActive);
            if (!_isExiting && active != _communicationActive)
            {
                SetCommunicationActive(active);
            }

            if (!_isExiting && _activeReminder?.IsVisible == true)
            {
                await PauseMediaForOverlayAsync();
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _communicationCheckRunning, 0);
        }
    }

    private void SetCommunicationActive(bool active)
    {
        if (_communicationActive == active)
        {
            return;
        }

        _communicationActive = active;
        _engine.SetPauseReason(PauseReason.Communication, active, _sessionClock.UtcNow);
        if (active && _activeReminderKind is ReminderKind.EyeBreak or ReminderKind.Walking)
        {
            _activeReminder?.PauseCountdown();
            _activeReminder?.Hide();
            _ = ResumePausedMediaAsync();
        }

        RefreshDashboard();
        if (!active)
        {
            if (_activeReminder is not null)
            {
                ResumeActiveReminderIfAllowed();
            }
            else
            {
                ShowNextReminder();
                ResumeMediaIfIdle();
            }
        }
        else
        {
            ShowNextReminder();
        }
    }

    private void ResumeActiveReminderIfAllowed()
    {
        if (_activeReminder is null || _engine.IsPaused)
        {
            return;
        }

        if (_communicationActive && _activeReminderKind is ReminderKind.EyeBreak or ReminderKind.Walking)
        {
            _activeReminder.PauseCountdown();
            _activeReminder.Hide();
            return;
        }

        _activeReminder.Topmost = true;
        _activeReminder.Show();
        _activeReminder.Activate();
        _activeReminder.ResumeCountdown();
    }

    private void ResumeMediaIfIdle()
    {
        if (!_engine.IsPaused && _activeReminder is null)
        {
            _ = ResumePausedMediaAsync();
        }
    }

    private void SystemSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        var locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock => true,
            SessionSwitchReason.SessionUnlock => false,
            _ => (bool?)null
        };
        if (locked is { } isLocked)
        {
            DispatchPauseReason(PauseReason.SessionLocked, isLocked);
        }
    }

    private void SystemPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        var suspended = e.Mode switch
        {
            PowerModes.Suspend => true,
            PowerModes.Resume => false,
            _ => (bool?)null
        };
        if (suspended is { } isSuspended)
        {
            DispatchPauseReason(PauseReason.SystemSuspended, isSuspended);
        }
    }

    private void DispatchPauseReason(PauseReason reason, bool isActive)
    {
        if (_isExiting)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isExiting)
                {
                    SetPauseReason(reason, isActive);
                }
            }));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WindowMessageSessionChange)
        {
            var sessionChange = wParam.ToInt32();
            if (sessionChange == SessionLockMessage)
            {
                SetPauseReason(PauseReason.SessionLocked, true);
                handled = true;
            }
            else if (sessionChange == SessionUnlockMessage)
            {
                SetPauseReason(PauseReason.SessionLocked, false);
                handled = true;
            }
        }

        if (message == WindowMessagePowerBroadcast
            && wParam.ToInt32() == PowerBroadcastSettingChange
            && lParam != IntPtr.Zero
            && Marshal.PtrToStructure<Guid>(lParam) == LidSwitchStateChange)
        {
            var dataLength = Marshal.ReadInt32(lParam, 16);
            if (dataLength > 0)
            {
                var lidClosed = Marshal.ReadByte(lParam, 20) == 0;
                SetPauseReason(PauseReason.LidClosed, lidClosed);
            }
        }

        return IntPtr.Zero;
    }

    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open StayActive", null, (_, _) => OpenDashboard());
        menu.Items.Add("Pause all", null, (_, _) => SetPaused(!_settings.IsPaused));
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Create desktop shortcut", null, (_, _) => CreateDesktopShortcut());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void UpdateTrayPauseItem()
    {
        if (_trayIcon.ContextMenuStrip?.Items.Count > 1)
        {
            _trayIcon.ContextMenuStrip.Items[1].Text = _settings.IsPaused ? "Resume all" : "Pause all";
        }
    }

    private void Tick()
    {
        ClockText.Text = FormatElapsedClock();
        foreach (var kind in _engine.CollectDue(_sessionClock.UtcNow).OrderBy(GetReminderPriority))
        {
            _pendingReminders.Enqueue(kind);
        }

        RefreshDashboard();
        ShowNextReminder();
    }

    private static int GetReminderPriority(ReminderKind kind) => kind switch
    {
        ReminderKind.Water => 0,
        ReminderKind.EyeBreak => 1,
        ReminderKind.Walking => 2,
        _ => 3
    };

    private void ShowNextReminder()
    {
        if (_engine.IsPaused || !_glassesConfirmed || _activeReminder is not null || _pendingReminders.Count == 0)
        {
            return;
        }

        ReminderKind? nextKind = null;
        var pendingCount = _pendingReminders.Count;
        for (var index = 0; index < pendingCount; index++)
        {
            var candidate = _pendingReminders.Dequeue();
            if (_communicationActive && candidate is ReminderKind.EyeBreak or ReminderKind.Walking)
            {
                _pendingReminders.Enqueue(candidate);
                continue;
            }

            nextKind = candidate;
            break;
        }

        if (nextKind is not { } kind)
        {
            return;
        }

        var duration = kind switch
        {
            ReminderKind.EyeBreak => TimeSpan.FromSeconds(_settings.EyeBreakDurationSeconds),
            ReminderKind.Walking => TimeSpan.FromMinutes(_settings.WalkingDurationMinutes),
            ReminderKind.Water => TimeSpan.FromSeconds(35),
            _ => TimeSpan.Zero
        };

        _activeReminderKind = kind;
        _activeReminder = new ReminderWindow(kind, duration, action => HandleReminderAction(kind, action));
        _ = PauseMediaForOverlayAsync();
        _activeReminder.Closed += (_, _) =>
        {
            _activeReminder = null;
            _activeReminderKind = null;
            if (_isExiting)
            {
                return;
            }

            RefreshDashboard();
            ShowNextReminder();
            ResumeMediaIfIdle();
        };
        _activeReminder.Show();
    }

    private async Task PauseMediaForOverlayAsync()
    {
        var activeCall = await Task.Run(_communicationDetector.IsCommunicationCaptureActive);
        if (_isExiting)
        {
            return;
        }

        if (activeCall != _communicationActive)
        {
            await Dispatcher.InvokeAsync(() => SetCommunicationActive(activeCall));
        }

        if (!_isExiting && _activeReminder?.IsVisible == true)
        {
            var vlcPausedThroughWindowsMedia = await _mediaSessionController.PausePlayingSessionsAsync(_communicationActive);
            if (!vlcPausedThroughWindowsMedia)
            {
                _vlcMediaController.PausePlayingSessions();
            }
        }
    }

    private async Task ResumePausedMediaAsync()
    {
        await _mediaSessionController.ResumePausedSessionsAsync();
        _vlcMediaController.ResumePausedSessions();
    }

    private void HandleReminderAction(ReminderKind kind, ReminderAction action)
    {
        if (_isExiting)
        {
            return;
        }

        var now = _sessionClock.UtcNow;
        if (action == ReminderAction.Complete)
        {
            _engine.Complete(kind, now);
        }
        else if (action == ReminderAction.Snooze)
        {
            _engine.Snooze(kind, TimeSpan.FromMinutes(5), now);
        }
        else
        {
            _engine.Snooze(kind, TimeSpan.FromMinutes(5), now);
        }

        PersistSettings();
        RefreshDashboard();
    }

    private void WaterTakenButton_Click(object sender, RoutedEventArgs e) => CompleteActivityFromDashboard(ReminderKind.Water);

    private void WalkingTakenButton_Click(object sender, RoutedEventArgs e) => CompleteActivityFromDashboard(ReminderKind.Walking);

    private void CompleteActivityFromDashboard(ReminderKind kind)
    {
        _engine.Complete(kind, _sessionClock.UtcNow);
        var pendingReminders = _pendingReminders.Where(pending => pending != kind).ToArray();
        _pendingReminders.Clear();
        foreach (var pending in pendingReminders)
        {
            _pendingReminders.Enqueue(pending);
        }

        PersistSettings();
        RefreshDashboard();
    }

    private void RefreshDashboard()
    {
        PauseButton.Content = _settings.IsPaused ? "Resume all" : "Pause all";
        PauseStatusText.Text = GetPauseStatus();
        PauseDetailText.Text = _communicationActive && !_pauseReasons.IsPaused
            ? "Eye and walking timers are paused during the call. Water reminders remain active."
            : _pauseReasons.IsPaused
            ? "All reminder timers are held until the active pause conditions clear."
            : "Pause all reminders any time, including during an exam.";
        PauseStatusText.Foreground = (System.Windows.Media.Brush)FindResource(_engine.IsPaused ? "ThemePausedText" : "ThemeGood");

        EyeStateText.Text = _settings.EyeBreakEnabled ? "Enabled" : "Disabled";
        WaterStateText.Text = _settings.WaterEnabled ? "Enabled" : "Disabled";
        WalkingStateText.Text = _settings.WalkingEnabled ? "Enabled" : "Disabled";
        GlassesStatusText.Text = _glassesConfirmed
            ? "Confirmed for this session"
            : _glassesCheckSkipped ? "Skipped while reminders are paused" : "Wear glasses before starting";
        EyeRemainingText.Text = GetTimerText(ReminderKind.EyeBreak, "Next break");
        WaterRemainingText.Text = GetTimerText(ReminderKind.Water, "Next reminder");
        WalkingRemainingText.Text = GetTimerText(ReminderKind.Walking, "Next reminder");
        FooterStatusText.Text = "Settings are stored locally. Camera is off.";
        UpdateTrayPauseItem();
    }

    private string GetPauseStatus()
    {
        if (_pauseReasons.Contains(PauseReason.SessionLocked))
        {
            return "Windows session locked: reminders paused";
        }

        if (_pauseReasons.Contains(PauseReason.LidClosed))
        {
            return "Lid closed: reminders paused";
        }

        if (_pauseReasons.Contains(PauseReason.SystemSuspended))
        {
            return "System asleep: reminders paused";
        }

        if (_communicationActive)
        {
            return "Call detected: eye and walking reminders paused";
        }

        return _settings.IsPaused ? "Exam mode: reminders paused" : "Reminders are running";
    }

    private string GetTimerText(ReminderKind kind, string label)
    {
        var enabled = kind switch
        {
            ReminderKind.EyeBreak => _settings.EyeBreakEnabled,
            ReminderKind.Water => _settings.WaterEnabled,
            _ => _settings.WalkingEnabled
        };

        if (!enabled)
        {
            return "Disabled";
        }

        if (_engine.GetSnapshots().First(snapshot => snapshot.Kind == kind).IsPending)
        {
            return "Reminder ready";
        }

        var remaining = _engine.GetRemaining(kind, _sessionClock.UtcNow);
        return $"{label}: {TimerFormatter.FormatMinutesSeconds(remaining)}";
    }

    private void LoadSettingsIntoControls()
    {
        _isLoadingSettings = true;
        SystemAppearanceRadio.IsChecked = _settings.AppearanceMode == "System";
        LightAppearanceRadio.IsChecked = _settings.AppearanceMode == "Light";
        DarkAppearanceRadio.IsChecked = _settings.AppearanceMode == "Dark";
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        StartMinimizedCheck.IsChecked = _settings.StartMinimized;
        StartPausedCheck.IsChecked = _settings.StartPausedForExam;
        EyeEnabledCheck.IsChecked = _settings.EyeBreakEnabled;
        EyeIntervalBox.Text = _settings.EyeBreakIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        EyeDurationBox.Text = _settings.EyeBreakDurationSeconds.ToString(CultureInfo.InvariantCulture);
        WaterEnabledCheck.IsChecked = _settings.WaterEnabled;
        WaterIntervalBox.Text = _settings.WaterIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        WalkingEnabledCheck.IsChecked = _settings.WalkingEnabled;
        WalkingIntervalBox.Text = _settings.WalkingIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        WalkingDurationBox.Text = _settings.WalkingDurationMinutes.ToString(CultureInfo.InvariantCulture);
        _isLoadingSettings = false;
    }

    private void AppearanceMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || sender is not System.Windows.Controls.RadioButton { IsChecked: true, Tag: string mode })
        {
            return;
        }

        _settings.AppearanceMode = mode;
        ApplyAppearance(mode, animated: true);
        PersistSettings();
    }

    private void ApplyAppearance(string mode, bool animated)
    {
        var isDark = mode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDarkMode()
        };

        if (!animated)
        {
            SetThemePalette(isDark);
            return;
        }

        var transitionVersion = ++_themeTransitionVersion;
        AppShell.BeginAnimation(UIElement.OpacityProperty, null);
        AppShell.Opacity = 1;
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0.84, TimeSpan.FromMilliseconds(110));
        fadeOut.Completed += (_, _) =>
        {
            if (transitionVersion != _themeTransitionVersion)
            {
                return;
            }

            SetThemePalette(isDark);
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.84, 1, TimeSpan.FromMilliseconds(180));
            fadeIn.Completed += (_, _) =>
            {
                if (transitionVersion == _themeTransitionVersion)
                {
                    AppShell.BeginAnimation(UIElement.OpacityProperty, null);
                    AppShell.Opacity = 1;
                }
            };
            AppShell.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };
        AppShell.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void SetThemePalette(bool isDark)
    {
        var palette = isDark
            ? new Dictionary<string, string>
            {
                ["ThemeWindowBackground"] = "#1B2421",
                ["ThemeCardBackground"] = "#26322D",
                ["ThemeCardMutedBackground"] = "#222D28",
                ["ThemePrimaryText"] = "#EDF3EF",
                ["ThemeMutedText"] = "#B0C0B7",
                ["ThemeBorder"] = "#46574F",
                ["ThemeAccent"] = "#78C5D1",
                ["ThemeAccentSurface"] = "#304A49",
                ["ThemeGood"] = "#8BC9A4",
                ["ThemeWater"] = "#78C5D1",
                ["ThemeStatusSurface"] = "#293A32",
                ["ThemeInfoSurface"] = "#3A3429",
                ["ThemeInfoText"] = "#F0D6A7",
                ["ThemePausedText"] = "#F0B98D"
            }
            : new Dictionary<string, string>
            {
                ["ThemeWindowBackground"] = "#F3F6F3",
                ["ThemeCardBackground"] = "#FFFFFF",
                ["ThemeCardMutedBackground"] = "#F7F8F6",
                ["ThemePrimaryText"] = "#20312C",
                ["ThemeMutedText"] = "#60736B",
                ["ThemeBorder"] = "#CAD6D0",
                ["ThemeAccent"] = "#39768B",
                ["ThemeAccentSurface"] = "#E1EEE6",
                ["ThemeGood"] = "#3A765E",
                ["ThemeWater"] = "#39768B",
                ["ThemeStatusSurface"] = "#E1EEE6",
                ["ThemeInfoSurface"] = "#F4EEE2",
                ["ThemeInfoText"] = "#604C2D",
                ["ThemePausedText"] = "#824928"
            };

        foreach (var (key, hexColor) in palette)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            Resources[key] = new System.Windows.Media.SolidColorBrush(color);
        }
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1), CultureInfo.InvariantCulture) == 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    private void SystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.AppearanceMode == "System")
        {
            Dispatcher.BeginInvoke(() => ApplyAppearance("System", animated: true));
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadInt(EyeIntervalBox, 5, 60, "Eye-break interval", out var eyeInterval)
            || !TryReadInt(EyeDurationBox, 5, 120, "Eye-break duration", out var eyeDuration)
            || !TryReadInt(WaterIntervalBox, 30, 120, "Water interval", out var waterInterval)
            || !TryReadInt(WalkingIntervalBox, 15, 180, "Walking interval", out var walkingInterval)
            || !TryReadInt(WalkingDurationBox, 2, 3, "Walking block duration", out var walkingDuration))
        {
            return;
        }

        var now = _sessionClock.UtcNow;
        ApplyTimerSettings(ReminderKind.EyeBreak, _settings.EyeBreakEnabled, _settings.EyeBreakIntervalMinutes,
            EyeEnabledCheck.IsChecked == true, eyeInterval, now);
        ApplyTimerSettings(ReminderKind.Water, _settings.WaterEnabled, _settings.WaterIntervalMinutes,
            WaterEnabledCheck.IsChecked == true, waterInterval, now);
        ApplyTimerSettings(ReminderKind.Walking, _settings.WalkingEnabled, _settings.WalkingIntervalMinutes,
            WalkingEnabledCheck.IsChecked == true, walkingInterval, now);

        _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        _settings.StartMinimized = StartMinimizedCheck.IsChecked == true;
        _settings.StartPausedForExam = StartPausedCheck.IsChecked == true;
        _settings.EyeBreakEnabled = EyeEnabledCheck.IsChecked == true;
        _settings.EyeBreakIntervalMinutes = eyeInterval;
        _settings.EyeBreakDurationSeconds = eyeDuration;
        _settings.WaterEnabled = WaterEnabledCheck.IsChecked == true;
        _settings.WaterIntervalMinutes = waterInterval;
        _settings.WalkingEnabled = WalkingEnabledCheck.IsChecked == true;
        _settings.WalkingIntervalMinutes = walkingInterval;
        _settings.WalkingDurationMinutes = walkingDuration;

        try
        {
            StartupService.SetEnabled(_settings.StartWithWindows, _settings.StartMinimized, _settings.StartPausedForExam);
            PersistSettings();
            FooterStatusText.Text = "Settings saved on this PC.";
            ShowDashboard();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            _settings.StartWithWindows = false;
            StartWithWindowsCheck.IsChecked = false;
            FooterStatusText.Text = "Startup setting could not be changed. Other settings are still saved.";
            PersistSettings();
        }

        RefreshDashboard();
    }

    private void ApplyTimerSettings(ReminderKind kind, bool oldEnabled, int oldInterval, bool enabled, int interval, DateTimeOffset now)
    {
        if (oldEnabled != enabled)
        {
            _engine.SetEnabled(kind, enabled, now);
        }

        if (oldInterval != interval)
        {
            _engine.SetInterval(kind, TimeSpan.FromMinutes(interval), now);
        }

        if (!enabled)
        {
            var retained = _pendingReminders.Where(pending => pending != kind).ToArray();
            _pendingReminders.Clear();
            foreach (var pending in retained)
            {
                _pendingReminders.Enqueue(pending);
            }
        }
    }

    private bool TryReadInt(System.Windows.Controls.TextBox textBox, int minimum, int maximum, string label, out int value)
    {
        if (!int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            || value < minimum || value > maximum)
        {
            FooterStatusText.Text = $"{label} must be between {minimum} and {maximum}.";
            textBox.Focus();
            textBox.SelectAll();
            return false;
        }

        return true;
    }

    private void PersistSettings()
    {
        try
        {
            ((App)System.Windows.Application.Current).SaveSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FooterStatusText.Text = "Settings could not be written. Check access to your AppData folder.";
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e) => SetPaused(!_settings.IsPaused);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void DashboardButton_Click(object sender, RoutedEventArgs e) => ShowDashboard();

    private void OpenSettings()
    {
        LoadSettingsIntoControls();
        DashboardView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private void ShowDashboard()
    {
        SettingsView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
    }

    private void OpenDashboard()
    {
        if (!_glassesConfirmed && !_engine.IsPaused)
        {
            ShowGlassesPrompt();
            return;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        _uiTimer.Stop();
        _communicationTimer.Stop();
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        SystemEvents.SessionSwitch -= SystemSessionSwitch;
        SystemEvents.PowerModeChanged -= SystemPowerModeChanged;
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowMessageHook);
        }

        if (_sessionNotificationsRegistered)
        {
            WTSUnRegisterSessionNotification(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            _sessionNotificationsRegistered = false;
        }

        if (_lidSwitchNotification != IntPtr.Zero)
        {
            UnregisterPowerSettingNotification(_lidSwitchNotification);
            _lidSwitchNotification = IntPtr.Zero;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIconImage.Dispose();
        _activeReminder?.CloseForAppControl();
        await ResumePausedMediaAsync();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void CreateDesktopShortcut()
    {
        try
        {
            StartupService.CreateDesktopShortcut();
            FooterStatusText.Text = "StayActive shortcut created on the desktop.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or COMException)
        {
            FooterStatusText.Text = "Could not create a desktop shortcut.";
        }
    }

    internal void ExitForSessionEnd() => ExitApplication();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var windowHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _windowSource = System.Windows.Interop.HwndSource.FromHwnd(windowHandle);
        _windowSource?.AddHook(WindowMessageHook);
        _sessionNotificationsRegistered = WTSRegisterSessionNotification(windowHandle, NotifyForThisSession);
        var lidSwitchStateChange = LidSwitchStateChange;
        _lidSwitchNotification = RegisterPowerSettingNotification(windowHandle, ref lidSwitchStateChange, DeviceNotifyWindowHandle);
        var systemMenu = GetSystemMenu(windowHandle, false);
        EnableMenuItem(systemMenu, SystemCommandClose, MenuByCommand | MenuGrayed);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            PersistSettings();
        }
    }
}