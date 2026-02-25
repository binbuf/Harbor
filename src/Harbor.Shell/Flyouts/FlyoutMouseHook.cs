using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Harbor.Shell.Flyouts;

/// <summary>
/// Low-level mouse hook that detects clicks outside a target window and invokes a callback.
/// Used by flyouts to dismiss themselves when clicking anywhere else — including
/// non-activating windows (WS_EX_NOACTIVATE) like the menu bar and dock.
/// </summary>
internal sealed class FlyoutMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public System.Drawing.Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly Window _targetWindow;
    private readonly Action _onClickOutside;
    private readonly LowLevelMouseProc _hookProc;
    private IntPtr _hookId;
    private bool _disposed;

    public FlyoutMouseHook(Window targetWindow, Action onClickOutside)
    {
        _targetWindow = targetWindow;
        _onClickOutside = onClickOutside;

        // Must hold a reference to the delegate to prevent GC
        _hookProc = HookCallback;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (!IsPointInsideWindow(hookStruct.pt))
                {
                    // Dispatch close on the UI thread
                    _targetWindow.Dispatcher.BeginInvoke(_onClickOutside);
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool IsPointInsideWindow(System.Drawing.Point screenPt)
    {
        // Get the flyout's HWND bounds in physical screen pixels
        var hwndSource = (HwndSource?)PresentationSource.FromVisual(_targetWindow);
        if (hwndSource is null) return false;

        GetWindowRect(hwndSource.Handle, out var rect);
        return screenPt.X >= rect.Left && screenPt.X <= rect.Right
            && screenPt.Y >= rect.Top && screenPt.Y <= rect.Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
}
