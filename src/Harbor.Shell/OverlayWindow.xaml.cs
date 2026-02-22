using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

/// <summary>
/// Borderless, transparent WPF overlay window that sits on top of an application title bar.
/// Uses WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW to avoid stealing focus
/// and to stay hidden from Alt+Tab / taskbar.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>
    /// The target window handle this overlay is tracking.
    /// </summary>
    public HWND TargetHwnd { get; }

    /// <summary>
    /// The Win32 handle of this overlay window, available after SourceInitialized.
    /// </summary>
    public HWND OverlayHwnd { get; private set; }

    public OverlayWindow(HWND targetHwnd)
    {
        TargetHwnd = targetHwnd;
        InitializeComponent();
        TrafficLights.TargetHwnd = targetHwnd;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// The ButtonClicked event from the traffic light buttons.
    /// </summary>
    public event Action<HWND, TrafficLightAction>? ButtonClicked
    {
        add => TrafficLights.ButtonClicked += value;
        remove => TrafficLights.ButtonClicked -= value;
    }

    /// <summary>
    /// Sets whether the target window is active (foreground).
    /// Controls traffic light button colors (active vs inactive gray).
    /// </summary>
    public void SetActive(bool isActive)
    {
        TrafficLights.SetActive(isActive);
    }

    /// <summary>
    /// Updates button capabilities based on the target window's styles.
    /// </summary>
    public void SetCapabilities(bool canMinimize, bool canMaximize)
    {
        TrafficLights.SetCanMinimize(canMinimize);
        TrafficLights.SetCanMaximize(canMaximize);
    }

    /// <summary>
    /// Updates the maximize button glyph based on the target window's maximized state.
    /// </summary>
    public void SetMaximized(bool isMaximized)
    {
        TrafficLights.SetMaximized(isMaximized);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        OverlayHwnd = new HWND(hwndSource.Handle);

        // Apply extended window styles: LAYERED | NOACTIVATE | TOOLWINDOW
        var exStyle = (uint)WindowInterop.GetWindowLongPtr(
            OverlayHwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        exStyle |= WindowInterop.WS_EX_LAYERED
                 | WindowInterop.WS_EX_NOACTIVATE
                 | WindowInterop.WS_EX_TOOLWINDOW;

        WindowInterop.SetWindowLongPtr(
            OverlayHwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            (nint)exStyle);

        Trace.WriteLine($"[Harbor] OverlayWindow: Created for target HWND {TargetHwnd}, overlay HWND {OverlayHwnd}");
    }

    /// <summary>
    /// Repositions the overlay using direct SetWindowPos P/Invoke, bypassing WPF layout.
    /// Uses SWP_NOACTIVATE | SWP_NOZORDER to avoid stealing focus or changing z-order.
    /// </summary>
    public void Reposition(RECT titleBarRect)
    {
        if (OverlayHwnd == HWND.Null) return;

        var x = titleBarRect.left;
        var y = titleBarRect.top;
        var cx = titleBarRect.right - titleBarRect.left;
        var cy = titleBarRect.bottom - titleBarRect.top;

        WindowInterop.SetWindowPos(
            OverlayHwnd,
            HWND.Null,
            x, y, cx, cy,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
    }

    /// <summary>
    /// Updates the overlay z-order to appear directly above the target window.
    /// </summary>
    public void UpdateZOrder()
    {
        if (OverlayHwnd == HWND.Null) return;

        // Place this overlay directly after (in front of) the target window in z-order.
        // SetWindowPos with hwndInsertAfter = targetHwnd places the overlay above the target.
        WindowInterop.SetWindowPos(
            OverlayHwnd,
            TargetHwnd,
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
            | SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }
}
