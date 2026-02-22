using System.Diagnostics;
using System.Windows.Interop;
using Windows.Win32.Foundation;

namespace Harbor.Core.Services;

/// <summary>
/// Monitors display configuration changes (monitor connect/disconnect, resolution changes).
/// Subscribes to WM_DISPLAYCHANGE via a message-only window and notifies listeners.
/// </summary>
public sealed class DisplayChangeService : IDisposable
{
    private const int WM_DISPLAYCHANGE = 0x007E;

    private HwndSource? _hwndSource;
    private bool _disposed;

    /// <summary>
    /// Fired when the display configuration changes (monitor connect/disconnect/resolution change).
    /// </summary>
    public event Action? DisplayChanged;

    public DisplayChangeService()
    {
        // Create a message-only window to receive WM_DISPLAYCHANGE
        var parameters = new HwndSourceParameters("HarborDisplayChange")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        Trace.WriteLine("[Harbor] DisplayChangeService: Listening for WM_DISPLAYCHANGE.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            Trace.WriteLine("[Harbor] DisplayChangeService: WM_DISPLAYCHANGE received.");
            DisplayChanged?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
            _hwndSource = null;
        }

        Trace.WriteLine("[Harbor] DisplayChangeService: Disposed.");
    }
}
