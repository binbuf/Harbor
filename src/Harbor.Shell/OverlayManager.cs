using System.Collections.Concurrent;
using System.Diagnostics;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

/// <summary>
/// Manages the lifecycle of overlay windows that sit on top of application title bars.
/// One overlay per tracked window, keyed by HWND.
/// Routes traffic light button clicks to the target window via WindowCommandService.
/// Integrates with OverlaySyncService for dual-layer synchronization (event-driven + polling).
/// Supports fullscreen retreat: hides overlays on a specific monitor and suppresses new creation.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly WindowEventManager _eventManager;
    private readonly TitleBarDiscoveryService _titleBarService;
    private readonly WindowCommandService _commandService;
    private readonly TitleBarColorService? _colorService;
    private readonly OverlaySyncService _syncService;
    private readonly ConcurrentDictionary<HWND, OverlayWindow> _overlays = new();

    // Set of monitor handles where fullscreen retreat is active — overlays are suppressed on these monitors
    private readonly HashSet<IntPtr> _retreatedMonitors = new();
    // Overlays hidden during retreat, keyed by monitor handle, for restore
    private readonly ConcurrentDictionary<IntPtr, List<HWND>> _hiddenOverlays = new();

    private Guid _foregroundSubscription;
    private Guid _locationSubscription;
    private Guid _destroySubscription;
    private bool _disposed;
    private HWND _activeHwnd;

    public OverlayManager(
        WindowEventManager eventManager,
        TitleBarDiscoveryService titleBarService,
        WindowCommandService commandService,
        OverlaySyncService syncService,
        TitleBarColorService? colorService = null)
    {
        _eventManager = eventManager;
        _titleBarService = titleBarService;
        _commandService = commandService;
        _syncService = syncService;
        _colorService = colorService;

        _syncService.MonitorChanged += OnMonitorChanged;

        _foregroundSubscription = _eventManager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
            MarshalToUiThread = true,
            Handler = OnForegroundChanged,
        });

        _locationSubscription = _eventManager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.LocationChange },
            MarshalToUiThread = true,
            Handler = OnLocationChanged,
        });

        _destroySubscription = _eventManager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.ObjectDestroy },
            MarshalToUiThread = true,
            Handler = OnWindowDestroyed,
        });

        Trace.WriteLine("[Harbor] OverlayManager: Initialized with dual-layer sync.");
    }

    /// <summary>
    /// Returns the number of active overlays.
    /// </summary>
    public int OverlayCount => _overlays.Count;

    /// <summary>
    /// Returns true if an overlay exists for the given target HWND.
    /// </summary>
    public bool HasOverlay(HWND hwnd) => _overlays.ContainsKey(hwnd);

    /// <summary>
    /// Gets the overlay for a given target HWND, or null if none exists.
    /// </summary>
    public OverlayWindow? GetOverlay(HWND hwnd)
    {
        _overlays.TryGetValue(hwnd, out var overlay);
        return overlay;
    }

    /// <summary>
    /// Returns the sync service for instrumentation access.
    /// </summary>
    public OverlaySyncService SyncService => _syncService;

    /// <summary>
    /// Returns true if fullscreen retreat is active on the given monitor.
    /// </summary>
    public bool IsRetreated(IntPtr monitorHandle) => _retreatedMonitors.Contains(monitorHandle);

    /// <summary>
    /// Hides all overlays on the specified monitor and suppresses new overlay creation there.
    /// Called when a fullscreen app is detected on that monitor.
    /// </summary>
    public void Retreat(IntPtr monitorHandle)
    {
        if (_disposed) return;
        if (!_retreatedMonitors.Add(monitorHandle)) return; // already retreated

        Trace.WriteLine($"[Harbor] OverlayManager: Retreating overlays on monitor {monitorHandle}.");

        var hidden = new List<HWND>();

        foreach (var kvp in _overlays)
        {
            var hwnd = kvp.Key;
            var overlay = kvp.Value;
            var overlayMonitor = DisplayInterop.GetMonitorForWindow(hwnd);
            if (overlayMonitor == monitorHandle)
            {
                _syncService.Untrack(hwnd);
                overlay.Hide();
                hidden.Add(hwnd);
            }
        }

        _hiddenOverlays[monitorHandle] = hidden;
        Trace.WriteLine($"[Harbor] OverlayManager: Retreated {hidden.Count} overlays on monitor {monitorHandle}.");
    }

    /// <summary>
    /// Restores overlays on the specified monitor after fullscreen exit.
    /// Re-enables overlay creation and re-shows previously hidden overlays.
    /// </summary>
    public void Restore(IntPtr monitorHandle)
    {
        if (_disposed) return;
        if (!_retreatedMonitors.Remove(monitorHandle)) return; // wasn't retreated

        Trace.WriteLine($"[Harbor] OverlayManager: Restoring overlays on monitor {monitorHandle}.");

        if (_hiddenOverlays.TryRemove(monitorHandle, out var hidden))
        {
            foreach (var hwnd in hidden)
            {
                if (!WindowInterop.IsWindow(hwnd) || !WindowInterop.IsWindowVisible(hwnd))
                {
                    // Window went away during retreat — remove the overlay
                    DestroyOverlay(hwnd);
                    continue;
                }

                if (_overlays.TryGetValue(hwnd, out var overlay))
                {
                    overlay.Show();

                    // Re-discover title bar and re-register sync tracking
                    var titleBarInfo = _titleBarService.Discover(hwnd);
                    if (titleBarInfo is not null)
                    {
                        overlay.Reposition(titleBarInfo.Rect);
                        UpdateSyncTracking(overlay, hwnd, titleBarInfo.Rect);
                    }
                }
            }
        }

        Trace.WriteLine($"[Harbor] OverlayManager: Restored overlays on monitor {monitorHandle}.");
    }

    /// <summary>
    /// Ensures an overlay exists for the given window. Creates one if it doesn't exist.
    /// Repositions the overlay to match the current title bar.
    /// </summary>
    public OverlayWindow? EnsureOverlay(HWND hwnd)
    {
        if (_disposed) return null;
        if (hwnd == HWND.Null) return null;

        // Suppress overlay creation on retreated monitors
        var monitor = DisplayInterop.GetMonitorForWindow(hwnd);
        if (_retreatedMonitors.Contains(monitor))
            return null;

        // Check skip list — don't overlay frameless/custom-chrome windows
        if (_titleBarService.IsSkipped(hwnd))
            return null;

        // Discover the title bar rectangle
        var titleBarInfo = _titleBarService.Discover(hwnd);
        if (titleBarInfo is null)
            return null;

        if (_overlays.TryGetValue(hwnd, out var existing))
        {
            // Reposition existing overlay and update sync tracking
            existing.Reposition(titleBarInfo.Rect);
            UpdateSyncTracking(existing, hwnd, titleBarInfo.Rect);
            ApplyMaskColor(existing, hwnd, titleBarInfo.Rect);
            return existing;
        }

        // Create new overlay
        var overlay = new OverlayWindow(hwnd);
        overlay.ButtonClicked += OnButtonClicked;
        overlay.Show();
        overlay.Reposition(titleBarInfo.Rect);
        overlay.UpdateZOrder();
        UpdateOverlayState(overlay, hwnd);
        UpdateSyncTracking(overlay, hwnd, titleBarInfo.Rect);
        ApplyMaskColor(overlay, hwnd, titleBarInfo.Rect);

        if (_overlays.TryAdd(hwnd, overlay))
        {
            Trace.WriteLine($"[Harbor] OverlayManager: Created overlay for HWND {hwnd}");
            return overlay;
        }

        // Race condition — another thread created the overlay first
        _syncService.Untrack(hwnd);
        overlay.Close();
        _overlays.TryGetValue(hwnd, out var winner);
        return winner;
    }

    /// <summary>
    /// Destroys the overlay for the given target window.
    /// </summary>
    public void DestroyOverlay(HWND hwnd)
    {
        if (_overlays.TryRemove(hwnd, out var overlay))
        {
            _syncService.Untrack(hwnd);
            overlay.ButtonClicked -= OnButtonClicked;
            overlay.Close();
            Trace.WriteLine($"[Harbor] OverlayManager: Destroyed overlay for HWND {hwnd}");
        }
    }

    /// <summary>
    /// Registers or updates sync tracking for an overlay based on current window/title bar geometry.
    /// </summary>
    private void UpdateSyncTracking(OverlayWindow overlay, HWND hwnd, RECT titleBarRect)
    {
        if (overlay.OverlayHwnd == HWND.Null) return;

        if (!WindowInterop.GetWindowRect(hwnd, out var windowRect))
            return;

        var offset = TitleBarOffset.Compute(windowRect, titleBarRect);
        overlay.SetTitleBarOffset(windowRect, titleBarRect);

        if (_syncService.IsTracking(hwnd))
        {
            _syncService.UpdateOffset(hwnd, windowRect, offset);
        }
        else
        {
            _syncService.Track(hwnd, overlay.OverlayHwnd, windowRect, offset);
        }
    }

    private void OnButtonClicked(HWND hwnd, TrafficLightAction action)
    {
        _commandService.Execute(hwnd, action);

        if (action == TrafficLightAction.Close)
        {
            // Remove the overlay after sending close — the window will handle its own lifecycle
            DestroyOverlay(hwnd);
        }
        else
        {
            // After minimize/maximize/restore, update the overlay state
            // Use a short delay to allow the window to process the command
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () =>
                {
                    if (_overlays.TryGetValue(hwnd, out var overlay))
                    {
                        UpdateOverlayState(overlay, hwnd);
                    }
                });
        }
    }

    private void ApplyMaskColor(OverlayWindow overlay, HWND hwnd, RECT titleBarRect)
    {
        if (_colorService is null) return;

        try
        {
            var colorInfo = _colorService.Detect(hwnd, titleBarRect);
            overlay.SetMaskColor(colorInfo.Color);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] OverlayManager: Failed to apply mask color: {ex.Message}");
        }
    }

    private static void UpdateOverlayState(OverlayWindow overlay, HWND hwnd)
    {
        overlay.SetCapabilities(
            WindowCommandService.CanMinimize(hwnd),
            WindowCommandService.CanMaximize(hwnd));
        overlay.SetMaximized(WindowCommandService.IsMaximized(hwnd));
    }

    /// <summary>
    /// Called when a tracked window crosses a monitor boundary.
    /// WPF windows cannot change their DPI context after creation,
    /// so the overlay must be destroyed and recreated on the new monitor.
    /// </summary>
    private void OnMonitorChanged(HWND hwnd)
    {
        if (_disposed) return;

        // Marshal to UI thread since we need to create/destroy WPF windows
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;

            Trace.WriteLine($"[Harbor] OverlayManager: Monitor changed for HWND {hwnd}, recreating overlay.");

            // Destroy old overlay (untrack handled inside)
            DestroyOverlay(hwnd);

            // Recreate on new monitor's DPI context
            var overlay = EnsureOverlay(hwnd);

            // Restore active state if this was the foreground window
            if (overlay is not null && hwnd == _activeHwnd)
            {
                overlay.SetActive(true);
                overlay.UpdateZOrder();
            }
        });
    }

    private void OnForegroundChanged(WindowEventArgs args)
    {
        if (_disposed) return;

        // Mark previous foreground overlay as inactive
        if (_activeHwnd != HWND.Null && _overlays.TryGetValue(_activeHwnd, out var previousOverlay))
        {
            previousOverlay.SetActive(false);
        }

        _activeHwnd = args.WindowHandle;

        // Create or update overlay for the newly foreground window
        EnsureOverlay(args.WindowHandle);

        // Mark new foreground overlay as active and update z-order
        if (_overlays.TryGetValue(args.WindowHandle, out var overlay))
        {
            overlay.SetActive(true);
            overlay.UpdateZOrder();
        }
    }

    private void OnLocationChanged(WindowEventArgs args)
    {
        if (_disposed) return;

        // Suppress location change processing for windows on retreated monitors
        var monitor = DisplayInterop.GetMonitorForWindow(args.WindowHandle);
        if (_retreatedMonitors.Contains(monitor))
            return;

        // Only reposition if we have an overlay for this window
        if (!_overlays.TryGetValue(args.WindowHandle, out var overlay))
            return;

        // Layer 1 fast path: use OverlaySyncService to reposition from cached offset
        // This avoids full UIA title bar re-discovery for every location change
        var fastResult = _syncService.RepositionFromEvent(args.WindowHandle);
        if (fastResult.HasValue)
        {
            // Update maximized state on location change (fires when window is maximized/restored)
            var isMaximized = WindowCommandService.IsMaximized(args.WindowHandle);
            overlay.SetMaximized(isMaximized);

            // If maximized state changed, the title bar geometry may have changed —
            // do a full re-discovery to update the offset
            if (isMaximized != overlay.TitleBarOffset.Height > 0)
            {
                var titleBarInfo = _titleBarService.Discover(args.WindowHandle);
                if (titleBarInfo is not null)
                {
                    UpdateSyncTracking(overlay, args.WindowHandle, titleBarInfo.Rect);
                }
            }

            return;
        }

        // Fallback: full re-discovery (sync service didn't have this window tracked)
        var info = _titleBarService.Discover(args.WindowHandle);
        if (info is null)
        {
            // Window may have gone frameless or been minimized — destroy overlay
            DestroyOverlay(args.WindowHandle);
            return;
        }

        overlay.Reposition(info.Rect);
        UpdateSyncTracking(overlay, args.WindowHandle, info.Rect);

        // Update maximized state on location change (fires when window is maximized/restored)
        overlay.SetMaximized(WindowCommandService.IsMaximized(args.WindowHandle));
    }

    private void OnWindowDestroyed(WindowEventArgs args)
    {
        DestroyOverlay(args.WindowHandle);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _syncService.MonitorChanged -= OnMonitorChanged;
        _eventManager.Unsubscribe(_foregroundSubscription);
        _eventManager.Unsubscribe(_locationSubscription);
        _eventManager.Unsubscribe(_destroySubscription);

        // Close all overlay windows
        foreach (var kvp in _overlays)
        {
            try
            {
                _syncService.Untrack(kvp.Key);
                kvp.Value.ButtonClicked -= OnButtonClicked;
                kvp.Value.Close();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] OverlayManager: Error closing overlay: {ex.Message}");
            }
        }

        _overlays.Clear();

        Trace.WriteLine("[Harbor] OverlayManager: Disposed.");
    }
}
