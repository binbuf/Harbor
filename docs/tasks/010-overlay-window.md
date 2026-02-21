# Task 010: Overlay Window Creation & Positioning

**Priority:** 4 (Overlay Core)
**Depends on:** 002, 008, 009
**Blocks:** 011, 014, 015

## Objective

Create the borderless, transparent WPF overlay windows that sit on top of application title bars. These overlays host the traffic light buttons (Task 011) and the title bar color mask (Task 014). This task focuses on window creation, positioning, and lifecycle — not content.

## Technical Reference

Refer to `docs/Design.md` Sections 4A Steps 3–4 (Overlay Spawn and Positioning), 7B (Mitigation Architecture — SetWindowPos bypass), and 7C (Performance Budget).

## Requirements

1. **Overlay Window Properties:**
   - Borderless, transparent WPF Window
   - Extended styles: `WS_EX_LAYERED | WS_EX_NOACTIVATE` — clicking the overlay must NOT steal focus
   - `WS_EX_TOOLWINDOW` — overlay must not appear in Alt+Tab or taskbar
   - `AllowsTransparency = true`, `WindowStyle = None`
   - Not owned by the target window (independent lifecycle)

2. **Overlay Manager:**
   - Create an `OverlayManager` class that manages the lifecycle of all active overlays
   - One overlay per tracked window (keyed by HWND)
   - Create overlay when a window becomes foreground or is discovered
   - Destroy overlay when the target window is closed or added to the skip list

3. **Initial Positioning:**
   - Size and position the overlay to match the title bar bounding rectangle from Task 009
   - Use direct `SetWindowPos` P/Invoke (not WPF `Window.Left`/`Window.Top`) for sub-millisecond positioning (Section 7B)
   - Flags: `SWP_NOACTIVATE | SWP_NOZORDER`

4. **Z-Order Management:**
   - Overlay must always appear directly above its target window
   - Re-evaluate z-order on `EVENT_SYSTEM_FOREGROUND`
   - When the target window loses focus, update overlay to show inactive state (visual change handled in Task 011)

5. **Overlay Content (Placeholder):**
   - For this task, render a semi-transparent debug rectangle so positioning can be visually verified
   - Left region: reserved for traffic light buttons (Task 011)
   - Right region: reserved for title bar color mask (Task 014)

## Acceptance Criteria / Tests

- [ ] Overlay window appears directly on top of the active window's title bar
- [ ] Clicking the overlay does NOT steal focus from the underlying application
- [ ] Overlay does NOT appear in Alt+Tab or the taskbar
- [ ] Overlay repositions correctly when the target window is moved (basic — full sync in Task 015)
- [ ] Overlay is destroyed when the target window is closed
- [ ] Multiple overlays can exist simultaneously for different windows
- [ ] Unit tests verify overlay creation with correct window styles (check extended style bits)
- [ ] Unit tests verify `OverlayManager` tracks overlays by HWND correctly
- [ ] Integration test: open Notepad, verify overlay appears aligned to its title bar
