using StayActive.Core;
using StayActive.Models;
using StayActive.Services;
using System.Windows;

namespace StayActive;

public partial class App : System.Windows.Application
{
	private SettingsStore _settingsStore = null!;
	private AppSettings _settings = null!;
	private TimerEngine _timerEngine = null!;
	private SessionClock _sessionClock = null!;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		_sessionClock = new SessionClock();
		_settingsStore = new SettingsStore();
		_settings = _settingsStore.Load();
		_settings.Normalize();
		var nowUtc = _sessionClock.UtcNow;
		_timerEngine = CreateTimerEngine(_settings, nowUtc);
		if (_settings.IsPaused)
		{
			_sessionClock.Pause();
			_timerEngine.Pause(nowUtc);
		}

		try
		{
			StartupService.SetEnabled(_settings.StartWithWindows, _settings.StartMinimized, _settings.StartPausedForExam);
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or PlatformNotSupportedException)
		{
			_settings.StartWithWindows = false;
		}

		MainWindow window;
		try
		{
			window = new MainWindow(_settings, _timerEngine, _settingsStore, _sessionClock);
		}
		catch (Exception exception)
		{
			System.Windows.MessageBox.Show("StayActive could not initialize its window. " + exception.Message, "StayActive", MessageBoxButton.OK, MessageBoxImage.Error);
			Shutdown();
			return;
		}

		MainWindow = window;
		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		SessionEnding += (_, _) => window.ExitForSessionEnd();

		var arguments = Environment.GetCommandLineArgs();
		var startPaused = arguments.Contains("--paused", StringComparer.OrdinalIgnoreCase) || _settings.StartPausedForExam || _settings.IsPaused;
		var startMinimized = arguments.Contains("--minimized", StringComparer.OrdinalIgnoreCase) || _settings.StartMinimized && _settings.StartWithWindows;
		if (startPaused)
		{
			window.SetPaused(true);
			if (startMinimized)
			{
				window.HideToTray();
			}
			else
			{
				window.OpenDashboardForUser(skipGlassesCheck: true);
			}
		}
		else
		{
			window.ShowGlassesPrompt();
		}
	}

	internal static TimerEngine CreateTimerEngine(AppSettings settings, DateTimeOffset nowUtc)
	{
		var options = new Dictionary<ReminderKind, ReminderOptions>
		{
			[ReminderKind.EyeBreak] = new(TimeSpan.FromMinutes(settings.EyeBreakIntervalMinutes), settings.EyeBreakEnabled),
			[ReminderKind.Water] = new(TimeSpan.FromMinutes(settings.WaterIntervalMinutes), settings.WaterEnabled),
			[ReminderKind.Walking] = new(TimeSpan.FromMinutes(settings.WalkingIntervalMinutes), settings.WalkingEnabled)
		};
		return new TimerEngine(nowUtc, options);
	}

	internal void SaveSettings()
	{
		_settingsStore.Save(_settings);
	}
}

