## Completed in v2.1

- [x] Pause supported Windows system media sessions while a reminder overlay is visible.
- [x] Resume only the media sessions StayActive observed playing and successfully paused.
- [x] During detected calls/meetings, pause eye-break and walking timers while leaving water reminders active.

Call detection is best-effort: active microphone capture plus a recognized meeting-client/browser process, checked every three seconds and before media is paused. Browser microphone use outside a call may also pause eye/walk timers. Media players must expose a Windows system media session to be controllable; unsupported raw-audio apps are left untouched. Recognized call media is excluded while a call is detected.