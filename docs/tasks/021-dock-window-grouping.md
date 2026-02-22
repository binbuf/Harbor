# Task 021: Dock Window Grouping

**Priority:** Critical
**Status:** Pending
**Depends on:** 006 (Dock Basic), 012 (Dock Pinning & Context Menu)

## Summary

Group multiple windows of the same application under a single dock icon, matching macOS behavior. Currently each `ApplicationWindow` from ManagedShell creates a separate `DockItem`, so running two Explorer windows shows two icons.

## Requirements

### 1. DockItem Model Changes (`DockItem` in `DockItemManager.cs`)

- Replace single `Window` property with `List<ApplicationWindow> Windows { get; }`
- Add `ActiveWindow` property: the most recently focused window (for tooltip, display name)
- `IsRunning` should be true if `Windows.Count > 0`
- `DisplayName` should show the app name (from exe filename or pinned display name), NOT the individual window title

### 2. DockItemManager Grouping Logic

- In `RebuildRunningItems()`: group `_tasks.GroupedWindows` by `WinFileName` (case-insensitive)
- For each unique executable, create ONE `DockItem` with all matching windows in its `Windows` list
- In `RebuildPinnedItems()`: when finding running windows for a pinned app, collect ALL matching windows (not just the first via `FindRunningWindow`)
- Replace `FindRunningWindow()` with `FindRunningWindows()` returning `List<ApplicationWindow>`

### 3. Click Behavior (`HandleDockIconClick` in `Dock.xaml.cs`)

- When clicking a running grouped icon: bring ALL windows to front, with the most recently active window on top
- Use `BringToFront()` on each window in reverse-recency order, then the most recent last (so it ends up on top)
- When the app is already focused (active window belongs to this group): minimize all windows in the group

### 4. Context Menu — Window List (`DockContextMenuService.cs` + `Dock.xaml.cs`)

- Add a new `DockMenuAction.SwitchToWindow` action
- When a grouped icon has multiple windows, add individual window titles at the TOP of the context menu, before other items
- Each window title menu item should bring that specific window to front when clicked
- Add a separator between window list and the rest of the menu
- `DockMenuItem` needs an optional `WindowHandle` (IntPtr) property for `SwitchToWindow` action

### 5. Tooltip

- Tooltip should show the application name (e.g., "File Explorer"), not individual window title
- When hovering, if multiple windows exist, optionally show count: "File Explorer (2 windows)"

## Acceptance Criteria

- [ ] Two instances of the same app show as ONE dock icon
- [ ] Clicking the grouped icon brings all windows to front (most recent on top)
- [ ] Clicking when already focused minimizes all windows
- [ ] Right-click context menu lists individual window titles at the top
- [ ] Clicking a window title in context menu brings that specific window to front
- [ ] Running indicator dot appears for grouped items
- [ ] Pinned apps with multiple running instances show as one icon with running indicator
- [ ] Tests pass: `dotnet test Harbor.slnx`

## Files to Modify

- `src/Harbor.Core/Services/DockItemManager.cs` — grouping logic + DockItem model
- `src/Harbor.Core/Services/DockContextMenuService.cs` — window list in menu
- `src/Harbor.Shell/Dock.xaml.cs` — click handling for groups
- `tests/Harbor.Core.Tests/DockContextMenuServiceTests.cs` — update tests
- `tests/Harbor.Shell.Tests/DockLayoutTests.cs` — may need updates
