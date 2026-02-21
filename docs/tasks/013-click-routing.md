# Task 013: Traffic Light Click Routing

**Priority:** 5 (Core Feature)
**Depends on:** 002, 011
**Blocks:** 020

## Objective

Wire the traffic light button clicks to actual window commands, sending the appropriate system messages to the target application window.

## Technical Reference

Refer to `docs/Design.md` Section 4A Step 5 (Click Routing) for the message routing specification.

## Requirements

1. **Close Button:**
   - Send `WM_SYSCOMMAND` with `SC_CLOSE` to the target HWND
   - Remove the overlay for that window after the close command is sent

2. **Minimize Button:**
   - Send `WM_SYSCOMMAND` with `SC_MINIMIZE` to the target HWND
   - (Genie animation intercepts this in Task 020 — for now, standard minimize)
   - Update dock icon to show the window as minimized

3. **Maximize/Restore Button:**
   - If window is not maximized: send `WM_SYSCOMMAND` with `SC_MAXIMIZE`
   - If window is already maximized: send `WM_SYSCOMMAND` with `SC_RESTORE`
   - Update the maximize button glyph to reflect current state (+ for maximize, ⤢ or restore icon)

4. **Window State Tracking:**
   - Monitor window state (normal, maximized, minimized) to set correct maximize/restore glyph
   - Update on `EVENT_OBJECT_LOCATIONCHANGE` and after sending commands

5. **Edge Cases:**
   - Applications that block `SC_CLOSE` (e.g., "Save changes?" dialogs) — Harbor should not force-close
   - Applications without minimize or maximize capabilities (check `WS_MINIMIZEBOX` / `WS_MAXIMIZEBOX` styles)
   - Disable/gray out buttons for windows that don't support the action

## Acceptance Criteria / Tests

- [ ] Clicking Close on the overlay closes the target application (verified with Notepad)
- [ ] Clicking Minimize sends the window to the taskbar / dock
- [ ] Clicking Maximize expands the window to fill the work area
- [ ] Clicking Maximize on an already-maximized window restores it
- [ ] The maximize button glyph updates correctly based on window state
- [ ] Windows without `WS_MINIMIZEBOX` show a disabled minimize button
- [ ] Applications with unsaved changes can still show their "Save?" dialog (Harbor doesn't force-close)
- [ ] Unit tests verify correct `WM_SYSCOMMAND` message is sent for each button
- [ ] Unit tests verify window capability detection (min/max box style checks)
