# Task 004: Top Menu Bar (System Bar)

**Priority:** 3 (Core Component)
**Depends on:** 002, 003
**Blocks:** 005, 018

## Objective

Implement the persistent top menu bar as a WPF AppBar window. This bar displays the active application name on the left and a clock on the right. System tray and theming are handled in separate tasks.

## Technical Reference

Refer to `docs/Design.md` Sections 3A (The Top Menu), 5A (Top Menu Bar UI Spec), and 5E (macOS → Windows Visual Mapping) for layout, dimensions, and material specifications.

## Requirements

1. **AppBar Window:**
   - Create a WPF Window registered as a top-docked AppBar via ManagedShell (from Task 003)
   - Height: 24 DIP (Section 5A)
   - Span full width of primary monitor
   - Set `Topmost = true`, borderless, non-resizable

2. **Background Material:**
   - Apply Acrylic blur background using `SetWindowCompositionAttribute` with `ACCENT_ENABLE_ACRYLICBLURBEHIND`
   - Dark mode default: `#1E1E1E` at 80% opacity with 30px blur
   - Bottom border: 1px solid `#3A3A3A`
   - (Light mode handled in Task 018)

3. **Left Side — App Name:**
   - Harbor icon: 16x16 DIP, 8px left padding
   - Monitor foreground window using `GetForegroundWindow()` + `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` from Task 002
   - Display active application name in bold (600 weight)
   - Font: Segoe UI Variable, 13px
   - Placeholder menu items (File, Edit, View) — non-functional, regular weight (400), 16px horizontal padding

4. **Right Side — Clock:**
   - Right-aligned, 12px right padding
   - Format: `h:mm AM/PM` following Windows regional settings
   - Update every minute via timer

5. **Interaction States:**
   - Hover: rounded rectangle highlight at 10% white opacity, 4px corner radius, 120ms ease-out fade
   - Pressed: 20% opacity, immediate
   - Disabled/inactive: 50% opacity text

## Acceptance Criteria / Tests

- [ ] Top menu bar renders as a 24 DIP tall bar across the full width of the primary monitor
- [ ] AppBar reservation prevents other windows from overlapping the menu bar
- [ ] Active application name updates within 100ms of switching foreground windows
- [ ] Clock displays correct time and updates each minute
- [ ] Acrylic blur background is visible (or graceful fallback to solid color if API unavailable)
- [ ] Hover states render correctly with proper timing on menu item placeholders
- [ ] Unit tests verify foreground window name extraction logic
- [ ] Unit tests verify clock formatting follows regional settings
