# Task 009: Title Bar Discovery (UIA + NONCLIENT Fallback)

**Priority:** 4 (Overlay Foundation)
**Depends on:** 002, 008
**Blocks:** 010, 011, 014

## Objective

Implement the title bar discovery pipeline that locates the exact bounding rectangle of any application's title bar, using UI Automation as the primary method and NONCLIENT heuristics as fallback.

## Technical Reference

Refer to `docs/Design.md` Sections 4A Steps 1–2 (UIA Tracking Pipeline), 6A (Per-Application UIA Tree Variability), and the NONCLIENT Fallback Algorithm for the full discovery strategy.

## Requirements

1. **UIA Primary Discovery:**
   - Use `UIAutomationClient` API to walk the target window's automation element tree
   - Locate the element with `ControlType.TitleBar`
   - Read `BoundingRectangle` to get exact (X, Y, Width, Height) of the native title bar
   - Set a 100ms timeout on UIA queries to handle hung applications (Section 11B)

2. **Framework Detection:**
   - Detect the UI framework of the target window using `FrameworkId` from UIA or process name heuristics
   - Apply framework-specific strategies per Section 6A:
     - **Win32/WPF:** Primary UIA path
     - **UWP/WinUI 3:** Fallback to `DwmGetWindowAttribute(DWMWA_CAPTION_BUTTON_BOUNDS)` for zero-height title bars
     - **Electron:** Detect `FrameworkId = 'Chrome'` or `electron.exe` process; use NONCLIENT heuristic; skip if frameless
     - **Java:** Detect `java.exe`/`javaw.exe`; fallback to `GetSystemMetrics(SM_CYCAPTION)`
     - **Qt:** NONCLIENT fallback; maintain allow/deny list

3. **NONCLIENT Fallback Algorithm:**
   - When UIA fails: compute `titleBarHeight = windowRect.Top - clientRect.Top` using `GetWindowRect` / `GetClientRect`
   - If computed height is zero or negative, the window uses custom chrome — add to session-scoped skip list
   - Full rectangle: `(windowRect.Left, windowRect.Top, windowRect.Width, computedHeight)`

4. **Skip List Management:**
   - Maintain a session-scoped set of HWNDs that should not receive overlays
   - Windows are added when NONCLIENT height is zero/negative
   - Entries expire when the HWND is destroyed

5. **Result Caching:**
   - Cache discovered title bar rectangles keyed by HWND
   - Invalidate on `EVENT_OBJECT_LOCATIONCHANGE` (position changed) or window style change

## Acceptance Criteria / Tests

- [ ] UIA discovery correctly finds the title bar `BoundingRectangle` for Notepad (Win32)
- [ ] UIA discovery works for a WPF app (e.g., Visual Studio)
- [ ] NONCLIENT fallback triggers for Electron apps (e.g., VS Code) and returns a valid rectangle
- [ ] Frameless windows (NONCLIENT height = 0) are added to the skip list, not overlayed
- [ ] UIA queries timeout gracefully after 100ms for hung applications
- [ ] Framework detection correctly identifies at least Win32, Electron, and Java processes
- [ ] Unit tests verify NONCLIENT fallback calculation with mock `RECT` values
- [ ] Unit tests verify skip list add/remove/expiry logic
- [ ] Integration tests run against at least 3 different application frameworks
