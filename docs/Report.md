# Protocol URI Activation Without Explorer Shell — Investigation Report

## What Is Harbor?

Harbor is a **Windows 11 shell replacement** that provides a macOS-style desktop experience. It replaces the standard Windows taskbar and Start menu with a top menu bar and a bottom dock, built with WPF/.NET 10 and [ManagedShell](https://github.com/cairoshell/ManagedShell).

On startup, when the `ReplaceExplorer` setting is enabled (default: `true`), Harbor **kills `explorer.exe`** by posting `WM_QUIT` to its `Shell_TrayWnd` window. This prevents Windows from auto-restarting explorer (unlike `taskkill`). On exit or crash, Harbor restarts explorer to restore the normal desktop.

```csharp
// App.xaml.cs — How Harbor kills the explorer shell on startup
private static void KillExplorer()
{
    const uint WM_QUIT = 0x0012;
    var trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
    if (trayWnd != default)
    {
        WindowInterop.PostMessage(trayWnd, WM_QUIT, 0, 0);
        // Wait for explorer to exit
        foreach (var proc in Process.GetProcessesByName("explorer"))
        {
            proc.WaitForExit(3000);
            proc.Dispose();
        }
    }
}
```

After killing explorer, Harbor manually reserves screen work area for its AppBars via `SystemParametersInfo(SPI_SETWORKAREA)` because the standard `SHAppBarMessage` API requires a live explorer shell.

## The Problem

Harbor has a **Windows logo menu** (like macOS's Apple menu) with actions like "About This PC", "System Settings...", and "App Store...". These trigger protocol URIs:

| Menu item | URI |
|---|---|
| About This PC | `ms-settings:about` |
| System Settings... | `ms-settings:` |
| App Store... | `ms-windows-store:` |

**When explorer.exe is dead, none of these URIs can be activated.** The user sees either:
- No visible effect at all (silent failure)
- A ~30-second hang followed by `"explorer.exe file system error (-2147219200)"` dialog

The error code `-2147219200` is `0x80040900` — a `FACILITY_ITF` error from the shell's protocol handler infrastructure, indicating there is no registered shell to route the URI.

## Root Cause

**All Windows protocol URI activation mechanisms depend on the explorer shell process being alive.** Explorer.exe is not just a file manager — it hosts the shell namespace, the protocol handler registry's runtime resolution, and the COM activation infrastructure that routes `ms-settings:`, `ms-windows-store:`, and similar URIs to their UWP/packaged app targets.

When Harbor kills explorer, the entire chain breaks:
- `ShellExecuteEx` hangs waiting for shell COM objects that will never respond
- COM `IApplicationActivationManager` hangs on the same dead infrastructure
- WinRT `Launcher.LaunchUriAsync` delegates to the same underlying COM activation
- Direct EXE launch (`SystemSettings.exe`) starts a process but the UWP app host can't display UI without the shell's activation context

## Approaches Tried

### Approach 1: WinRT `Windows.System.Launcher.LaunchUriAsync`

**Hypothesis:** The WinRT launcher API is a modern, high-level API designed for app-to-app URI activation. Since the project targets `net10.0-windows10.0.22621`, WinRT projections are available without additional packages.

```csharp
private static async void LaunchUri(string uri)
{
    try
    {
        var success = await Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
        Trace.WriteLine($"LaunchUriAsync returned: {success}");
    }
    catch (Exception ex)
    {
        Trace.WriteLine($"LaunchUriAsync failed: {ex.Message}");
    }
}
```

**Result:** Silent failure. `LaunchUriAsync` returned `false` with no exception. The API internally delegates to the same shell COM infrastructure that is dead.

---

### Approach 2: COM `IApplicationActivationManager`

**Hypothesis:** Using the COM activation manager directly (the same API that `explorer.exe` uses internally) might bypass the shell dependency.

```csharp
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager
{
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
        uint options,
        out uint processId);
    // ...
}

// CLSID: 45BA127D-10A8-46EA-8AB7-56EA9078943C
var aam = (IApplicationActivationManager)new ApplicationActivationManager();
aam.ActivateApplication(
    "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
    "ms-settings:", 0, out uint pid);
```

**Result:** Call hung indefinitely. Never returned, never threw.

Diagnostic log:
```
[13:16:23.673] LaunchUri called: uri=ms-settings:, hwnd=0xE05E2
[13:16:23.677] Resolved AUMID: windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel
[13:16:23.678] Creating ApplicationActivationManager...
[13:16:23.692] Created AAM. Checking IInitializeWithWindow support: False
[13:16:23.693] Calling ActivateApplication(...)...
                 <--- never returned
```

---

### Approach 3: COM AAM + `IInitializeWithWindow`

**Hypothesis:** Another developer suggested that `IApplicationActivationManager` needs a parent HWND via `IInitializeWithWindow` to function in a desktop context without a shell.

```csharp
[ComImport, Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IInitializeWithWindow
{
    void Initialize(IntPtr hwnd);
}

var aam = new ApplicationActivationManager();
if (aam is IInitializeWithWindow initWindow)
{
    initWindow.Initialize(hwnd);  // GetForegroundWindow()
}
```

**Result:** `ApplicationActivationManager` does **not** implement `IInitializeWithWindow`. The `is` check returned `false`. The `ActivateApplication` call still hung.

---

### Approach 4: Multi-approach diagnostic test

Tried all three mechanisms with timeouts on dedicated threads to compare behavior:

```
A1: Process.Start(uri) with UseShellExecute=true  — on background thread with 5s timeout
A2: COM ActivateApplication                        — on STA thread with 5s timeout
A3: Process.Start("explorer.exe", uri)             — original approach
```

Diagnostic log:
```
[13:24:15.366] ========== LaunchUri: ms-settings: ==========
[13:24:15.376] [A1] Process.Start(uri, UseShellExecute=true)...
[13:24:20.364] [Approach1_UseShellExecute] HUNG — timed out after 5s
[13:24:20.384] [A2] Creating AAM on STA thread (ApartmentState=STA)...
[13:24:20.389] [A2] Calling ActivateApplication(...)...
[13:24:25.385] [Approach2_COM_STA] HUNG — timed out after 5s
[13:24:25.393] [A3] Process.Start(explorer.exe, uri, UseShellExecute=false)...
[13:24:25.404] [A3] SUCCEEDED (process started — may take 30s to show)
[13:25:00.611] [Approach1_UseShellExecute] EXCEPTION: Win32Exception: Unknown error (0x80040900) (0x80004005)
```

**Results:**
- **A1 (`ShellExecuteEx`)**: Hung for ~5s, then eventually threw `Win32Exception` with `0x80040900` after ~35s total
- **A2 (`COM AAM on STA`)**: Hung indefinitely even on a dedicated STA thread
- **A3 (`explorer.exe <uri>`)**: Process started immediately, but resulted in ~30s delay then error dialog

---

### Approach 5: Direct EXE launch

**Hypothesis:** Bypass protocol URI resolution entirely by launching the Settings app's executable directly.

```csharp
var settingsExe = @"C:\WINDOWS\ImmersiveControlPanel\SystemSettings.exe";
Process.Start(new ProcessStartInfo(settingsExe, uri) { UseShellExecute = false });
```

Diagnostic log:
```
[13:39:31.289] ========== LaunchUri: ms-settings: ==========
[13:39:31.292] [Direct] Trying direct launch: C:\WINDOWS\ImmersiveControlPanel\SystemSettings.exe
[13:39:31.304] [Direct] SUCCEEDED
```

**Result:** Process started (no exception), but **no window appeared**. `SystemSettings.exe` is a UWP/packaged app — it requires the AppX activation context provided by the shell to display its UI. Without explorer hosting the shell namespace, the process launches but has no activation context to create its XAML Islands / CoreWindow.

---

### Approach 6: Temporarily restart explorer

**Hypothesis:** Since all activation APIs need a live shell, temporarily restart explorer, activate the URI, then kill explorer again.

```csharp
private static void LaunchUri(string uri)
{
    Task.Run(() =>
    {
        const uint WM_QUIT = 0x0012;
        // 1. Start explorer.exe
        Process.Start("explorer.exe");

        // 2. Wait for Shell_TrayWnd to appear (= shell is ready)
        HWND trayWnd = default;
        for (int i = 0; i < 150; i++) // 15s timeout
        {
            trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
            if (trayWnd != default) break;
            Thread.Sleep(100);
        }
        if (trayWnd == default) return;

        Thread.Sleep(500); // Let shell fully initialize

        // 3. Activate URI (should work now)
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

        Thread.Sleep(3000); // Wait for activation to process

        // 4. Kill explorer again
        trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (trayWnd != default)
        {
            WindowInterop.PostMessage(trayWnd, WM_QUIT, 0, 0);
            foreach (var proc in Process.GetProcessesByName("explorer"))
            {
                proc.WaitForExit(5000);
                proc.Dispose();
            }
        }
    });
}
```

**Result:** Not working. Still no visible effect. Possible issues:
- Explorer may be conflicting with Harbor's existing AppBar registrations or work area adjustments
- The shell may not fully initialize while Harbor's AppBars are occupying the screen
- `UseShellExecute = true` may still route through the dead-then-alive-then-dead shell in a racy way
- Killing explorer again may tear down the Settings app's activation context before it can fully start
- Harbor's `WorkAreaService` has already called `SystemParametersInfo(SPI_SETWORKAREA)` to adjust the work area — explorer starting up may conflict with this

## Summary Table

| # | Approach | API | Result | Error |
|---|----------|-----|--------|-------|
| 1 | WinRT Launcher | `Windows.System.Launcher.LaunchUriAsync` | Silent failure (returns `false`) | None |
| 2 | COM AAM (MTA) | `IApplicationActivationManager::ActivateApplication` | Hung indefinitely | N/A |
| 3 | COM AAM + IInitializeWithWindow | Same + `IInitializeWithWindow::Initialize` | AAM doesn't implement the interface; still hung | N/A |
| 4 | ShellExecuteEx | `Process.Start(uri, UseShellExecute=true)` | Hung ~35s then failed | `0x80040900` |
| 5 | Direct EXE | `Process.Start("SystemSettings.exe")` | Process started, no window | Missing AppX context |
| 6 | Temp restart explorer | Start explorer → ShellExecute → kill explorer | No visible effect | Unknown |

## Key Files

| File | Role |
|---|---|
| `src/Harbor.Core/Services/SystemActionService.cs` | Contains `LaunchUri()` — the method under investigation |
| `src/Harbor.Shell/App.xaml.cs` | Contains `KillExplorer()` — how Harbor kills the shell on startup |
| `src/Harbor.Core/Services/WorkAreaService.cs` | Manually adjusts screen work area via `SystemParametersInfo` |
| `src/Harbor.Core/Services/CrashRecoveryService.cs` | Contains `LaunchExplorer()` — how Harbor restarts explorer on exit/crash |
| `src/Harbor.Core/Services/ShellSettingsService.cs` | `ReplaceExplorer` setting (default: `true`) |
| `src/Harbor.Core/Interop/WindowInterop.cs` | Win32 P/Invoke wrappers (PostMessage, FindWindow, etc.) |
| `src/Harbor.Core/NativeMethods.txt` | CsWin32 configuration for Win32 API generation |

## Diagnostic Log Location

`%LOCALAPPDATA%\Harbor\launch-diag.log` — file-based diagnostic logging is built into the current `LaunchUri` implementation.

## Environment

- Windows 11 (10.0.26200)
- .NET 10, C# 13, WPF
- CsWin32 0.3.183
- ManagedShell 0.0.337
- Target: `net10.0-windows10.0.22621`

## Ideas Not Yet Tried

1. **PowerShell `Start-Process`**: Launch Settings via `powershell -Command "Start-Process ms-settings:"` — PowerShell may have its own shell context that can resolve protocol URIs independently.

2. **`explorer.exe ms-settings:` with `UseShellExecute = false` but wait longer before killing**: The original approach started a new explorer instance. Perhaps instead of killing explorer immediately after the URI opens, let it coexist temporarily and kill it only after the target app window is confirmed visible.

3. **`cmd /c start ms-settings:`**: Similar to PowerShell idea — `cmd`'s `start` command calls `ShellExecuteEx` but from its own process context.

4. **Register Harbor as the shell and use `IApplicationActivationManager` from shell context**: If Harbor registers itself as the Windows shell (HKCU `Shell` registry key), the activation infrastructure may recognize Harbor's process as the shell and allow COM activation to work.

5. **Use `CreateProcess` with an AppX activation token**: The `CreateProcessW` function can accept an `STARTUPINFOEX` with `PROC_THREAD_ATTRIBUTE_PACKAGE_FULL_NAME` to launch packaged apps. This may provide the activation context that direct EXE launch was missing.

6. **Don't kill explorer — hide it instead**: Instead of posting `WM_QUIT` to `Shell_TrayWnd`, simply hide the taskbar window (set `WS_EX_TOOLWINDOW` + move offscreen). Explorer stays alive but invisible, and all activation APIs would work. Trade-off: explorer consumes memory and may interfere with AppBar behavior.

7. **Use the `IPackageDebugSettings` interface**: This COM interface can activate packaged apps for debugging. It may work without the shell since it's designed for development scenarios.

8. **Investigate Cairo Shell / other shell replacements**: [Cairo Shell](https://github.com/cairoshell/cairoshell) is a mature Windows shell replacement that also uses ManagedShell. Research how they handle protocol URI activation with explorer dead.

## Questions for Reviewer

1. Is there a way to activate UWP/packaged apps (like Settings and Store) without the explorer shell process hosting the shell namespace?
2. Is the "temporarily restart explorer" approach viable if we handle the timing and cleanup correctly? What are we likely getting wrong?
3. Should we abandon the "kill explorer" approach and instead hide it, keeping the shell infrastructure alive but visually suppressed?
4. Are there undocumented Windows APIs or registry keys that allow protocol URI resolution without a live shell?
