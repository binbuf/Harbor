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
    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
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
}
