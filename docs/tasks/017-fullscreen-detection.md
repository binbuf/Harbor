# Task 017: Fullscreen Detection & Retreat

**Priority:** 5 (Compatibility)
**Depends on:** 002, 008
**Blocks:** None directly

## Objective

Detect when a fullscreen or exclusive-mode application is running and retreat all Harbor overlays and AppBars to avoid interference with games and media players.

## Technical Reference

Refer to `docs/Design.md` Section 6B (Fullscreen & Exclusive Mode Applications) for detection strategy and retreat behavior.

## Requirements

1. **Fullscreen Detection:**
   - Monitor foreground window dimensions against the display's full resolution
   - Classify as fullscreen when: window exactly matches display bounds AND has no visible NONCLIENT area
   - Check for DXGI exclusive mode: `WS_EX_TOPMOST` combined with matching-resolution client rect
   - Check on every `EVENT_SYSTEM_FOREGROUND` event

2. **Retreat Behavior (on fullscreen detected):**
   - Hide all overlays for the display hosting the fullscreen app
   - Collapse AppBar reservation (free the reserved screen edge) so the fullscreen app gets the full display
   - Suppress `EVENT_OBJECT_LOCATIONCHANGE` processing for the fullscreen HWND (reduce overhead)

3. **Restore Behavior (on fullscreen exit):**
   - Detect when fullscreen app exits or loses focus
   - Restore AppBar reservations
   - Re-enable overlay tracking
   - Recreate overlays for visible windows on that display

4. **Multi-Monitor Consideration:**
   - Fullscreen on one monitor should NOT affect Harbor on other monitors
   - Only retreat on the specific display where the fullscreen app is running

## Acceptance Criteria / Tests

- [ ] Launching a fullscreen game hides the Top Menu and Dock on that monitor
- [ ] Alt-tabbing away from the fullscreen app restores Harbor's UI
- [ ] Fullscreen video in VLC triggers retreat behavior
- [ ] A borderless-windowed game that matches display resolution is correctly detected
- [ ] Fullscreen on monitor 1 does not affect Harbor on monitor 2
- [ ] AppBar reservation is released (fullscreen app gets the full screen area)
- [ ] Unit tests verify fullscreen classification logic (window rect vs. display bounds)
- [ ] Unit tests verify retreat/restore state machine transitions
- [ ] Integration test: maximize a window to full screen, confirm Harbor retreats and restores
