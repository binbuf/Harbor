# Task 023: Fix Dock Icon Overflow

**Priority:** Critical
**Status:** Done
**Depends on:** 006 (Dock Basic)

## Summary

App icons in the dock are clipped at the bottom — they overflow out of view. The active indicator dot (4px, positioned at `Margin="0,0,0,-2"`) extends below the icon grid, and the overall container padding may be insufficient for 48px icons + dots within the 62 DIP bar height.

## Root Cause Analysis

In `Dock.xaml`, the `DockIconTemplate`:
- Grid is `Width="48" Height="48"`
- Active dot is `VerticalAlignment="Bottom" Margin="0,0,0,-2"` — the **negative margin** pushes the dot 2px BELOW the grid boundary
- The `DockContainer` has `Padding="4,8"` (4px horizontal, 8px vertical)

Math: 8px top padding + 48px icon + 8px bottom padding = 64 DIP. But the dot at -2 margin needs 2px more = 66 DIP minimum. The `DockRoot` is only 62 DIP high. The dot is clipped.

## Solution

1. **Fix the indicator dot position**: Remove the negative margin. Instead, make the icon Grid taller to accommodate the dot. Change Grid Height from 48 to 56 (48px icon + 4px gap + 4px dot). Position the dot inside the grid with proper positive margin.

2. **Adjust padding**: Change `DockContainer` `Padding` from `"4,8"` to `"4,4"` since the icon grid now includes its own vertical space for the dot.

3. **Adjust DockRoot height**: Increase from 62 to 68 DIP to fit: 4px top padding + 56px icon grid + 4px bottom padding + 4px bottom margin = 68 DIP.

4. **Update AppBar registration**: In `App.xaml.cs`, change the dock's `desiredHeight` from `62` to `68`.

5. **Update auto-hide slide distances**: In `Dock.xaml.cs`, the show/hide animations use hardcoded `62` for the slide distance. Update to match new height.

### DockIconTemplate Changes

```xml
<Grid Width="48" Height="56" ...>
    <!-- Application icon -->
    <Image Source="{Binding Icon}"
           Width="48" Height="48"
           VerticalAlignment="Top"
           RenderOptions.BitmapScalingMode="HighQuality" />
    <!-- Active indicator dot -->
    <Ellipse Width="4" Height="4"
             Fill="{DynamicResource DockActiveDotBrush}"
             HorizontalAlignment="Center"
             VerticalAlignment="Bottom"
             Margin="0,0,0,0" />
</Grid>
```

## Acceptance Criteria

- [ ] Icons are fully visible within the dock (no clipping)
- [ ] Active indicator dots visible below icons
- [ ] Hover/press animations still work correctly
- [ ] Bounce animation still works
- [ ] Auto-hide show/hide slide distances match new height
- [ ] Tests pass: `dotnet test Harbor.slnx`
- [ ] Update `DockLayoutTests` if icon grid size assumptions changed

## Files to Modify

- `src/Harbor.Shell/Dock.xaml` — icon template, container padding, root height
- `src/Harbor.Shell/Dock.xaml.cs` — auto-hide slide distances
- `src/Harbor.Shell/App.xaml.cs` — dock AppBar desired height
- `tests/Harbor.Shell.Tests/DockLayoutTests.cs` — update size constants
- `tests/Harbor.Shell.Tests/DockAnimationTests.cs` — may need slide distance updates
