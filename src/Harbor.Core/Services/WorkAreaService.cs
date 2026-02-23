using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Manually reserves screen space for AppBars by adjusting the Windows work area
/// via SystemParametersInfo(SPI_SETWORKAREA). Computes from the full primary monitor
/// bounds so that any existing reservations (e.g. the Windows taskbar) are overridden.
/// The original work area is saved on first Apply and restored on Dispose/Restore.
/// </summary>
public sealed class WorkAreaService : IDisposable
{
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SPI_SETWORKAREA = 0x002F;

    private RECT _originalWorkArea;
    private RECT _screenBounds;
    private bool _applied;
    private bool _disposed;

    /// <summary>
    /// Captures the current work area, reads the full primary monitor bounds,
    /// and applies a work area that reserves space only for Harbor's bars.
    /// </summary>
    /// <param name="topInset">Pixels reserved at the top (menu bar height).</param>
    /// <param name="bottomInset">Pixels reserved at the bottom (dock height).</param>
    public void Apply(int topInset, int bottomInset)
    {
        if (_applied) return;

        // Save the original work area (includes taskbar reservation) so we can restore it
        _originalWorkArea = GetWorkArea();
        Trace.WriteLine($"[Harbor] WorkAreaService: Original work area: L={_originalWorkArea.left} T={_originalWorkArea.top} R={_originalWorkArea.right} B={_originalWorkArea.bottom}");

        // Get the full primary monitor bounds (ignoring any AppBar reservations)
        _screenBounds = GetPrimaryMonitorBounds();
        Trace.WriteLine($"[Harbor] WorkAreaService: Screen bounds: L={_screenBounds.left} T={_screenBounds.top} R={_screenBounds.right} B={_screenBounds.bottom}");

        // Compute from full screen bounds, not the current work area
        var reduced = new RECT
        {
            left = _screenBounds.left,
            top = _screenBounds.top + topInset,
            right = _screenBounds.right,
            bottom = _screenBounds.bottom - bottomInset,
        };

        SetWorkArea(reduced);
        _applied = true;
        Trace.WriteLine($"[Harbor] WorkAreaService: Applied work area: L={reduced.left} T={reduced.top} R={reduced.right} B={reduced.bottom}");
    }

    /// <summary>
    /// Re-applies the work area with new insets without resetting the saved original work area.
    /// </summary>
    public void Reapply(int topInset, int bottomInset)
    {
        if (!_applied) return;

        var reduced = new RECT
        {
            left = _screenBounds.left,
            top = _screenBounds.top + topInset,
            right = _screenBounds.right,
            bottom = _screenBounds.bottom - bottomInset,
        };

        SetWorkArea(reduced);
        Trace.WriteLine($"[Harbor] WorkAreaService: Reapplied work area: L={reduced.left} T={reduced.top} R={reduced.right} B={reduced.bottom}");
    }

    /// <summary>
    /// Restores the original work area. Safe to call multiple times.
    /// </summary>
    public void Restore()
    {
        if (!_applied) return;

        SetWorkArea(_originalWorkArea);
        _applied = false;
        Trace.WriteLine("[Harbor] WorkAreaService: Restored original work area.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        Restore();
        _disposed = true;
    }

    private static unsafe RECT GetWorkArea()
    {
        RECT rect = default;
        PInvoke.SystemParametersInfo(
            (SYSTEM_PARAMETERS_INFO_ACTION)SPI_GETWORKAREA,
            0,
            &rect,
            0);
        return rect;
    }

    private static unsafe void SetWorkArea(RECT rect)
    {
        PInvoke.SystemParametersInfo(
            (SYSTEM_PARAMETERS_INFO_ACTION)SPI_SETWORKAREA,
            0,
            &rect,
            SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
    }

    /// <summary>
    /// Gets the full bounds of the primary monitor (rcMonitor, not rcWork).
    /// </summary>
    private static RECT GetPrimaryMonitorBounds()
    {
        // MONITOR_DEFAULTTOPRIMARY = 1
        var hMonitor = PInvoke.MonitorFromWindow(HWND.Null, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(hMonitor, ref info);
        return info.rcMonitor;
    }
}
