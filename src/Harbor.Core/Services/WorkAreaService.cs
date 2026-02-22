using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Manually reserves screen space for AppBars by adjusting the Windows work area
/// via SystemParametersInfo(SPI_SETWORKAREA). This is needed because SHAppBarMessage
/// requires explorer.exe to be running, and Harbor kills explorer on startup.
/// </summary>
public sealed class WorkAreaService : IDisposable
{
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SPI_SETWORKAREA = 0x002F;

    private RECT _originalWorkArea;
    private bool _applied;
    private bool _disposed;

    /// <summary>
    /// Captures the current work area and applies a reduced work area
    /// accounting for the menu bar and dock.
    /// </summary>
    /// <param name="topInset">Pixels reserved at the top (menu bar height).</param>
    /// <param name="bottomInset">Pixels reserved at the bottom (dock height).</param>
    public void Apply(int topInset, int bottomInset)
    {
        if (_applied) return;

        // Save the original work area
        _originalWorkArea = GetWorkArea();
        Trace.WriteLine($"[Harbor] WorkAreaService: Original work area: L={_originalWorkArea.left} T={_originalWorkArea.top} R={_originalWorkArea.right} B={_originalWorkArea.bottom}");

        // Apply reduced work area
        var reduced = new RECT
        {
            left = _originalWorkArea.left,
            top = _originalWorkArea.top + topInset,
            right = _originalWorkArea.right,
            bottom = _originalWorkArea.bottom - bottomInset,
        };

        SetWorkArea(reduced);
        _applied = true;
        Trace.WriteLine($"[Harbor] WorkAreaService: Applied work area: L={reduced.left} T={reduced.top} R={reduced.right} B={reduced.bottom}");
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
}
