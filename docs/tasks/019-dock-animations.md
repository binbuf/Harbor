# Task 019: Dock — Animations & Auto-Hide

**Priority:** 6 (Polish)
**Depends on:** 006
**Blocks:** None directly

## Objective

Implement all Dock animations: icon hover magnification, bounce on launch, press feedback, active indicator dot, and auto-hide behavior with slide animations.

## Technical Reference

Refer to `docs/Design.md` Sections 5B (Icon States, Auto-Hide Behavior) and 5D (Animation Specifications — Dock icon bounce, Dock show/hide).

## Requirements

1. **Hover Magnification:**
   - Scale icon from 48x48 to 56x56 DIP (1.167x) on hover
   - Duration: 150ms, easing: `ease-out`
   - Use WPF `ScaleTransform` with `RenderTransformOrigin` at icon center (Section 5E)
   - Tooltip after 500ms hover: app name in rounded pill (`#1E1E1E` bg, `#FFFFFF` text, 6px 12px padding, 8px corner radius), 8px above icon

2. **Active Indicator Dot:**
   - Small circle below running app icons: 4px diameter
   - Color: `#FFFFFF` (dark) / `#000000` (light)
   - Centered horizontally, 2px below icon bottom edge

3. **Launch Bounce:**
   - When a pinned app is launched, bounce its icon
   - 3 bounces: 12 DIP upward translation, 300ms each (150ms up ease-out, 150ms down ease-in)
   - Total duration: 900ms

4. **Press Feedback:**
   - Scale to 44x44 DIP (0.917x) on press, 80ms ease-in
   - Return to 48x48 on release, 100ms ease-out

5. **Auto-Hide:**
   - Trigger zone: 2px tall invisible hit-test region at screen bottom edge
   - Reveal delay: 200ms after cursor enters trigger zone
   - Show animation: slide up, 250ms, `cubic-bezier(0.16, 1, 0.3, 1)`
   - Hide delay: 1000ms after cursor leaves dock area
   - Hide animation: slide down, 200ms, `cubic-bezier(0.7, 0, 0.84, 0)`
   - When hidden, release AppBar screen reservation so windows can use the full area

## Acceptance Criteria / Tests

- [ ] Hovering over a dock icon scales it to 56x56 smoothly
- [ ] Tooltip appears after 500ms hover with correct styling
- [ ] Running apps show a dot indicator below their icon
- [ ] Launching an app from a pinned dock icon triggers the bounce animation
- [ ] Pressing an icon scales it down, releasing scales it back
- [ ] Moving cursor to bottom screen edge reveals the dock after 200ms delay
- [ ] Moving cursor away hides the dock after 1000ms delay
- [ ] Auto-hide slide animations use the correct cubic-bezier curves
- [ ] Unit tests verify animation timing values match spec
- [ ] Unit tests verify auto-hide state machine (hidden → revealing → visible → hiding → hidden)
