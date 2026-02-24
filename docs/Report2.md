# Report: Apps Launcher Icon Extraction Issues

## Problem Statement

The Harbor Apps launcher (shell:AppsFolder enumeration) shows incorrect icons for many applications. Instead of the app's real icon, users see:

1. **White blank document icons** — The most common issue. The Windows "blank page" icon (white rectangle with folded corner) appears for many apps that clearly have real icons elsewhere in the OS (e.g. Start Menu, File Explorer, taskbar).
2. **Generic application icons** — Some apps show a generic colored app icon that is not the app's actual branding icon.
3. **Broken/corrupt icons** — At least one app (AIDocScanner) shows a square with a cross, indicating a broken bitmap.

These apps DO have real icons — they display correctly in the Windows Start Menu "All Apps" list, in File Explorer, and in the taskbar. The issue is specific to our extraction pipeline.

## Architecture

### Current Icon Extraction Pipeline

Located in `src/Harbor.Core/Services/ShellAppEnumerator.cs`, the pipeline is:

```
ExtractIconFromShell(IShellItem)     — IShellItemImageFactory.GetImage()
    ↓ (returns null if generic detected)
ExtractIconFallback(parsingName)     — UWP manifest or Win32 exe extraction
    ↓ (returns null if not found)
GeneratePlaceholderIcon(displayName) — Gray circle with first letter
```

### Existing Robust Pipeline (Not Connected)

`src/Harbor.Core/Services/IconExtractionService.cs` has a battle-tested multi-strategy pipeline used by the dock:

```
SHGetFileInfo(exePath)      — Shell file info icon
ExtractIconEx(exePath)      — PE resource table extraction
TryExtractUwpIcon(exePath)  — AppxManifest.xml → logo asset with scale variants
```

But this service takes **file paths** as input, not shell:AppsFolder items. The Apps launcher enumerates via COM `IShellItem` objects whose parsing names are either AUMIDs (UWP) or sometimes file paths (Win32).

## Root Cause Analysis

`IShellItemImageFactory.GetImage()` returns `S_OK` with a valid HBITMAP, but the bitmap contains the system's **per-class fallback icon** instead of the app's actual icon. Per Microsoft docs: *"If no thumbnail or icon is available for the requested item, a per-class icon may be provided from the Shell."* The API does not indicate this happened — the HRESULT is success.

This means:
- The COM call "succeeds" so our null-check passes
- The returned bitmap is a valid image (not null, not corrupt)
- But it's the wrong image — a generic icon instead of the app's real one
- Our fallback strategies never trigger because the primary call didn't fail

## What We've Tried

### Attempt 1: Basic IShellItemImageFactory at 48x48

**Code:** `imageFactory.GetImage(new SIZE(48, 48), SIIGBF.RESIZETOFIT, ...)`

**Result:** Many apps returned the blank document icon. The small size (48x48) and `RESIZETOFIT` flag caused the shell to fall back to per-class icons when it couldn't find an icon at that exact size.

### Attempt 2: BIGGERSIZEOK Fallback Flag

**Code:** Added retry with `SIIGBF.BIGGERSIZEOK` when `RESIZETOFIT` failed.

**Result:** No improvement. The issue isn't that GetImage fails — it succeeds but returns the wrong (generic) icon. `BIGGERSIZEOK` only helps when GetImage returns an error HRESULT.

### Attempt 3: Pixel-Based Generic Icon Detection

**Code:** Added `IsLikelyGenericIcon()` that counts quantized colors (4-bit per channel). If fewer than 12 unique colors, the icon is classified as generic and rejected.

**Result:** Partially effective. Caught the simple white blank document icon (very few colors). But:
- **False negatives:** Some generic icons have enough colors to pass the 12-color threshold (e.g. generic app icons with gradients or colored elements).
- **Potential false positives:** Could reject legitimate monochrome/simple app icons.
- Even when detection works, the app falls through to the placeholder instead of finding the real icon.

### Attempt 4: SIIGBF_ICONONLY Flag + 256x256 Request Size

**Code:** Changed to `imageFactory.GetImage(new SIZE(256, 256), SIIGBF.ICONONLY, ...)` with `ICONONLY | BIGGERSIZEOK` as fallback.

**Rationale:**
- `ICONONLY` (0x04) tells the shell to skip thumbnail extraction and go straight to the icon handler, avoiding per-class thumbnail fallbacks.
- 256x256 requests the highest resolution available, since some apps only register high-res icons and fall back to generic at smaller sizes.

**Result:** No visible improvement. The shell still returns the same generic per-class icons regardless of the `ICONONLY` flag or requested size. The underlying issue is in the shell namespace extension backing `shell:AppsFolder`, not in the flags.

### Attempt 5: UWP Fallback via AUMID → PackageManager → AppxManifest

**Code:** Added `TryExtractUwpIcon(aumid)` that:
1. Parses the AUMID to get PackageFamilyName (text before `!`)
2. Uses `Windows.Management.Deployment.PackageManager.FindPackagesForUser("")` to find the package
3. Reads `AppxManifest.xml` from the install directory
4. Loads the `Square44x44Logo` or `Square150x150Logo` asset
5. Searches for scaled variants (`.targetsize-N.png`, `.scale-N.png`)

**Result:** Limited improvement. Works for some UWP apps, but:
- Some packages throw `FileNotFoundException` when accessing `InstalledLocation.Path` (orphaned/framework packages)
- Only triggers when `IsLikelyGenericIcon` detects the icon as generic — if the generic icon passes the color threshold, this fallback never runs
- Only applies to UWP apps (parsing name contains `!`), not Win32 apps

### Attempt 6: Win32 Fallback via SHGetFileInfo / ExtractIconEx

**Code:** Added `TryExtractWin32Icon(exePath)` that:
1. Resolves `.lnk` targets via `IShellLinkW` + `IPersistFile` COM
2. Tries `ExtractIconEx` (PE resource table) for the resolved exe
3. Falls back to `SHGetFileInfo` for shell-associated icon

**Result:** Limited improvement. Many shell:AppsFolder parsing names for Win32 apps are NOT simple file paths — they can be:
- Start Menu shortcut references that don't resolve to a real path
- Indirect references through the shell namespace
- The fallback only triggers when the primary detection catches the generic icon

## Fundamental Issues

1. **Detection gap:** `IsLikelyGenericIcon` is a heuristic that misses generic icons with more than 12 quantized colors. The generic "application" icon with gradients/shadows passes easily.

2. **Silent fallback:** The shell API provides no way to distinguish "here's the app's real icon" from "here's a generic fallback because I couldn't find the real one." The HRESULT is the same.

3. **Parsing name mismatch:** The shell:AppsFolder parsing names (AUMIDs for UWP, indirect references for Win32) don't directly map to file paths needed by `SHGetFileInfo`/`ExtractIconEx`/`TryExtractUwpIcon`. There's a translation gap.

4. **Fallback chain ordering:** Our fallbacks only activate when the generic icon detector fires. If detection misses, the generic icon is used as-is and no fallback is attempted.

## Unexplored Approaches

### IExtractIcon via IShellFolder.GetUIObjectOf (High confidence, high effort)

The shell's own internal icon extraction mechanism. `GetIconLocation()` returns the actual icon resource path and index, AND flags like `GIL_SIMULATEDOC` / `GIL_DEFAULTICON` that **explicitly indicate** when a generic icon is being provided. This would solve the silent-fallback detection problem entirely.

Requires: New COM interface declarations for `IShellFolder`, `IEnumIDList`, `IExtractIconW`, and PIDL management.

### System.AppUserModel.RelaunchIconResource Property

Query `IShellItem2.GetString()` with the `System.AppUserModel.RelaunchIconResource` PROPERTYKEY to get the icon resource path directly from the shell item's property store. This is what the taskbar uses.

Requires: `IShellItem2` COM interface declaration and PROPERTYKEY definitions.

### PropertyStore → System.Link.TargetParsingPath

For Win32 apps, query the shell item's property store for `System.Link.TargetParsingPath` to get the actual .exe path, then use existing extraction strategies on that path.

Requires: Same `IShellItem2` + PROPERTYKEY infrastructure.

### Compare Against Known Generic Icon Hashes

Pre-capture the pixel data of Windows' known generic icons (blank document from shell32.dll index 0, generic app from shell32.dll index 2, etc.) at startup. During extraction, compare the returned bitmap against these known hashes instead of using the fragile color-count heuristic.

Requires: One-time capture of reference icons, hash comparison function.

### Always Run Fallback Chain

Instead of only running UWP/Win32 fallbacks when the generic detector fires, always attempt the fallback chain and prefer its result if it succeeds. Use the IShellItemImageFactory result only as a last resort.

Requires: Inverting the current pipeline order (fallbacks first, shell factory last).

## Files Involved

| File | Role |
|------|------|
| `src/Harbor.Core/Services/ShellAppEnumerator.cs` | Shell:AppsFolder enumeration + icon extraction pipeline |
| `src/Harbor.Core/Services/IconExtractionService.cs` | Dock icon extraction (file-path based, battle-tested) |
| `src/Harbor.Core/Services/InstalledAppService.cs` | Orchestrates scanning, caching, and watching for changes |
| `src/Harbor.Core/Models/AppInfo.cs` | Data model (DisplayName, ExecutablePath, Icon) |
| `src/Harbor.Shell/AppsLauncherWindow.xaml` | Apps launcher UI (displays icons at 56x56) |
