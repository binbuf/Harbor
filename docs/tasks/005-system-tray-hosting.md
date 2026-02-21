# Task 005: System Tray Hosting

**Priority:** 3 (Core Component)
**Depends on:** 003, 004
**Blocks:** None directly (enhances Top Menu)

## Objective

Integrate ManagedShell's `TrayService` into the top menu bar to display system tray notification icons on the right side, replicating the macOS-style status area.

## Technical Reference

Refer to `docs/Design.md` Sections 3A (Right Side — System Tray), 5A (Right Side — System Tray & Clock), and 5E (System Tray Icons mapping).

## Requirements

1. **Tray Icon Display:**
   - Use ManagedShell's `TrayService` to enumerate and host `Shell_NotifyIcon` items
   - Render icons at 18x18 DIP
   - 8px spacing between icons, 8px right margin
   - Position icons to the left of the clock on the top menu bar

2. **Tray Icon Interaction:**
   - Left click: forward to the owning application's notification callback
   - Right click: forward to the owning application's context menu callback
   - Tooltip: display the icon's tooltip text on hover

3. **Dynamic Updates:**
   - React to tray icon additions, removals, and icon changes in real time
   - Handle icon balloon notifications

4. **Layout Integration:**
   - Icons flow right-to-left from the clock area
   - Control Center chevron glyph `›` positioned 10px left of clock (visual placeholder only)

## Acceptance Criteria / Tests

- [ ] All standard Windows system tray icons (network, volume, battery, etc.) appear in the menu bar
- [ ] Icons render at 18x18 DIP with correct spacing
- [ ] Left-clicking a tray icon activates the expected application behavior (e.g., volume mixer)
- [ ] Right-clicking a tray icon shows the expected context menu
- [ ] Adding/removing tray icons dynamically updates the display
- [ ] Integration test confirms `TrayService` enumerates at least 1 icon on a standard Windows install
- [ ] Unit tests verify icon layout calculation (positioning, spacing)
