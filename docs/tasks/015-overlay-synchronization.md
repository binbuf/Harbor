# Task 015: Overlay Synchronization (Dual-Layer Tracking)

**Priority:** 5 (Critical Path)
**Depends on:** 002, 008, 010
**Blocks:** None directly (but critical for UX quality)

## Objective

Implement the dual-layer overlay synchronization system that keeps overlays perfectly aligned with their target windows during dragging and resizing, achieving sub-frame latency.

## Technical Reference

Refer to `docs/Design.md` Sections 7A (The Problem), 7B (Mitigation Architecture — Dual-Layer Tracking), 7C (Performance Budget), and 7D (Escape Hatch) for the full synchronization architecture. **This is identified as the single hardest engineering challenge in Harbor.**

## Requirements

1. **Layer 1: Event-Driven (Primary):**
   - On `EVENT_OBJECT_LOCATIONCHANGE`, call `GetWindowRect` on the target HWND (direct Win32, not UIA)
   - Reposition overlay using `SetWindowPos` via P/Invoke with `SWP_NOACTIVATE | SWP_NOZORDER`
   - **Critical:** Bypass WPF layout system entirely — use `HwndSource.Handle` with direct P/Invoke, NOT `Window.Left`/`Window.Top`

2. **Layer 2: High-Frequency Polling (Fallback):**
   - Dedicated background thread polling `GetWindowRect` at ~120Hz (8.3ms interval)
   - Use high-resolution timer (`timeBeginPeriod(1)` or Multimedia Timer)
   - If polled position differs from last known position, reposition overlay immediately
   - Catches delayed or suppressed `EVENT_OBJECT_LOCATIONCHANGE` events

3. **DWM Frame Synchronization:**
   - Call `DwmFlush()` after repositioning to sync with DWM composition cycle
   - Use `DwmGetCompositionTimingInfo` for precise VBlank timestamp scheduling

4. **Performance Targets (Section 7C):**
   - `GetWindowRect` call: < 0.1ms
   - `SetWindowPos` (overlay): < 0.5ms
   - Total per-frame budget: < 16.6ms at 60Hz
   - Frame-miss rate during continuous dragging: < 1%

5. **Optimization:**
   - Suspend tracking for minimized or fully occluded windows
   - Only poll windows that are currently visible and have active overlays
   - Minimize thread synchronization overhead between the polling thread and event callbacks

6. **Instrumentation:**
   - Add high-resolution timestamp logging (QueryPerformanceCounter) for profiling
   - Measure delta between `GetWindowRect` returning a new position and `SetWindowPos` completing
   - Log frame-miss rate during drag operations

## Acceptance Criteria / Tests

- [ ] Overlay tracks a window being dragged with no visible lag at 60Hz
- [ ] Overlay tracks during window resize operations
- [ ] Polling fallback catches position changes even when location-change events are delayed
- [ ] `SetWindowPos` is called via P/Invoke (not WPF properties) — verified by code review
- [ ] Position update latency is < 2ms p99 (measured with instrumentation)
- [ ] Frame-miss rate is < 1% during continuous 5-second drag (measured with instrumentation)
- [ ] Minimized windows do not consume polling cycles
- [ ] Unit tests verify position comparison logic (changed vs. unchanged)
- [ ] Integration test: drag Notepad window across screen, visually confirm overlay follows smoothly
- [ ] Performance test: measure and log actual latencies against targets from Section 7C
