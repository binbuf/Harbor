# Task 016: Multi-Monitor & DPI Scaling Support

**Priority:** 5 (Platform Support)
**Depends on:** 002, 003, 010
**Blocks:** None directly

## Objective

Ensure all Harbor components (Top Menu, Dock, overlays) render correctly on multi-monitor setups with mixed DPI scaling, including handling windows dragged across monitor boundaries.

## Technical Reference

Refer to `docs/Design.md` Sections 8A (Per-Monitor DPI Awareness) and 8B (Multi-Monitor AppBar Behavior) for requirements.

## Requirements

1. **DPI-Aware Overlay Positioning:**
   - On every `EVENT_OBJECT_LOCATIONCHANGE`, retrieve the DPI for the monitor hosting the target window using `GetDpiForWindow(targetHwnd)`
   - Scale all overlay position and size calculations by `(currentDpi / 96.0)`
   - Ensure traffic light button sizes, mask dimensions, and text remain crisp at all DPI levels

2. **Cross-Monitor Drag Handling:**
   - Detect when a window is dragged across a monitor boundary (change in `MonitorFromWindow` return value)
   - On boundary crossing: destroy the overlay on the old monitor and recreate it on the new monitor's DPI context
   - WPF windows cannot change their DPI context after creation — recreation is mandatory (Section 8A)

3. **Multi-Monitor AppBars:**
   - Register separate AppBar instances for each connected monitor (Section 8B)
   - Top Menu and Dock appear on the primary monitor by default
   - Handle `WM_DISPLAYCHANGE` to detect monitor connect/disconnect
   - Rebuild AppBar registrations dynamically when display configuration changes

4. **DPI Testing Scenarios:**
   - 100% (96 DPI) on all monitors
   - 150% (144 DPI) on primary, 100% on secondary
   - 200% (192 DPI) on primary, 125% on secondary
   - Monitor disconnect/reconnect while Harbor is running

## Acceptance Criteria / Tests

- [ ] Overlay positions correctly on a 150% scaled monitor (no offset or sizing errors)
- [ ] Dragging a window from a 100% monitor to a 150% monitor keeps the overlay aligned
- [ ] Top Menu bar renders at correct height (24 DIP scaled to physical pixels) on each monitor
- [ ] Dock renders at correct size on each monitor
- [ ] Disconnecting a monitor removes its AppBar registrations without crashing
- [ ] Reconnecting a monitor restores AppBar registrations
- [ ] Unit tests verify DPI scaling math: `position * (dpi / 96.0)` for various DPI values
- [ ] Unit tests verify monitor boundary detection logic
- [ ] Integration test on a mixed-DPI setup (if available) confirms visual correctness
