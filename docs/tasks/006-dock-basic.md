# Task 006: Dock — Basic Structure & Task Enumeration

**Priority:** 3 (Core Component)
**Depends on:** 002, 003
**Blocks:** 007, 008, 012, 019

## Objective

Implement the bottom-docked Dock bar as a WPF AppBar window that displays icons for running applications, sourced from ManagedShell's `TaskService`.

## Technical Reference

Refer to `docs/Design.md` Sections 3B (The Dock), 5B (Dock UI Spec), and 5E (Dock frosted glass, Dock magnification) for layout and behavior specifications.

## Requirements

1. **AppBar Window:**
   - Create a WPF Window registered as a bottom-docked AppBar via ManagedShell
   - Bar height: 62 DIP (includes padding)
   - Centered horizontally on primary monitor
   - Bottom margin: 4px from screen edge
   - Corner radius: 16px on the Dock container
   - Borderless, transparent, topmost

2. **Background Material:**
   - Acrylic blur: `#1E1E1E` at 50% opacity with 40px blur
   - Border: 1px solid `#FFFFFF` at 12% opacity
   - (Light mode in Task 018)

3. **Task Enumeration:**
   - Bind to ManagedShell's `TaskService`
   - Display an icon for each running application window
   - React to `EVENT_OBJECT_CREATE` and `EVENT_OBJECT_DESTROY` to add/remove icons dynamically

4. **Icon Display:**
   - Default icon size: 48x48 DIP
   - 8px vertical padding, 4px horizontal spacing between icons
   - Dock width adjusts dynamically based on number of icons

5. **Separator:**
   - 1px wide vertical line between pinned and running (unpinned) apps
   - `#FFFFFF` at 20% opacity, 24 DIP tall, centered vertically
   - (Pinned apps persistence is part of Task 012)

6. **Basic Click Behavior:**
   - Single click on a running app icon: activate/bring to front that window
   - If the window is already focused: minimize it

## Acceptance Criteria / Tests

- [ ] Dock renders as a centered bar at the bottom of the primary monitor with 16px corner radius
- [ ] AppBar reservation prevents other windows from overlapping the Dock
- [ ] Opening a new application adds its icon to the Dock within 500ms
- [ ] Closing an application removes its icon from the Dock
- [ ] Clicking a Dock icon brings the corresponding window to the foreground
- [ ] Clicking an already-focused app's Dock icon minimizes it
- [ ] Acrylic blur background renders (or fallback to solid color)
- [ ] Unit tests verify task list binding and icon count updates
- [ ] Unit tests verify click-to-activate and click-to-minimize logic
