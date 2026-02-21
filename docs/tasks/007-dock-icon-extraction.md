# Task 007: Dock — Icon Extraction

**Priority:** 4 (Enhancement)
**Depends on:** 002, 006
**Blocks:** None directly

## Objective

Extract high-resolution application icons for display in the Dock, using multiple fallback strategies to ensure all applications show meaningful icons.

## Technical Reference

Refer to `docs/Design.md` Sections 3B (Interaction — icon extraction) and 5B (Icon size: 48x48 DIP) for icon extraction strategy.

## Requirements

1. **Primary Extraction — `SHGetFileInfo`:**
   - Given a process/window handle, resolve the executable path
   - Call `SHGetFileInfo` with `SHGFI_ICON | SHGFI_LARGEICON` to extract the application icon
   - Convert the `HICON` to a WPF `ImageSource` for display

2. **Fallback — Resource Table Extraction:**
   - If `SHGetFileInfo` returns a generic/default Windows icon, fall back to direct icon extraction from the executable's resource table using `ExtractIconEx` or `LoadImage`
   - Select the highest resolution icon available (prefer 256x256 or 128x128 for sharp rendering at 48 DIP)

3. **UWP/Store App Icons:**
   - UWP apps may not have a traditional .exe icon. Resolve the app's `AppxManifest.xml` to find the `Square44x44Logo` or `Square150x150Logo` asset path
   - Load the PNG asset directly

4. **Caching:**
   - Cache extracted icons keyed by executable path
   - Invalidate cache entry if the executable's last-modified timestamp changes

5. **Default Fallback:**
   - If all extraction methods fail, display a generic application icon (bundled with Harbor)

## Acceptance Criteria / Tests

- [ ] Win32 apps (Notepad, Paint) show their correct icons at 48x48 DIP
- [ ] Electron apps (VS Code, Discord) show their correct icons, not a generic Electron icon
- [ ] UWP apps (Calculator, Settings) show their correct icons
- [ ] A missing or corrupt executable path results in the default fallback icon, not a crash
- [ ] Icons are cached and a second request for the same app does not re-extract
- [ ] Unit tests verify icon extraction returns a non-null `ImageSource` for known executables
- [ ] Unit tests verify fallback chain triggers in correct priority order
