using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Interop;

/// <summary>
/// Wrapper over Win32 window management APIs.
/// </summary>
public static class WindowInterop
{
    // System command constants for SendMessage/PostMessage
    public const uint SC_CLOSE = 0xF060;
    public const uint SC_MINIMIZE = 0xF020;
    public const uint SC_MAXIMIZE = 0xF030;
    public const uint SC_RESTORE = 0xF120;
    public const uint WM_SYSCOMMAND = 0x0112;

    // Window style constants
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_MAXIMIZE = 0x01000000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TOPMOST = 0x00000008;

    public static HWND GetForegroundWindow()
    {
        return PInvoke.GetForegroundWindow();
    }

    public static bool GetWindowRect(HWND hwnd, out RECT rect)
    {
        return PInvoke.GetWindowRect(hwnd, out rect);
    }

    public static bool GetClientRect(HWND hwnd, out RECT rect)
    {
        return PInvoke.GetClientRect(hwnd, out rect);
    }

    public static bool SetWindowPos(HWND hwnd, HWND hwndInsertAfter, int x, int y, int cx, int cy, SET_WINDOW_POS_FLAGS flags)
    {
        return PInvoke.SetWindowPos(hwnd, hwndInsertAfter, x, y, cx, cy, flags);
    }

    public static bool SetWindowPosNoActivate(HWND hwnd, int x, int y, int cx, int cy)
    {
        return PInvoke.SetWindowPos(
            hwnd,
            HWND.Null,
            x, y, cx, cy,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
    }

    public static bool ShowWindow(HWND hwnd, SHOW_WINDOW_CMD command)
    {
        return PInvoke.ShowWindow(hwnd, command);
    }

    public static LRESULT SendMessage(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        return PInvoke.SendMessage(hwnd, msg, wParam, lParam);
    }

    public static bool PostMessage(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        return PInvoke.PostMessage(hwnd, msg, wParam, lParam);
    }

    public static void SendSysCommand(HWND hwnd, uint command)
    {
        PInvoke.SendMessage(hwnd, WM_SYSCOMMAND, (WPARAM)command, (LPARAM)0);
    }

    public static void PostSysCommand(HWND hwnd, uint command)
    {
        PInvoke.PostMessage(hwnd, WM_SYSCOMMAND, (WPARAM)command, (LPARAM)0);
    }

    public static nint GetWindowLongPtr(HWND hwnd, WINDOW_LONG_PTR_INDEX index)
    {
        return PInvoke.GetWindowLong(hwnd, index);
    }

    public static nint SetWindowLongPtr(HWND hwnd, WINDOW_LONG_PTR_INDEX index, nint newLong)
    {
        return PInvoke.SetWindowLong(hwnd, index, (int)newLong);
    }

    public static HMONITOR MonitorFromWindow(HWND hwnd, MONITOR_FROM_FLAGS flags)
    {
        return PInvoke.MonitorFromWindow(hwnd, flags);
    }

    public static bool IsWindow(HWND hwnd)
    {
        return PInvoke.IsWindow(hwnd);
    }

    public static bool IsWindowVisible(HWND hwnd)
    {
        return PInvoke.IsWindowVisible(hwnd);
    }

    public static uint GetCurrentProcessId()
    {
        return PInvoke.GetCurrentProcessId();
    }

    public static string GetWindowText(HWND hwnd)
    {
        var length = PInvoke.GetWindowTextLength(hwnd);
        if (length == 0) return string.Empty;

        unsafe
        {
            var buffer = stackalloc char[length + 1];
            PInvoke.GetWindowText(hwnd, buffer, length + 1);
            return new string(buffer);
        }
    }

    public static string GetClassName(HWND hwnd)
    {
        unsafe
        {
            var buffer = stackalloc char[256];
            var length = PInvoke.GetClassName(hwnd, buffer, 256);
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }
    }

    public static uint GetWindowThreadProcessId(HWND hwnd, out uint processId)
    {
        unsafe
        {
            uint pid;
            var threadId = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
            processId = pid;
            return threadId;
        }
    }

    public static uint GetWindowStyle(HWND hwnd)
    {
        return (uint)GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
    }

    public static bool HasMinimizeBox(HWND hwnd)
    {
        return (GetWindowStyle(hwnd) & WS_MINIMIZEBOX) != 0;
    }

    public static bool HasMaximizeBox(HWND hwnd)
    {
        return (GetWindowStyle(hwnd) & WS_MAXIMIZEBOX) != 0;
    }

    public static bool IsMaximized(HWND hwnd)
    {
        return (GetWindowStyle(hwnd) & WS_MAXIMIZE) != 0;
    }

    /// <summary>
    /// Returns the Z-order position of a window (0 = topmost in Z, higher = further back).
    /// Walks the GW_HWNDPREV chain from the given window.
    /// </summary>
    public static int GetZOrder(HWND hwnd)
    {
        int z = 0;
        var current = hwnd;
        while ((current = PInvoke.GetWindow(current, GET_WINDOW_CMD.GW_HWNDPREV)) != HWND.Null)
        {
            z++;
        }
        return z;
    }

    // Win32 constants for child window creation
    private const uint WS_CHILD = 0x40000000;
    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_EX_TRANSPARENT_STYLE = 0x00000020;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint hwnd);

    /// <summary>
    /// Creates a transparent Win32 child HWND using the built-in "STATIC" window class.
    /// Used by HwndHost-based controls (NavThumbnailSlot) to host DWM thumbnails.
    /// WS_EX_TRANSPARENT lets mouse input pass through to WPF siblings.
    /// </summary>
    public static unsafe HWND CreateChildWindow(HWND hwndParent, int width, int height)
    {
        var handle = CreateWindowExW(
            WS_EX_TRANSPARENT_STYLE,
            "STATIC",
            null,
            WS_CHILD | WS_VISIBLE,
            0, 0, width, height,
            (nint)hwndParent.Value,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        return new HWND((void*)handle);
    }

    /// <summary>Destroys a Win32 window (e.g. child HWND created by CreateChildWindow).</summary>
    public static unsafe bool DestroyWindowHandle(HWND hwnd)
    {
        return DestroyWindow((nint)hwnd.Value);
    }
}
