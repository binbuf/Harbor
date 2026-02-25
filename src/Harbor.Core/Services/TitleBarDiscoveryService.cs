using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Harbor.Core.Services;

/// <summary>
/// Identifies the UI framework of a target window.
/// </summary>
public enum UIFramework
{
    Unknown,
    Win32,
    Wpf,
    Uwp,
    WinUI3,
    Electron,
    Java,
    Qt,
}

/// <summary>
/// Result of title bar discovery for a window.
/// </summary>
public sealed class TitleBarInfo
{
    /// <summary>
    /// The window handle this info applies to.
    /// </summary>
    public required HWND Hwnd { get; init; }

    /// <summary>
    /// Bounding rectangle of the title bar in screen coordinates.
    /// </summary>
    public required RECT Rect { get; init; }

    /// <summary>
    /// The detected UI framework of the window.
    /// </summary>
    public required UIFramework Framework { get; init; }

    /// <summary>
    /// Whether UIA was used (true) or NONCLIENT fallback (false).
    /// </summary>
    public required bool UsedUia { get; init; }
}

/// <summary>
/// Discovers the title bar bounding rectangle for application windows using
/// UI Automation as the primary method and NONCLIENT heuristics as fallback.
/// Maintains a session-scoped skip list for frameless windows and caches results by HWND.
/// </summary>
public sealed class TitleBarDiscoveryService : IDisposable
{
    /// <summary>
    /// Timeout for UIA queries to handle hung applications (Section 11B).
    /// </summary>
    public static readonly TimeSpan UiaTimeout = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentDictionary<HWND, TitleBarInfo> _cache = new();
    private readonly ConcurrentDictionary<HWND, byte> _skipList = new();
    private readonly WindowEventManager? _eventManager;
    private Guid _locationSubscription;
    private Guid _destroySubscription;
    private bool _disposed;

    /// <summary>
    /// Creates a new TitleBarDiscoveryService.
    /// </summary>
    /// <param name="eventManager">
    /// Optional WindowEventManager for automatic cache invalidation on location changes
    /// and skip list cleanup on window destruction.
    /// </param>
    public TitleBarDiscoveryService(WindowEventManager? eventManager = null)
    {
        _eventManager = eventManager;

        if (_eventManager is not null)
        {
            _locationSubscription = _eventManager.Subscribe(new WindowEventSubscription
            {
                EventTypes = new HashSet<WindowEventType> { WindowEventType.LocationChange },
                Handler = OnLocationChanged,
            });

            _destroySubscription = _eventManager.Subscribe(new WindowEventSubscription
            {
                EventTypes = new HashSet<WindowEventType> { WindowEventType.ObjectDestroy },
                Handler = OnWindowDestroyed,
            });
        }

        Trace.WriteLine("[Harbor] TitleBarDiscoveryService: Initialized.");
    }

    /// <summary>
    /// Returns true if the given HWND is in the skip list (frameless / custom chrome).
    /// </summary>
    public bool IsSkipped(HWND hwnd) => _skipList.ContainsKey(hwnd);

    /// <summary>
    /// Returns the number of cached title bar entries.
    /// </summary>
    public int CacheCount => _cache.Count;

    /// <summary>
    /// Returns the number of entries in the skip list.
    /// </summary>
    public int SkipListCount => _skipList.Count;

    /// <summary>
    /// Discovers the title bar rectangle for the given window handle.
    /// Returns null if the window is in the skip list or discovery fails entirely.
    /// </summary>
    public TitleBarInfo? Discover(HWND hwnd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (hwnd == HWND.Null)
            return null;

        // Check skip list first
        if (_skipList.ContainsKey(hwnd))
            return null;

        // Check cache
        if (_cache.TryGetValue(hwnd, out var cached))
            return cached;

        // Detect framework
        var framework = DetectFramework(hwnd);

        // Try UIA primary discovery
        var info = TryUiaDiscovery(hwnd, framework);

        // If UIA failed, try framework-specific fallbacks
        if (info is null)
        {
            info = framework switch
            {
                UIFramework.Uwp or UIFramework.WinUI3 => TryDwmCaptionFallback(hwnd, framework),
                _ => null,
            };
        }

        // If still no result, try NONCLIENT fallback
        info ??= TryNonClientFallback(hwnd, framework);

        // Cache the result if we got one
        if (info is not null)
        {
            _cache[hwnd] = info;
        }

        return info;
    }

    /// <summary>
    /// Invalidates the cached result for a specific window.
    /// </summary>
    public void InvalidateCache(HWND hwnd)
    {
        _cache.TryRemove(hwnd, out _);
    }

    /// <summary>
    /// Clears all cached results.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Removes a window from the skip list, allowing future discovery attempts.
    /// </summary>
    public bool RemoveFromSkipList(HWND hwnd)
    {
        return _skipList.TryRemove(hwnd, out _);
    }

    /// <summary>
    /// Detects the UI framework of the window at the given handle.
    /// </summary>
    public static UIFramework DetectFramework(HWND hwnd)
    {
        if (hwnd == HWND.Null)
            return UIFramework.Unknown;

        // Check process name first for reliable detection
        var processName = GetProcessName(hwnd);
        if (processName is not null)
        {
            var lower = processName.ToLowerInvariant();

            if (lower is "electron" or "electron.exe")
                return UIFramework.Electron;

            if (lower is "java" or "javaw" or "java.exe" or "javaw.exe")
                return UIFramework.Java;
        }

        // Try UIA FrameworkId (with timeout protection)
        try
        {
            var frameworkId = GetFrameworkIdWithTimeout(hwnd);

            if (frameworkId is not null)
            {
                return frameworkId switch
                {
                    "Win32" => UIFramework.Win32,
                    "WPF" => UIFramework.Wpf,
                    "XAML" => UIFramework.WinUI3,
                    "DirectUI" => UIFramework.Uwp,
                    "Chrome" => UIFramework.Electron,
                    "Qt" => UIFramework.Qt,
                    _ => UIFramework.Unknown,
                };
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: FrameworkId query failed: {ex.Message}");
        }

        return UIFramework.Unknown;
    }

    /// <summary>
    /// Attempts UIA title bar discovery. Returns null on failure or timeout.
    /// </summary>
    internal unsafe TitleBarInfo? TryUiaDiscovery(HWND hwnd, UIFramework framework)
    {
        try
        {
            RECT? rect = null;
            var handle = (nint)hwnd.Value;

            // Run UIA query on a thread pool thread with timeout
            var task = Task.Run(() =>
            {
                try
                {
                    var element = AutomationElement.FromHandle(handle);
                    if (element is null) return;

                    var titleBar = element.FindFirst(
                        TreeScope.Children,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TitleBar));

                    if (titleBar is null) return;

                    var bounds = titleBar.Current.BoundingRectangle;
                    if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                        return;

                    rect = new RECT
                    {
                        left = (int)bounds.Left,
                        top = (int)bounds.Top,
                        right = (int)(bounds.Left + bounds.Width),
                        bottom = (int)(bounds.Top + bounds.Height),
                    };
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: UIA query failed for HWND {handle}: {ex.Message}");
                }
            });

            if (!task.Wait(UiaTimeout))
            {
                Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: UIA timeout for HWND {(nint)hwnd.Value}");
                return null;
            }

            if (task.IsFaulted)
            {
                Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: UIA faulted for HWND {(nint)hwnd.Value}: {task.Exception?.InnerException?.Message}");
                return null;
            }

            if (rect is null)
                return null;

            return new TitleBarInfo
            {
                Hwnd = hwnd,
                Rect = rect.Value,
                Framework = framework,
                UsedUia = true,
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: UIA discovery failed for HWND {(nint)hwnd.Value}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fallback for UWP/WinUI3 windows using DwmGetWindowAttribute(DWMWA_CAPTION_BUTTON_BOUNDS).
    /// </summary>
    internal TitleBarInfo? TryDwmCaptionFallback(HWND hwnd, UIFramework framework)
    {
        try
        {
            var hr = DwmInterop.GetWindowAttribute(
                hwnd,
                (DWMWINDOWATTRIBUTE)DwmInterop.DWMWA_CAPTION_BUTTON_BOUNDS,
                out RECT captionBounds);

            if (hr.Failed)
                return null;

            var height = captionBounds.bottom - captionBounds.top;
            if (height <= 0)
                return null;

            // Get window rect to build full title bar rect
            if (!WindowInterop.GetWindowRect(hwnd, out var windowRect))
                return null;

            var titleRect = new RECT
            {
                left = windowRect.left,
                top = windowRect.top,
                right = windowRect.right,
                bottom = windowRect.top + height,
            };

            return new TitleBarInfo
            {
                Hwnd = hwnd,
                Rect = titleRect,
                Framework = framework,
                UsedUia = false,
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: DWM caption fallback failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// NONCLIENT fallback algorithm: computes title bar height from
    /// windowRect vs clientRect difference.
    /// Adds frameless windows to the skip list.
    /// </summary>
    internal unsafe TitleBarInfo? TryNonClientFallback(HWND hwnd, UIFramework framework)
    {
        if (!WindowInterop.GetWindowRect(hwnd, out var windowRect))
            return null;

        var height = ComputeNonClientHeight(hwnd, windowRect);

        // Zero or negative NONCLIENT height means custom chrome — skip
        if (height <= 0)
        {
            _skipList[hwnd] = 0;
            Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: Frameless window added to skip list: HWND {(nint)hwnd.Value}");
            return null;
        }

        var titleRect = new RECT
        {
            left = windowRect.left,
            top = windowRect.top,
            right = windowRect.right,
            bottom = windowRect.top + height,
        };

        return new TitleBarInfo
        {
            Hwnd = hwnd,
            Rect = titleRect,
            Framework = framework,
            UsedUia = false,
        };
    }

    /// <summary>
    /// Computes the NONCLIENT height for a window.
    /// This is the difference between the window top and the client area top,
    /// mapped to screen coordinates.
    /// </summary>
    public static int ComputeNonClientHeight(HWND hwnd, RECT windowRect)
    {
        if (!WindowInterop.GetClientRect(hwnd, out var clientRect))
            return -1;

        // GetClientRect returns coordinates relative to the client area (top-left is 0,0).
        // We need to map the client rect to screen coordinates to compare with windowRect.
        // The client area top in screen coords = windowRect.top + nonClientHeight.
        // Therefore nonClientHeight = (clientRect area offset from window top).
        // We can compute this using: windowRect height - clientRect height gives total
        // NONCLIENT (top + bottom borders). For just the title bar, we use
        // ClientToScreen or the simpler heuristic.

        // Use the window rect dimensions vs client rect dimensions.
        var windowHeight = windowRect.bottom - windowRect.top;
        var windowWidth = windowRect.right - windowRect.left;
        var clientHeight = clientRect.bottom - clientRect.top;
        var clientWidth = clientRect.right - clientRect.left;

        // Total non-client vertical = windowHeight - clientHeight
        // Total non-client horizontal = windowWidth - clientWidth
        // Border width (each side) = totalHorizontalNC / 2
        // Title bar height = totalVerticalNC - borderWidth (bottom border)
        var totalVerticalNc = windowHeight - clientHeight;
        var totalHorizontalNc = windowWidth - clientWidth;
        var borderWidth = totalHorizontalNc / 2;

        // Title bar height = total vertical NC - bottom border
        var titleBarHeight = totalVerticalNc - borderWidth;

        return titleBarHeight;
    }

    /// <summary>
    /// Gets the process name for a window handle.
    /// </summary>
    internal static string? GetProcessName(HWND hwnd)
    {
        try
        {
            WindowInterop.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return null;

            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the UIA FrameworkId with timeout protection.
    /// </summary>
    private static unsafe string? GetFrameworkIdWithTimeout(HWND hwnd)
    {
        string? frameworkId = null;
        var handle = (nint)hwnd.Value;

        var task = Task.Run(() =>
        {
            try
            {
                var element = AutomationElement.FromHandle(handle);
                frameworkId = element?.Current.FrameworkId;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] TitleBarDiscoveryService: FrameworkId query failed for HWND {handle}: {ex.Message}");
            }
        });

        if (!task.Wait(UiaTimeout))
            return null;

        if (task.IsFaulted)
            return null;

        return frameworkId;
    }

    private void OnLocationChanged(WindowEventArgs args)
    {
        // Invalidate cache so next Discover() call re-queries
        _cache.TryRemove(args.WindowHandle, out _);
    }

    private void OnWindowDestroyed(WindowEventArgs args)
    {
        // Clean up both cache and skip list for destroyed windows
        _cache.TryRemove(args.WindowHandle, out _);
        _skipList.TryRemove(args.WindowHandle, out _);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_eventManager is not null)
        {
            _eventManager.Unsubscribe(_locationSubscription);
            _eventManager.Unsubscribe(_destroySubscription);
        }

        _cache.Clear();
        _skipList.Clear();

        Trace.WriteLine("[Harbor] TitleBarDiscoveryService: Disposed.");
    }
}
