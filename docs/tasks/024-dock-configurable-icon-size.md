# Task 024: Configurable Dock Icon Size

**Priority:** High
**Status:** Pending
**Depends on:** 023 (Icon Overflow Fix)

## Summary

Add a settings service that persists dock preferences (icon size, full-width mode) to a JSON file. Icon size should be configurable from 32px to 128px (default 48px). The dock should dynamically resize when settings change.

## Requirements

### 1. DockSettingsService (`src/Harbor.Core/Services/DockSettingsService.cs`)

New service that persists dock settings to `%LOCALAPPDATA%\Harbor\dock-settings.json`.

```csharp
public class DockSettings
{
    public int IconSize { get; set; } = 48;        // 32–128, default 48
    public bool FullWidthDock { get; set; } = false; // false = content-sized (macOS-style)
}
```

- `Load()` / `Save()` methods with JSON serialization
- Thread-safe
- `SettingsChanged` event fired on save
- Handle corrupt/missing JSON gracefully (return defaults)

### 2. Dock XAML Updates

- The `DockIconTemplate` Grid dimensions, Image dimensions, and active dot positioning should be driven by a bindable icon size value, not hardcoded `48`
- Approach: Expose `IconSize` as a property on the Dock window. Use a `{Binding}` or resource for the template to reference. Alternatively, rebuild the icon template when size changes.
- Simpler approach: When settings change, update the `Width`/`Height` on the icon template programmatically by iterating rendered items, or use a shared resource value.

### 3. Dock.xaml.cs Updates

- Accept `DockSettingsService` in `Initialize()`
- Subscribe to `SettingsChanged`
- When icon size changes:
  - Update `IconDefaultSize` (make it instance, not const)
  - Recalculate `IconHoverSize` (default * 1.167) and `IconPressedSize` (default * 0.917)
  - Update the AppBar height: new height = iconSize + 8 (dot space) + 8 (padding) + 4 (margin)
  - Trigger re-layout of dock items

### 4. App.xaml.cs Wiring

- Create `DockSettingsService` instance
- Pass it to `Dock.Initialize()`

## Acceptance Criteria

- [ ] Default icon size is 48px (backward compatible)
- [ ] Changing icon size in settings JSON and restarting applies the new size
- [ ] Icons scale correctly at sizes 32, 48, 64, 96, 128
- [ ] Hover/press scale factors remain proportional
- [ ] Active indicator dot remains properly positioned at all sizes
- [ ] Settings file created at `%LOCALAPPDATA%\Harbor\dock-settings.json`
- [ ] Corrupt/missing settings file falls back to defaults
- [ ] Tests pass: `dotnet test Harbor.slnx`

## Files to Create

- `src/Harbor.Core/Services/DockSettingsService.cs`

## Files to Modify

- `src/Harbor.Shell/Dock.xaml` — parameterize icon dimensions
- `src/Harbor.Shell/Dock.xaml.cs` — dynamic sizing support
- `src/Harbor.Shell/App.xaml.cs` — wire DockSettingsService
- `tests/Harbor.Core.Tests/` — add DockSettingsServiceTests.cs
