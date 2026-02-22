using System.Diagnostics;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;

namespace Harbor.Core.Services;

/// <summary>
/// Routes traffic light button clicks to the correct WM_SYSCOMMAND for the target window.
/// Also queries window capabilities (WS_MINIMIZEBOX, WS_MAXIMIZEBOX) and maximized state.
/// </summary>
public sealed class WindowCommandService
{
    /// <summary>
    /// Sends the appropriate WM_SYSCOMMAND for the given action to the target window.
    /// For Maximize, automatically toggles between SC_MAXIMIZE and SC_RESTORE based on current state.
    /// </summary>
    public void Execute(HWND hwnd, TrafficLightAction action)
    {
        if (hwnd == HWND.Null) return;
        if (!WindowInterop.IsWindow(hwnd)) return;

        var command = action switch
        {
            TrafficLightAction.Close => WindowInterop.SC_CLOSE,
            TrafficLightAction.Minimize => WindowInterop.SC_MINIMIZE,
            TrafficLightAction.Maximize => WindowInterop.IsMaximized(hwnd)
                ? WindowInterop.SC_RESTORE
                : WindowInterop.SC_MAXIMIZE,
            _ => 0u,
        };

        if (command == 0) return;

        Trace.WriteLine($"[Harbor] WindowCommandService: Sending SC 0x{command:X4} to HWND {hwnd}");
        WindowInterop.PostSysCommand(hwnd, command);
    }

    /// <summary>
    /// Returns true if the window supports minimize (has WS_MINIMIZEBOX style).
    /// </summary>
    public static bool CanMinimize(HWND hwnd)
    {
        return WindowInterop.HasMinimizeBox(hwnd);
    }

    /// <summary>
    /// Returns true if the window supports maximize/restore (has WS_MAXIMIZEBOX style).
    /// </summary>
    public static bool CanMaximize(HWND hwnd)
    {
        return WindowInterop.HasMaximizeBox(hwnd);
    }

    /// <summary>
    /// Returns true if the window is currently maximized.
    /// </summary>
    public static bool IsMaximized(HWND hwnd)
    {
        return WindowInterop.IsMaximized(hwnd);
    }

    /// <summary>
    /// Resolves the SC_ command constant that would be sent for a given action and window state.
    /// Useful for testing without actually sending the message.
    /// </summary>
    public static uint ResolveCommand(TrafficLightAction action, bool isMaximized)
    {
        return action switch
        {
            TrafficLightAction.Close => WindowInterop.SC_CLOSE,
            TrafficLightAction.Minimize => WindowInterop.SC_MINIMIZE,
            TrafficLightAction.Maximize => isMaximized
                ? WindowInterop.SC_RESTORE
                : WindowInterop.SC_MAXIMIZE,
            _ => 0,
        };
    }
}
