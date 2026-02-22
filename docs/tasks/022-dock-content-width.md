# Task 022: Content-Sized Dock (Not Full Width)

**Priority:** Critical
**Status:** Pending
**Depends on:** 006 (Dock Basic)

## Summary

The dock currently appears full-width because the acrylic effect is applied to the entire AppBar window via `CompositionInterop.EnableAcrylic()` on the window HWND. While the `DockContainer` border is centered with auto-width, the window-level acrylic makes the entire bar visible. The dock should be content-sized like macOS — a floating pill that only spans the width of its icons.

## Root Cause

In `Dock.xaml.cs`, `ApplyAcrylic()` calls `CompositionInterop.EnableAcrylic(new HWND(hwnd), acrylicColor)` which applies acrylic blur to the **entire AppBar window** (full screen width). The `DockContainer` border with `HorizontalAlignment="Center"` is correctly auto-sized, but the acrylic renders behind the full window.

## Solution

Remove window-level acrylic. Instead, keep the window fully transparent and achieve the frosted glass look on just the `DockContainer` using the WPF `Border.Background` with a semi-transparent brush. The acrylic/blur won't be pixel-perfect (no blur), but the semi-transparent background already defined in the theme dictionaries (`DockBackground: #801E1E1E` / `#80F6F6F6`) provides the right visual. The `DockContainer` already has `Background="{DynamicResource DockBackground}"` and `BorderBrush`, `CornerRadius`, etc.

### Changes

1. **`Dock.xaml.cs`**: Remove `ApplyAcrylic()` call from `OnSourceInitialized`. Remove `ApplyThemedAcrylic()` method (or make it a no-op). The DockContainer's `{DynamicResource DockBackground}` already provides the semi-transparent fill.

2. **`App.xaml.cs`**: Remove `_dock?.ApplyThemedAcrylic(theme)` call in `OnThemeChanged` (the DynamicResource will auto-update when the theme dictionary swaps).

3. **`Dock.xaml`**: Ensure `DockContainer` has correct background. It already does via `{DynamicResource DockBackground}`.

4. **Bottom margin / floating**: The `DockContainer` already has `Margin="0,0,0,4"` for 4px bottom gap — this matches the macOS floating dock look. Verify this is visually correct.

## Acceptance Criteria

- [ ] Dock appears as a centered, content-width pill (not full screen width)
- [ ] Semi-transparent background visible on the DockContainer only
- [ ] Rounded corners (16px) visible
- [ ] Border visible (1px themed)
- [ ] Theme switching still works (DynamicResource swaps)
- [ ] Auto-hide animations still work
- [ ] Tests pass: `dotnet test Harbor.slnx`

## Files to Modify

- `src/Harbor.Shell/Dock.xaml.cs` — remove window-level acrylic
- `src/Harbor.Shell/App.xaml.cs` — remove acrylic reapply on theme change for dock
