# Task 012: Dock — Pinning & Context Menu

**Priority:** 5 (Enhancement)
**Depends on:** 006
**Blocks:** None directly

## Objective

Add application pinning support and right-click context menus to the Dock, allowing users to persist favorite applications and manage running apps.

## Technical Reference

Refer to `docs/Design.md` Section 5B (Separator, Right-Click Context Menu) for context menu items and pinned/running app separation.

## Requirements

1. **Pinned Applications:**
   - Allow users to pin applications to the Dock
   - Pinned apps persist across Harbor restarts (store in a local config file, e.g., JSON at `%LOCALAPPDATA%\Harbor\dock-pins.json`)
   - Pinned apps appear to the left of the separator; running unpinned apps appear to the right
   - Clicking a pinned app that isn't running launches it

2. **Separator:**
   - Render a 1px vertical line between pinned and running sections
   - `#FFFFFF` at 20% opacity, 24 DIP tall, centered vertically
   - Only visible when both pinned and running unpinned apps exist

3. **Right-Click Context Menu:**
   - **Pinned apps:** "Open", "Remove from Dock", separator, "Options ▸" submenu ("Keep in Dock", "Open at Login")
   - **Running apps:** "Open", separator, "Options ▸" submenu ("Keep in Dock", "Open at Login"), separator, "Quit"
   - Style: Acrylic background matching system context menu

4. **"Quit" Action:**
   - Sends a close message to the application's main window
   - If the app doesn't close within 3 seconds, offer to force-kill

5. **"Open at Login":**
   - Toggle creating/removing a shortcut in the user's Startup folder (`shell:startup`)

## Acceptance Criteria / Tests

- [ ] Pinning an app via context menu persists it across Harbor restarts
- [ ] Unpinning removes the app from the pinned section
- [ ] Pinned app icons appear to the left of the separator
- [ ] Clicking a pinned app that isn't running launches the application
- [ ] Right-click context menu appears with all specified items
- [ ] "Quit" successfully closes a running application
- [ ] "Open at Login" creates a startup shortcut; toggling off removes it
- [ ] Unit tests verify pin/unpin persistence (mock file I/O)
- [ ] Unit tests verify context menu item generation based on pinned vs. running state
