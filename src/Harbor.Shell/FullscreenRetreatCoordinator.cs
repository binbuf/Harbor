using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using ManagedShell.AppBar;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

/// <summary>
/// State of fullscreen retreat for a specific monitor.
/// </summary>
public enum RetreatState
{
    Normal,
    Retreated,
}

/// <summary>
/// Per-monitor retreat tracking.
/// </summary>
internal sealed class MonitorRetreatInfo
{
    public RetreatState State { get; set; } = RetreatState.Normal;
    public HWND FullscreenHwnd { get; set; }
}

/// <summary>
/// Coordinates fullscreen detection with retreat/restore of Harbor UI elements.
/// Listens for EVENT_SYSTEM_FOREGROUND events and checks the new foreground window
/// for fullscreen status. Hides/shows AppBars and overlays per-monitor.
/// </summary>
public sealed class FullscreenRetreatCoordinator : IDisposable
{
    private readonly FullscreenDetectionService _detectionService;
    private readonly WindowEventManager _eventManager;
    private readonly OverlayManager _overlayManager;
    private readonly ConcurrentDictionary<IntPtr, MonitorRetreatInfo> _monitors = new();

    // Retreatable UI elements keyed by monitor handle — only primary monitor for now
    private readonly ConcurrentDictionary<IntPtr, List<IRetreatable>> _retreatables = new();

    private Guid _foregroundSubscription;
    private bool _disposed;

    public FullscreenRetreatCoordinator(
        FullscreenDetectionService detectionService,
        WindowEventManager eventManager,
        OverlayManager overlayManager)
    {
        _detectionService = detectionService;
        _eventManager = eventManager;
        _overlayManager = overlayManager;

        _foregroundSubscription = _eventManager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
            MarshalToUiThread = true,
            Handler = OnForegroundChanged,
        });

        Trace.WriteLine("[Harbor] FullscreenRetreatCoordinator: Initialized.");
    }

    /// <summary>
    /// Registers a retreatable UI element on a specific monitor.
    /// </summary>
    public void RegisterRetreatable(IRetreatable retreatable, IntPtr monitorHandle)
    {
        var list = _retreatables.GetOrAdd(monitorHandle, _ => new List<IRetreatable>());
        lock (list)
        {
            list.Add(retreatable);
        }
    }

    /// <summary>
    /// Registers an AppBar window on a specific monitor for retreat management.
    /// Wraps it in an adapter that implements IRetreatable.
    /// </summary>
    public void RegisterAppBar(AppBarWindow appBar, IntPtr monitorHandle)
    {
        RegisterRetreatable(new AppBarWindowRetreatable(appBar), monitorHandle);
    }

    /// <summary>
    /// Adapter that wraps an AppBarWindow as an IRetreatable.
    /// </summary>
    private sealed class AppBarWindowRetreatable(AppBarWindow appBar) : IRetreatable
    {
        public Visibility Visibility
        {
            get => appBar.Visibility;
            set => appBar.Visibility = value;
        }

        public void UpdatePosition() => appBar.UpdatePosition();
    }

    /// <summary>
    /// Returns the current retreat state for a monitor.
    /// </summary>
    public RetreatState GetState(IntPtr monitorHandle)
    {
        return _monitors.TryGetValue(monitorHandle, out var info) ? info.State : RetreatState.Normal;
    }

    private void OnForegroundChanged(WindowEventArgs args)
    {
        if (_disposed) return;

        var hwnd = args.WindowHandle;
        var info = _detectionService.Classify(hwnd);

        if (info.IsFullscreen)
        {
            EnterRetreat(info.MonitorHandle, hwnd);
        }
        else
        {
            // Check if any retreated monitor should be restored
            // The foreground window may be on a monitor that was retreated
            foreach (var kvp in _monitors)
            {
                if (kvp.Value.State != RetreatState.Retreated) continue;

                // Restore if the fullscreen window is no longer foreground on this monitor,
                // or if the foreground window is on this monitor and not fullscreen
                var fullscreenHwnd = kvp.Value.FullscreenHwnd;
                var currentForeground = WindowInterop.GetForegroundWindow();

                if (currentForeground != fullscreenHwnd)
                {
                    // Re-check: maybe the fullscreen window is still fullscreen but lost focus
                    var recheck = _detectionService.Classify(fullscreenHwnd);
                    if (!recheck.IsFullscreen || recheck.MonitorHandle != kvp.Key)
                    {
                        ExitRetreat(kvp.Key);
                    }
                }
            }
        }
    }

    private void EnterRetreat(IntPtr monitorHandle, HWND fullscreenHwnd)
    {
        var info = _monitors.GetOrAdd(monitorHandle, _ => new MonitorRetreatInfo());

        if (info.State == RetreatState.Retreated)
        {
            // Already retreated, just update the fullscreen HWND
            info.FullscreenHwnd = fullscreenHwnd;
            return;
        }

        info.State = RetreatState.Retreated;
        info.FullscreenHwnd = fullscreenHwnd;

        Trace.WriteLine($"[Harbor] FullscreenRetreatCoordinator: Entering retreat on monitor {monitorHandle} for HWND {fullscreenHwnd}.");

        // Hide overlays on this monitor
        _overlayManager.Retreat(monitorHandle);

        // Hide retreatable UI elements on this monitor
        if (_retreatables.TryGetValue(monitorHandle, out var retreatables))
        {
            lock (retreatables)
            {
                foreach (var retreatable in retreatables)
                {
                    retreatable.Visibility = Visibility.Collapsed;
                    Trace.WriteLine($"[Harbor] FullscreenRetreatCoordinator: Collapsed retreatable on monitor {monitorHandle}.");
                }
            }
        }
    }

    private void ExitRetreat(IntPtr monitorHandle)
    {
        if (!_monitors.TryGetValue(monitorHandle, out var info)) return;
        if (info.State != RetreatState.Retreated) return;

        info.State = RetreatState.Normal;
        info.FullscreenHwnd = HWND.Null;

        Trace.WriteLine($"[Harbor] FullscreenRetreatCoordinator: Exiting retreat on monitor {monitorHandle}.");

        // Restore overlays on this monitor
        _overlayManager.Restore(monitorHandle);

        // Restore retreatable UI elements on this monitor
        if (_retreatables.TryGetValue(monitorHandle, out var retreatables))
        {
            lock (retreatables)
            {
                foreach (var retreatable in retreatables)
                {
                    retreatable.Visibility = Visibility.Visible;
                    retreatable.UpdatePosition();
                    Trace.WriteLine($"[Harbor] FullscreenRetreatCoordinator: Restored retreatable on monitor {monitorHandle}.");
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _eventManager.Unsubscribe(_foregroundSubscription);

        // Restore all retreated monitors
        foreach (var kvp in _monitors)
        {
            if (kvp.Value.State == RetreatState.Retreated)
            {
                ExitRetreat(kvp.Key);
            }
        }

        _monitors.Clear();
        _retreatables.Clear();

        Trace.WriteLine("[Harbor] FullscreenRetreatCoordinator: Disposed.");
    }
}
