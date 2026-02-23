using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Hides explorer's visible UI elements (taskbar) instead of killing the process.
/// This preserves the shell's DCOM/Immersive infrastructure needed for protocol
/// URI activation (ms-settings:, ms-windows-store:, etc.).
/// </summary>
public sealed class ExplorerSuppressionService : IDisposable
{
    private readonly List<HWND> _hiddenWindows = [];
    private bool _suppressed;
    private bool _disposed;

    /// <summary>
    /// Hides all explorer taskbar windows (primary and secondary monitors).
    /// </summary>
    public void Suppress()
    {
        if (_suppressed) return;

        // Hide the primary taskbar
        var trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (trayWnd != default)
        {
            PInvoke.ShowWindow(trayWnd, SHOW_WINDOW_CMD.SW_HIDE);
            _hiddenWindows.Add(trayWnd);
            Trace.WriteLine("[Harbor] ExplorerSuppressionService: Hidden Shell_TrayWnd.");
        }
        else
        {
            Trace.WriteLine("[Harbor] ExplorerSuppressionService: Shell_TrayWnd not found — explorer may not be running.");
        }

        // Hide secondary monitor taskbars (there can be multiple)
        nint secondaryRaw = FindWindowExW(nint.Zero, nint.Zero, "Shell_SecondaryTrayWnd", null);
        while (secondaryRaw != nint.Zero)
        {
            var secondaryWnd = new HWND(secondaryRaw);
            PInvoke.ShowWindow(secondaryWnd, SHOW_WINDOW_CMD.SW_HIDE);
            _hiddenWindows.Add(secondaryWnd);
            Trace.WriteLine($"[Harbor] ExplorerSuppressionService: Hidden Shell_SecondaryTrayWnd 0x{secondaryRaw:X}.");

            // Find the next one after the current
            secondaryRaw = FindWindowExW(nint.Zero, secondaryRaw, "Shell_SecondaryTrayWnd", null);
        }

        _suppressed = true;
        Trace.WriteLine($"[Harbor] ExplorerSuppressionService: Suppressed {_hiddenWindows.Count} explorer window(s).");
    }

    /// <summary>
    /// Restores all hidden explorer taskbar windows.
    /// </summary>
    public void Restore()
    {
        if (!_suppressed) return;

        // Restore tracked windows
        foreach (var hwnd in _hiddenWindows)
        {
            if (PInvoke.IsWindow(hwnd))
            {
                PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOW);
            }
        }
        _hiddenWindows.Clear();

        // Fallback: find and show by class name in case tracked HWNDs are stale
        RestoreExplorer();

        _suppressed = false;
        Trace.WriteLine("[Harbor] ExplorerSuppressionService: Explorer restored.");
    }

    /// <summary>
    /// Static recovery method for crash/watchdog scenarios.
    /// Finds explorer taskbar windows by class name and shows them.
    /// </summary>
    public static void RestoreExplorer()
    {
        var trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (trayWnd != default)
        {
            PInvoke.ShowWindow(trayWnd, SHOW_WINDOW_CMD.SW_SHOW);
        }

        nint secondaryRaw = FindWindowExW(nint.Zero, nint.Zero, "Shell_SecondaryTrayWnd", null);
        while (secondaryRaw != nint.Zero)
        {
            PInvoke.ShowWindow(new HWND(secondaryRaw), SHOW_WINDOW_CMD.SW_SHOW);
            secondaryRaw = FindWindowExW(nint.Zero, secondaryRaw, "Shell_SecondaryTrayWnd", null);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_suppressed)
        {
            Restore();
        }
    }

    // Manual P/Invoke for FindWindowExW — CsWin32's generated version requires unsafe context
    // due to PCWSTR pointer parameters. This managed-string version avoids that.
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(
        nint hwndParent,
        nint hwndChildAfter,
        string? lpszClass,
        string? lpszWindow);
}
