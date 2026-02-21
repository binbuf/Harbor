# Task 014: Title Bar Color Detection & Mask

**Priority:** 5 (Core Feature)
**Depends on:** 002, 009, 010
**Blocks:** None directly

## Objective

Draw a solid color block on the right side of the overlay to obscure the native minimize/maximize/close buttons, with the color matched to the application's title bar background so the mask is invisible to the user.

## Technical Reference

Refer to `docs/Design.md` Section 9A (Detection Strategy — Cascading) and Section 9B (Dark Mode / Light Mode) for the full color detection pipeline.

## Requirements

1. **Right-Side Mask Rendering:**
   - On the overlay window, draw a filled rectangle on the right side covering the native window control buttons
   - Size the mask to cover the native button area (typically ~135 DIP wide for the three buttons)
   - Use `DwmGetWindowAttribute(DWMWA_CAPTION_BUTTON_BOUNDS)` to get the exact native button region width

2. **Color Detection — Cascading Strategy:**

   **Priority 1 — DwmGetColorizationColor:**
   - Query system-wide DWM colorization color
   - Works for all apps using the default Windows title bar

   **Priority 2 — Mica/Acrylic Detection:**
   - Query `DwmGetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE)` to detect Mica or Acrylic
   - Render a matching translucent block

   **Priority 3 — Pixel Sampling:**
   - For custom-chrome apps (VS Code, Spotify, Discord, Slack)
   - Capture an 8x8 pixel region at the right edge of the title bar
   - Compute the median color and use for the mask
   - Cache result per-process

   **Priority 4 — User Override:**
   - Store per-application color overrides in Harbor settings (JSON config)
   - Allow users to manually specify title bar color for problematic apps

3. **Cache Management:**
   - Cache detected colors keyed by process name
   - Invalidate on `WM_THEMECHANGED`
   - Invalidate on system dark/light mode toggle (registry key change — Section 9B)

## Acceptance Criteria / Tests

- [ ] Native window buttons (min/max/close) are fully hidden behind the color mask
- [ ] Mask color matches the title bar for Notepad (Win32 — uses DWM colorization)
- [ ] Mask color matches for Windows Terminal (Mica material)
- [ ] Mask color matches for VS Code (custom chrome — pixel sampling)
- [ ] Changing Windows dark/light mode updates the mask color
- [ ] User override in config file correctly applies a custom mask color
- [ ] Unit tests verify the cascading priority logic (mock each detection method)
- [ ] Unit tests verify pixel sampling median color calculation
- [ ] Unit tests verify cache invalidation on theme change
