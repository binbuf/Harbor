using System.Diagnostics;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Monitors window positions and detects when any window overlaps the dock area
/// at the bottom of the primary monitor. Used for "WhenOverlapped" auto-hide mode.
/// </summary>
public sealed class DockOverlapMonitorService : IDisposable
{
    private readonly WindowEventManager _eventManager;
    private readonly int _dockZoneHeight;
    private readonly HashSet<HWND> _overlappingWindows = new();
    private Guid _subscriptionId;
    private bool _disposed;
    private bool _isOverlapped;
    private System.Threading.Timer? _debounceTimer;

    /// <summary>
    /// Raised when the overlap state changes. True = at least one window overlaps the dock zone.
    /// </summary>
    public event Action<bool>? OverlapChanged;

    /// <summary>
    /// Creates a new overlap monitor.
    /// </summary>
    /// <param name="eventManager">The window event manager to subscribe to.</param>
    /// <param name="dockZoneHeight">Height of the dock zone in physical pixels (default 82).</param>
    public DockOverlapMonitorService(WindowEventManager eventManager, int dockZoneHeight = 82)
    {
        _eventManager = eventManager;
        _dockZoneHeight = dockZoneHeight;

        _subscriptionId = _eventManager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType>
            {
                WindowEventType.LocationChange,
                WindowEventType.Foreground,
                WindowEventType.ObjectDestroy,
            },
            Handler = OnWindowEvent,
            MarshalToUiThread = false,
        });

        Trace.WriteLine("[Harbor] DockOverlapMonitorService: Started monitoring.");
    }

    private void OnWindowEvent(WindowEventArgs args)
    {
        if (_disposed) return;

        if (args.EventType == WindowEventType.ObjectDestroy)
        {
            // Window destroyed — remove from set
            if (_overlappingWindows.Remove(args.WindowHandle))
                ScheduleDebounce();
            return;
        }

        // Check if window overlaps the dock zone
        var hwnd = args.WindowHandle;

        // Skip minimized windows
        var style = WindowInterop.GetWindowStyle(hwnd);
        const uint WS_MINIMIZE = 0x20000000;
        if ((style & WS_MINIMIZE) != 0)
        {
            if (_overlappingWindows.Remove(hwnd))
                ScheduleDebounce();
            return;
        }

        // Get the window rect and primary screen height
        if (!WindowInterop.GetWindowRect(hwnd, out var rect))
        {
            _overlappingWindows.Remove(hwnd);
            ScheduleDebounce();
            return;
        }

        // Get primary monitor bounds
        var monitorBounds = DisplayInterop.GetMonitorBounds(hwnd);
        if (monitorBounds is null)
            return;

        var screenBottom = monitorBounds.Value.bottom;
        var dockTop = screenBottom - _dockZoneHeight;

        // Check if window extends into dock zone
        if (rect.bottom > dockTop && rect.top < screenBottom &&
            rect.right > monitorBounds.Value.left && rect.left < monitorBounds.Value.right)
        {
            if (_overlappingWindows.Add(hwnd))
                ScheduleDebounce();
        }
        else
        {
            if (_overlappingWindows.Remove(hwnd))
                ScheduleDebounce();
        }
    }

    private void ScheduleDebounce()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(_ => EvaluateOverlap(), null, 100, System.Threading.Timeout.Infinite);
    }

    private void EvaluateOverlap()
    {
        if (_disposed) return;

        var nowOverlapped = _overlappingWindows.Count > 0;
        if (nowOverlapped != _isOverlapped)
        {
            _isOverlapped = nowOverlapped;
            Trace.WriteLine($"[Harbor] DockOverlapMonitor: Overlap changed to {_isOverlapped}.");
            OverlapChanged?.Invoke(_isOverlapped);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _eventManager.Unsubscribe(_subscriptionId);
        _debounceTimer?.Dispose();
        _overlappingWindows.Clear();

        Trace.WriteLine("[Harbor] DockOverlapMonitorService: Disposed.");
    }
}
