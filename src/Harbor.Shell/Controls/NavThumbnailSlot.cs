using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;

namespace Harbor.Shell.Controls;

/// <summary>
/// An HwndHost that creates a child Win32 HWND and registers a DWM thumbnail
/// of a source application window into it. Used by AppNavigatorOverlay so
/// DWM renders live thumbnails without WPF transparency interference.
///
/// The child HWND has WS_EX_TRANSPARENT so mouse events pass through to the
/// WPF sibling overlay elements (hover border, label) positioned on top.
/// </summary>
public sealed class NavThumbnailSlot : HwndHost
{
    private readonly nint _sourceHwnd;
    private HWND _childHwnd;
    private nint _thumbId;

    public NavThumbnailSlot(nint sourceHwnd)
    {
        _sourceHwnd = sourceHwnd;
        IsHitTestVisible = false; // let WPF siblings handle mouse
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var w = Math.Max(1, (int)ActualWidth);
        var h = Math.Max(1, (int)ActualHeight);

        _childHwnd = WindowInterop.CreateChildWindow(new HWND(hwndParent.Handle), w, h);

        if (_childHwnd == HWND.Null)
        {
            Trace.WriteLine("[Harbor] NavThumbnailSlot: CreateChildWindow failed.");
            return new HandleRef(this, nint.Zero);
        }

        // Register DWM thumbnail: paint source window into our child HWND
        var hr = DwmInterop.RegisterThumbnail(_childHwnd, new HWND(_sourceHwnd), out _thumbId);
        if (hr.Failed)
        {
            Trace.WriteLine($"[Harbor] NavThumbnailSlot: DwmRegisterThumbnail failed hr=0x{hr.Value:X8}");
        }
        else
        {
            UpdateDwmThumbnail(w, h);
        }

        return new HandleRef(this, _childHwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_thumbId != 0)
        {
            DwmInterop.UnregisterThumbnail(_thumbId);
            _thumbId = 0;
        }

        if (_childHwnd != HWND.Null)
        {
            WindowInterop.DestroyWindowHandle(_childHwnd);
            _childHwnd = HWND.Null;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshThumbnailSize();
    }

    /// <summary>Called externally after layout changes to re-sync the DWM destination rect.</summary>
    public void RefreshThumbnailSize()
    {
        if (_thumbId == 0 || _childHwnd == HWND.Null) return;

        // Get physical pixel size from the child HWND client rect
        if (!WindowInterop.GetClientRect(_childHwnd, out var clientRect)) return;

        var w = clientRect.right - clientRect.left;
        var h = clientRect.bottom - clientRect.top;
        if (w > 0 && h > 0)
            UpdateDwmThumbnail(w, h);
    }

    /// <summary>
    /// Directly resizes the child HWND to the given physical pixel dimensions and updates
    /// the DWM thumbnail destination rect. This bypasses WPF layout so it takes effect
    /// immediately — essential for smooth animation.
    /// </summary>
    public void SetPhysicalSize(int physW, int physH)
    {
        if (_childHwnd == HWND.Null || physW <= 0 || physH <= 0) return;

        WindowInterop.SetWindowPos(_childHwnd, HWND.Null, 0, 0, physW, physH,
            Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        UpdateDwmThumbnail(physW, physH);
    }

    private void UpdateDwmThumbnail(int physW, int physH)
    {
        if (_thumbId == 0) return;
        var hr = DwmInterop.UpdateThumbnailDestination(_thumbId, physW, physH);
        if (hr.Failed)
            Trace.WriteLine($"[Harbor] NavThumbnailSlot: UpdateThumbnail failed hr=0x{hr.Value:X8}");
    }
}
