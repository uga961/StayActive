# StayActive

StayActive is a small Windows desktop reminder app built with WPF and .NET 8. It keeps water, eye-break, and walking schedules independent, can stay in the system tray, and stores its settings locally.

## Current Scope

Implemented reminders:

- Water: 45-minute default; configurable from 30 to 120 minutes. The full-screen reminder resets only after the user confirms.
- 20-20-20 eye break: 20-minute interval and 20-second duration by default. Both are configurable; the reminder can be snoozed for five minutes.
- Walking: 60-minute interval and two-minute screen block by default. The interval is configurable, and the block can be set to 2 or 3 minutes. The full-screen reminder cannot be dismissed early through its window; it clears when the countdown ends. Pausing all reminders remains available for exam sessions.
- Pause all: available in the dashboard and tray menu. The pause state persists across restarts; reminders stay paused until resumed.
- The dashboard shows elapsed `HH:mm:ss` since launch. Eye, water, and walking countdowns use that same monotonic session clock and show total `MM:ss` remaining.
- Appearance can follow Windows or be set to Light or Dark in Settings; changes fade smoothly.

Windows startup is optional and off by default. Enable it in Settings to register StayActive for the current Windows user. Startup can be minimized to the tray and configured to start paused for exam sessions. The dashboard and tray also provide a one-click Pause all / Resume all control.

The **Appearance** setting has three options: **System** follows the Windows app-theme preference, while **Light** and **Dark** select a fixed palette. Changes are saved and transition with a brief fade.

## Start-Up Glasses Check

On launch, StayActive displays a full-screen glasses reminder with a brief animation. Click **I'm wearing my glasses** to confirm; StayActive then minimizes to the system tray and starts its reminders. This is a manual reminder, not computer vision or a verification system. The tray menu can pause the app for an exam; setting startup to paused skips the prompt.

Walking reminders intentionally do not use a camera: they block the screen for the configured 2- or 3-minute countdown. StayActive does not open the camera, collect images, or process face data.

## Run From Source

Requirements: Windows 10/11 x64 and the .NET 8 SDK.

```powershell
dotnet build StayActive.slnx -c Debug
dotnet test StayActive.slnx -c Debug
dotnet run --project StayActive/StayActive.csproj
```

The main window's close command is disabled; minimize hides StayActive to the tray, and maximize/restore remains available. Use the tray menu's Exit command to stop the application. Right-click the tray icon for Open, Pause/Resume, Settings, Create desktop shortcut, and Exit. The dashboard clock displays elapsed time since StayActive launched as `HH:mm:ss`.

## Publish A Single-File Executable

Run from the repository root on Windows:

```powershell
dotnet publish StayActive/StayActive.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o publish
```

The intended executable is `publish/StayActive.exe`. This packages the .NET runtime and StayActive icon, so a separate .NET installation is not required on the target PC. Verify the contents of the publish folder and test the executable on a clean Windows 10/11 x64 machine before distributing it. There is no installer; copy the executable to a stable location before enabling Windows startup, because the startup registry entry points to that path.

## Settings And Privacy

- Settings file: `%AppData%\StayActive\settings.json`
- Windows startup: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, only when enabled in Settings.
- No accounts, databases, analytics, network requests, or remote services are used by this application.
- Preferences are stored locally; reminder deadlines reset together on each app launch. No activity history or camera data is stored.
- Appearance mode is stored in `%AppData%\StayActive\settings.json`.
- Water interval range: 30-120 minutes.
- Walking interval range: 15-180 minutes; full-screen walk block: 2 or 3 minutes.
- Eye interval range: 5-60 minutes; eye-break duration range: 5-120 seconds.

Disable Start StayActive with Windows in Settings to remove the current-user startup entry. Pause all pauses reminders; Exit stops the current process.
