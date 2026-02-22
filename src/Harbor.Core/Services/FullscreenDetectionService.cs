using System.Diagnostics;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Result of fullscreen classification for a window.
/// </summary>
public readonly record struct FullscreenInfo(
    bool IsFullscreen,
    IntPtr MonitorHandle,
    FullscreenKind Kind);

/// <summary>
/// The type of fullscreen detected.
/// </summary>
public enum FullscreenKind
{
    None,
    BorderlessFullscreen,
    ExclusiveTopmost,
}

/// <summary>
/// Detects whether a window is running in fullscreen mode by comparing
/// its dimensions against the display bounds and checking window styles.
/// </summary>
public sealed class FullscreenDetectionService
{
    /// <summary>
    /// Classifies whether the given window is fullscreen on its monitor.
    /// </summary>
    public FullscreenInfo Classify(HWND hwnd)
    {
        if (hwnd == HWND.Null)
            return new FullscreenInfo(false, IntPtr.Zero, FullscreenKind.None);

        if (!WindowInterop.IsWindow(hwnd) || !WindowInterop.IsWindowVisible(hwnd))
            return new FullscreenInfo(false, IntPtr.Zero, FullscreenKind.None);

        var monitorHandle = DisplayInterop.GetMonitorForWindow(hwnd);
        var monitorBounds = DisplayInterop.GetMonitorBounds(hwnd);
        if (monitorBounds is null)
            return new FullscreenInfo(false, monitorHandle, FullscreenKind.None);

        var monitor = monitorBounds.Value;

        if (!WindowInterop.GetWindowRect(hwnd, out var windowRect))
            return new FullscreenInfo(false, monitorHandle, FullscreenKind.None);

        // Check if window covers the full monitor
        if (!CoversMonitor(windowRect, monitor))
            return new FullscreenInfo(false, monitorHandle, FullscreenKind.None);

        // Check for DXGI exclusive mode: WS_EX_TOPMOST + matching client rect
        if (IsExclusiveTopmost(hwnd, monitor))
        {
            Trace.WriteLine($"[Harbor] FullscreenDetection: HWND {hwnd} classified as ExclusiveTopmost.");
            return new FullscreenInfo(true, monitorHandle, FullscreenKind.ExclusiveTopmost);
        }

        // Check for borderless fullscreen: no visible NONCLIENT area
        if (HasNoNonClientArea(hwnd))
        {
            Trace.WriteLine($"[Harbor] FullscreenDetection: HWND {hwnd} classified as BorderlessFullscreen.");
            return new FullscreenInfo(true, monitorHandle, FullscreenKind.BorderlessFullscreen);
        }

        return new FullscreenInfo(false, monitorHandle, FullscreenKind.None);
    }

    /// <summary>
    /// Returns true if the window rect covers the entire monitor bounds.
    /// The window must match or exceed the monitor dimensions.
    /// </summary>
    internal static bool CoversMonitor(RECT windowRect, RECT monitorRect)
    {
        return windowRect.left <= monitorRect.left
            && windowRect.top <= monitorRect.top
            && windowRect.right >= monitorRect.right
            && windowRect.bottom >= monitorRect.bottom;
    }

    /// <summary>
    /// Checks for DXGI exclusive mode: WS_EX_TOPMOST combined with a client rect
    /// that matches the display resolution.
    /// </summary>
    internal static bool IsExclusiveTopmost(HWND hwnd, RECT monitorRect)
    {
        var exStyle = (uint)WindowInterop.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if ((exStyle & WindowInterop.WS_EX_TOPMOST) == 0)
            return false;

        if (!WindowInterop.GetClientRect(hwnd, out var clientRect))
            return false;

        var monitorWidth = monitorRect.right - monitorRect.left;
        var monitorHeight = monitorRect.bottom - monitorRect.top;
        var clientWidth = clientRect.right - clientRect.left;
        var clientHeight = clientRect.bottom - clientRect.top;

        return clientWidth == monitorWidth && clientHeight == monitorHeight;
    }

    /// <summary>
    /// Returns true if the window has no visible NONCLIENT area (custom chrome / borderless).
    /// Computed as: window rect height minus client rect height is zero or negative.
    /// </summary>
    internal static bool HasNoNonClientArea(HWND hwnd)
    {
        if (!WindowInterop.GetWindowRect(hwnd, out var windowRect))
            return false;

        if (!WindowInterop.GetClientRect(hwnd, out var clientRect))
            return false;

        var windowHeight = windowRect.bottom - windowRect.top;
        var clientHeight = clientRect.bottom - clientRect.top;

        // NONCLIENT height = window height - client height
        // If zero or negative, window has no visible NONCLIENT area
        return (windowHeight - clientHeight) <= 0;
    }
}
