# Task 011: Traffic Light Window Control Buttons

**Priority:** 5 (Core Feature)
**Depends on:** 010
**Blocks:** 013

## Objective

Implement the macOS-style traffic light buttons (Close, Minimize, Maximize) as interactive WPF controls rendered on the overlay window's left side.

## Technical Reference

Refer to `docs/Design.md` Sections 4A Step 5 (Click Routing), 5C (Traffic Light Window Controls — full visual spec), and 5E (WPF Ellipse + Path implementation).

## Requirements

1. **Button Layout:**
   - Three circular buttons: Close (red), Minimize (yellow), Maximize (green)
   - Button diameter: 12 DIP
   - Spacing: 8 DIP between buttons (center-to-center: 20 DIP)
   - Padding: 8 DIP from overlay left edge to first button center
   - Vertically centered within the title bar overlay

2. **Button Rendering:**
   - Each button is a WPF `Ellipse` with `Fill` bound to state
   - Glyphs (×, −, +/⤢) rendered as `Path` geometries (vector, not font) — Section 5C
   - Glyph stroke width: 1.5 DIP

3. **Button Colors (Default / Active Window):**
   - Close: `#FF5F57` (default), `#E0443E` (pressed)
   - Minimize: `#FEBC2E` (default), `#D4A528` (pressed)
   - Maximize: `#28C840` (default), `#1AAB29` (pressed)

4. **Interaction States:**
   - **Default:** Solid colored circles, no glyphs visible
   - **Hover (any button):** ALL three buttons simultaneously show glyphs. Glyph colors: `#4D0000` (close), `#6B4400` (minimize), `#003D00` (maximize). Glyph fade-in: 80ms ease-out
   - **Pressed:** Fill shifts to pressed color, glyph remains
   - **Inactive window:** All three buttons display as `#CDCDCD`, no glyphs. On hover, colors and glyphs restore

5. **Click Routing (placeholder):**
   - Wire up click events to fire commands (actual window commands routed in Task 013)
   - For this task, log button clicks with the target HWND and action type

## Acceptance Criteria / Tests

- [ ] Three colored circles render on the left side of the overlay at correct positions
- [ ] Hovering over any one button causes all three to simultaneously show their glyphs
- [ ] Glyph fade-in animation completes in ~80ms
- [ ] Pressing a button changes its fill to the pressed color
- [ ] Inactive windows show all buttons as `#CDCDCD`
- [ ] Hovering over inactive buttons restores their active colors
- [ ] Buttons are rendered as vector paths, not fonts (verify Path geometry data exists)
- [ ] Unit tests verify button positioning math (spacing, centering)
- [ ] Unit tests verify color state transitions (default → hover → pressed → inactive)
