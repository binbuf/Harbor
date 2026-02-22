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
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly WindowEventManager _eventManager;
    private readonly TitleBarDiscoveryService _titleBarService;
    private readonly WindowCommandService _commandService;
    private readonly TitleBarColorService? _colorService;
    private readonly ConcurrentDictionary<HWND, OverlayWindow> _overlays = new();

    private Guid _foregroundSubscription;
    private Guid _locationSubscription;
    private Guid _destroySubscription;
    private bool _disposed;
    private HWND _activeHwnd;

    public OverlayManager(
        WindowEventManager eventManager,
        TitleBarDiscoveryService titleBarService,
        WindowCommandService commandService,
        TitleBarColorService? colorService = null)
    {
        _eventManager = eventManager;
        _titleBarService = titleBarService;
        _commandService = commandService;
        _colorService = colorService;

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

        Trace.WriteLine("[Harbor] OverlayManager: Initialized.");
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
    /// Ensures an overlay exists for the given window. Creates one if it doesn't exist.
    /// Repositions the overlay to match the current title bar.
    /// </summary>
    public OverlayWindow? EnsureOverlay(HWND hwnd)
    {
        if (_disposed) return null;
        if (hwnd == HWND.Null) return null;

        // Check skip list — don't overlay frameless/custom-chrome windows
        if (_titleBarService.IsSkipped(hwnd))
            return null;

        // Discover the title bar rectangle
        var titleBarInfo = _titleBarService.Discover(hwnd);
        if (titleBarInfo is null)
            return null;

        if (_overlays.TryGetValue(hwnd, out var existing))
        {
            // Reposition existing overlay
            existing.Reposition(titleBarInfo.Rect);
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
        ApplyMaskColor(overlay, hwnd, titleBarInfo.Rect);

        if (_overlays.TryAdd(hwnd, overlay))
        {
            Trace.WriteLine($"[Harbor] OverlayManager: Created overlay for HWND {hwnd}");
            return overlay;
        }

        // Race condition — another thread created the overlay first
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
            overlay.ButtonClicked -= OnButtonClicked;
            overlay.Close();
            Trace.WriteLine($"[Harbor] OverlayManager: Destroyed overlay for HWND {hwnd}");
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

        // Only reposition if we have an overlay for this window
        if (!_overlays.TryGetValue(args.WindowHandle, out var overlay))
            return;

        // Re-discover title bar (cache was invalidated by TitleBarDiscoveryService)
        var titleBarInfo = _titleBarService.Discover(args.WindowHandle);
        if (titleBarInfo is null)
        {
            // Window may have gone frameless or been minimized — destroy overlay
            DestroyOverlay(args.WindowHandle);
            return;
        }

        overlay.Reposition(titleBarInfo.Rect);

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

        _eventManager.Unsubscribe(_foregroundSubscription);
        _eventManager.Unsubscribe(_locationSubscription);
        _eventManager.Unsubscribe(_destroySubscription);

        // Close all overlay windows
        foreach (var kvp in _overlays)
        {
            try
            {
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
