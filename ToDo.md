## Completed in v2.1

- [x] Pause supported Windows system media sessions while a reminder overlay is visible.
- [x] Add a targeted VLC Space-key fallback for active VLC audio sessions not exposed through Windows media controls.
- [x] Resume only the media sessions StayActive observed playing and successfully paused.
- [x] During detected calls/meetings, pause eye-break and walking timers while leaving water reminders active.
- [x] Preserve the active reminder's remaining countdown over lock/sleep/lid pauses instead of restarting the full walk block.

Call detection is best-effort: active microphone capture plus a recognized meeting-client/browser process, checked every three seconds and before media is paused. Browser microphone use outside a call may also pause eye/walk timers. Media players must expose a Windows system media session to be controllable; unsupported raw-audio apps are left untouched. Recognized call media is excluded while a call is detected.