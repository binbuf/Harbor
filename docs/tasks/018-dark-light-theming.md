# Task 018: Dark Mode / Light Mode Theming

**Priority:** 6 (Polish)
**Depends on:** 004, 006
**Blocks:** None directly

## Objective

Implement dynamic dark/light mode theming for all Harbor shell chrome (Top Menu, Dock, overlays) that follows the Windows system theme preference.

## Technical Reference

Refer to `docs/Design.md` Sections 5A (menu bar colors), 5B (dock colors), 5C (traffic light inactive colors), 9B (Dark Mode / Light Mode registry key), and 5E (Dark/Light mode mapping).

## Requirements

1. **Theme Detection:**
   - Read registry key `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`
   - `0` = dark mode, `1` = light mode
   - Subscribe to registry key changes for real-time theme switching

2. **WPF Resource Dictionaries:**
   - Create `DarkTheme.xaml` and `LightTheme.xaml` resource dictionaries
   - Swap active dictionary when theme changes
   - Define all color resources as named brushes for consistent binding

3. **Top Menu Bar Theme Values:**
   | Property | Dark | Light |
   |----------|------|-------|
   | Background | `#1E1E1E` @ 80% | `#F6F6F6` @ 80% |
   | Text | `#FFFFFF` | `#000000` |
   | Bottom border | `#3A3A3A` | `#D1D1D1` |
   | Hover highlight | `#FFFFFF` @ 10% | `#000000` @ 8% |

4. **Dock Theme Values:**
   | Property | Dark | Light |
   |----------|------|-------|
   | Background | `#1E1E1E` @ 50% | `#F6F6F6` @ 50% |
   | Border | `#FFFFFF` @ 12% | `#000000` @ 8% |
   | Separator | `#FFFFFF` @ 20% | `#000000` @ 12% |
   | Active dot | `#FFFFFF` | `#000000` |

5. **Title Bar Color Cache Invalidation:**
   - On theme change, invalidate all cached title bar colors (Task 014)
   - Re-query title bar colors for all active overlays

## Acceptance Criteria / Tests

- [ ] Harbor starts in the correct theme matching the current Windows setting
- [ ] Toggling Windows dark/light mode updates Harbor's UI in real time (within 1 second)
- [ ] All color values match the spec for both dark and light modes
- [ ] Acrylic blur opacity values differ correctly between Top Menu (80%) and Dock (50%)
- [ ] Title bar color masks update when theme changes
- [ ] Unit tests verify registry key reading and interpretation
- [ ] Unit tests verify resource dictionary swap logic
- [ ] Unit tests verify all color values match Design.md spec for both themes
