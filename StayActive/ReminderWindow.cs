using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using StayActive.Core;

namespace StayActive;

internal enum ReminderAction
{
    Complete,
    Snooze,
    Dismiss
}

internal sealed class ReminderWindow : Window
{
    private readonly DispatcherTimer? _countdownTimer;
    private readonly TextBlock _countdownText;
    private readonly Action<ReminderAction> _onAction;
    private readonly DateTimeOffset _endsAtUtc;
    private readonly bool _blocksDismissal;
    private bool _allowClose;
    private bool _actionSent;

    public ReminderWindow(ReminderKind kind, TimeSpan duration, Action<ReminderAction> onAction)
    {
        _onAction = onAction;
        _endsAtUtc = DateTimeOffset.UtcNow + duration;
        _blocksDismissal = kind == ReminderKind.Walking;
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Background = kind switch
        {
            ReminderKind.Water => new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 111, 151)),
            ReminderKind.EyeBreak => new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 79, 75)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 100, 75))
        };

        var title = kind switch
        {
            ReminderKind.Water => "DRINK WATER",
            ReminderKind.EyeBreak => "20-20-20 BREAK",
            _ => "TIME TO MOVE"
        };
        var description = kind switch
        {
            ReminderKind.Water => "Take a short water break. This reminder closes if ignored.",
            ReminderKind.EyeBreak => "Look at something at least 20 feet / 6 meters away.",
            _ => "Walk away from your desk. This screen will clear when the countdown ends."
        };

        _countdownText = new TextBlock
        {
            Text = FormatTime(duration),
            FontSize = 28,
            Foreground = System.Windows.Media.Brushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 28)
        };

        var content = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 760,
            Margin = new Thickness(32)
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 44,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 22,
            Foreground = System.Windows.Media.Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });

        if (duration > TimeSpan.Zero)
        {
            content.Children.Add(_countdownText);
        }

        if (kind != ReminderKind.Walking)
        {
            content.Children.Add(CreateButton(kind == ReminderKind.Water ? "I've Drunk Water" : "I'm Done", ReminderAction.Complete));
        }

        if (kind == ReminderKind.EyeBreak)
        {
            content.Children.Add(CreateButton("Snooze 5 min", ReminderAction.Snooze));
        }

        Content = content;
        Closing += (_, args) =>
        {
            if (_blocksDismissal && !_allowClose)
            {
                args.Cancel = true;
            }
        };
        Closed += (_, _) =>
        {
            if (!_actionSent)
            {
                _onAction(ReminderAction.Dismiss);
            }
        };

        if (duration > TimeSpan.Zero)
        {
            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) =>
            {
                var remaining = _endsAtUtc - DateTimeOffset.UtcNow;
                _countdownText.Text = FormatTime(remaining);
                if (remaining <= TimeSpan.Zero)
                {
                    _countdownTimer.Stop();
                    Finish(kind == ReminderKind.Water ? ReminderAction.Dismiss : ReminderAction.Complete);
                }
            };
            _countdownTimer.Start();
        }
    }

    private System.Windows.Controls.Button CreateButton(string text, ReminderAction action)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = text,
            FontSize = 18,
            Padding = new Thickness(24, 12, 24, 12),
            Margin = new Thickness(0, 8, 0, 0),
            MinWidth = 190,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
        };
        button.Click += (_, _) => Finish(action);
        return button;
    }

    private static string FormatTime(TimeSpan time) => $"{Math.Max(0, (int)time.TotalMinutes):00}:{Math.Max(0, time.Seconds):00}";

    private void Finish(ReminderAction action)
    {
        _actionSent = true;
        _allowClose = true;
        _countdownTimer?.Stop();
        _onAction(action);
        Close();
    }

    public void CloseForAppControl()
    {
        _actionSent = true;
        _allowClose = true;
        _countdownTimer?.Stop();
        Close();
    }
}