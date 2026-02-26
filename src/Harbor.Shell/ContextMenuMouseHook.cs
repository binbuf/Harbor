using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Harbor.Shell;

/// <summary>
/// Low-level mouse hook that detects clicks outside a <see cref="ContextMenu"/> and invokes a callback.
/// Needed because the dock has WS_EX_NOACTIVATE, which breaks WPF's built-in context menu dismissal
/// when clicking the desktop or other app windows.
/// </summary>
internal sealed class ContextMenuMouseHook : IDisposable
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private readonly ContextMenu _contextMenu;
    private readonly Action _onClickOutside;
    private readonly LowLevelMouseProc _hookProc;
    private IntPtr _hookId;
    private bool _disposed;

    public ContextMenuMouseHook(ContextMenu contextMenu, Action onClickOutside)
    {
        _contextMenu = contextMenu;
        _onClickOutside = onClickOutside;

        _hookProc = HookCallback;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (!IsPointInsideMenuTree(hookStruct.pt))
                {
                    _contextMenu.Dispatcher.BeginInvoke(_onClickOutside);
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Checks whether the screen point falls inside the context menu or any open submenu.
    /// </summary>
    private bool IsPointInsideMenuTree(POINT screenPt)
    {
        // Check the main context menu popup
        var popup = FindPopup(_contextMenu);
        if (popup is not null && IsPointInsidePopup(screenPt, popup))
            return true;

        // Check any open submenu popups
        return IsPointInsideSubmenu(screenPt, _contextMenu);
    }

    private static bool IsPointInsideSubmenu(POINT screenPt, ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not MenuItem menuItem)
                continue;

            if (!menuItem.IsSubmenuOpen)
                continue;

            var submenuPopup = FindPopup(menuItem);
            if (submenuPopup is not null && IsPointInsidePopup(screenPt, submenuPopup))
                return true;

            // Recurse into nested submenus
            if (IsPointInsideSubmenu(screenPt, menuItem))
                return true;
        }
        return false;
    }

    private static Popup? FindPopup(FrameworkElement element)
    {
        // ContextMenu and MenuItem both use a Popup child in their template
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is Popup popup)
                return popup;
        }
        return null;
    }

    private static bool IsPointInsidePopup(POINT screenPt, Popup popup)
    {
        if (popup.Child is null) return false;

        var source = (HwndSource?)PresentationSource.FromVisual(popup.Child);
        if (source is null) return false;

        GetWindowRect(source.Handle, out var rect);
        return screenPt.X >= rect.Left && screenPt.X <= rect.Right
            && screenPt.Y >= rect.Top && screenPt.Y <= rect.Bottom;
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
