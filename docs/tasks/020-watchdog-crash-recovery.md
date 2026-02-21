# Task 020: Watchdog & Crash Recovery

**Priority:** 4 (Safety Critical)
**Depends on:** 001, 002
**Blocks:** None directly (but must ship with MVP)

## Objective

Implement the watchdog process and crash recovery infrastructure described in Design.md Section 10. Harbor hides windows via `SW_HIDE` and replaces `explorer.exe` — an unhandled crash without recovery leaves the user with no taskbar, no system tray, and invisible windows.

## Technical Reference

Refer to `docs/Design.md` Sections 10A (Watchdog Process), 10B (Hidden Window Registry), 10C (Recovery Sequence), and 10D (Safe Startup).

## Requirements

1. **Hidden Window Registry:**
   - Maintain a memory-mapped file (`%TEMP%\harbor-hidden-hwnds.dat`) recording every HWND hidden via `SW_HIDE`
   - Update atomically on every hide/show operation
   - Format: simple array of HWND values readable by both the main process and the watchdog

2. **Heartbeat Mechanism:**
   - Harbor main process writes to a shared memory-mapped file every 500ms
   - Watchdog monitors for 3 consecutive missed heartbeats (1.5 seconds timeout)

3. **Watchdog Process (`harbor-watchdog.exe`):**
   - Lightweight native process (C or minimal .NET — independent of the main Harbor runtime)
   - Launched by Harbor on startup
   - Monitors the heartbeat and executes recovery on failure

4. **Recovery Sequence (on crash detection):**
   - Re-show all hidden windows from the Hidden Window Registry (`ShowWindow(SW_SHOW)`, skip invalid HWNDs via `IsWindow()`)
   - Restore native Windows animations (`SPI_SETCLIENTAREAANIMATION = TRUE`)
   - Launch `explorer.exe` as fallback shell
   - Display a system notification informing the user Harbor crashed
   - Write crash dump to `%LOCALAPPDATA%\Harbor\CrashDumps\`

5. **Safe Startup:**
   - On launch, check for and clear stale Hidden Window Registry entries from a previous session
   - Register `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` handlers that trigger recovery before process exit

## Acceptance Criteria / Tests

- [ ] Hidden Window Registry correctly tracks hide/show operations
- [ ] Watchdog detects a simulated crash (killed main process) within 2 seconds
- [ ] Recovery sequence re-shows all previously hidden windows
- [ ] Recovery sequence launches `explorer.exe` as fallback
- [ ] Native animations are restored after recovery
- [ ] Safe startup clears stale registry entries from a previous crashed session
- [ ] Unhandled exception in Harbor triggers recovery before exit
- [ ] Watchdog process does not crash if the registry file contains invalid (stale) HWNDs
- [ ] Unit tests for heartbeat write/read, registry add/remove, recovery logic
