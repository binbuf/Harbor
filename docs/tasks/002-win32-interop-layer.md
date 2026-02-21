# Task 002: Win32 & DWM P/Invoke Interop Layer

**Priority:** 2 (Foundation)
**Depends on:** 001
**Blocks:** 004, 005, 006, 008, 009, 010, 011, 014, 015, 016, 017

## Objective

Create a centralized, well-tested P/Invoke interop layer for all Win32 and DWM APIs that Harbor depends on. This avoids scattered `DllImport` declarations and provides a single source of truth for native function signatures, constants, and struct definitions.

## Technical Reference

Refer to `docs/Design.md` Appendix A (Key Win32 & DWM API Reference) for the complete list of required APIs. Also reference Sections 4A, 7B, 7C, 8A, 9A for usage context.

## Requirements

Create static classes in `Harbor.Core` under a `NativeMethods` or `Interop` namespace:

1. **Window Management APIs:**
   - `GetForegroundWindow()` — Section 3A
   - `GetWindowRect()` / `GetClientRect()` — Section 6A
   - `SetWindowPos()` with flags: `SWP_NOACTIVATE`, `SWP_NOZORDER` — Section 7B
   - `ShowWindow()` with commands: `SW_HIDE`, `SW_SHOW`, `SW_MINIMIZE` — Section 4B
   - `SendMessage()` / `PostMessage()` for `SC_CLOSE`, `SC_MINIMIZE`, `SC_MAXIMIZE`, `SC_RESTORE` — Section 4A
   - `GetWindowLongPtr()` / `SetWindowLongPtr()` for extended window styles — Section 4A
   - `MonitorFromWindow()` — Section 8A
   - `IsWindow()` — Section 10B

2. **Event Hook APIs:**
   - `SetWinEventHook()` / `UnhookWinEvent()` — Section 4A
   - Event constants: `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_LOCATIONCHANGE`, `EVENT_OBJECT_CREATE`, `EVENT_OBJECT_DESTROY` — Sections 3B, 4A

3. **DWM APIs:**
   - `DwmRegisterThumbnail()` / `DwmUnregisterThumbnail()` / `DwmUpdateThumbnailProperties()` — Section 4B
   - `DwmFlush()` — Section 7B
   - `DwmGetColorizationColor()` — Section 9A
   - `DwmGetWindowAttribute()` — Sections 6A, 9A
   - `DwmSetWindowAttribute()` — Section 5E
   - `SetWindowCompositionAttribute()` — Section 5E

4. **DPI APIs:**
   - `GetDpiForWindow()` — Section 8A

5. **System APIs:**
   - `SystemParametersInfo()` with `SPI_SETCLIENTAREAANIMATION` — Section 4B
   - `SHGetFileInfo()` — Section 3B
   - `GetSystemMetrics()` with `SM_CYCAPTION` — Section 6A

6. **Supporting Types:**
   - All required structs (`RECT`, `POINT`, `SIZE`, `DWM_THUMBNAIL_PROPERTIES`, etc.)
   - All required enums and constants
   - Delegate types for `WinEventProc` callback

## Acceptance Criteria / Tests

- [ ] All P/Invoke signatures compile without warnings
- [ ] Unit tests verify struct sizes/layouts match expected native sizes using `Marshal.SizeOf`
- [ ] Integration test calls `GetForegroundWindow()` and returns a non-zero `IntPtr`
- [ ] Integration test calls `GetWindowRect()` on a known window and returns a valid `RECT`
- [ ] Integration test calls `GetDpiForWindow()` and returns a value >= 96
- [ ] Constants are verified against documented Windows SDK values
