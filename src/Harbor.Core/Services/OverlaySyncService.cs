using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Harbor.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Describes the title bar position relative to the window rect.
/// Used to compute title bar position from GetWindowRect without full UIA re-discovery.
/// </summary>
public readonly record struct TitleBarOffset(
    int LeftDelta,
    int TopDelta,
    int RightDelta,
    int Height)
{
    /// <summary>
    /// Computes from the original window rect and title bar rect at discovery time.
    /// </summary>
    public static TitleBarOffset Compute(RECT windowRect, RECT titleBarRect)
    {
        return new TitleBarOffset(
            LeftDelta: titleBarRect.left - windowRect.left,
            TopDelta: titleBarRect.top - windowRect.top,
            RightDelta: titleBarRect.right - windowRect.right,
            Height: titleBarRect.bottom - titleBarRect.top);
    }

    /// <summary>
    /// Applies this offset to a new window rect to produce the title bar rect.
    /// </summary>
    public RECT Apply(RECT windowRect)
    {
        return new RECT
        {
            left = windowRect.left + LeftDelta,
            top = windowRect.top + TopDelta,
            right = windowRect.right + RightDelta,
            bottom = windowRect.top + TopDelta + Height,
        };
    }
}

/// <summary>
/// Tracks an overlay and its target window for synchronization.
/// </summary>
internal sealed class TrackedOverlay
{
    public required HWND TargetHwnd { get; init; }
    public required HWND OverlayHwnd { get; init; }
    public TitleBarOffset Offset { get; set; }
    public RECT LastWindowRect { get; set; }
    public volatile bool IsMinimized;

    /// <summary>
    /// The monitor handle hosting this window at last check.
    /// Used to detect cross-monitor drag boundary crossings.
    /// </summary>
    public IntPtr LastMonitor { get; set; }
}

/// <summary>
/// Instrumentation snapshot for overlay synchronization performance.
/// </summary>
public sealed class SyncStats
{
    public long TotalUpdates { get; internal set; }
    public long PollingUpdates { get; internal set; }
    public long EventDrivenUpdates { get; internal set; }
    public long FrameMisses { get; internal set; }
    public double LastUpdateLatencyMs { get; internal set; }
    public double MaxUpdateLatencyMs { get; internal set; }
    public double P99UpdateLatencyMs { get; internal set; }

    /// <summary>
    /// Frame-miss rate as a fraction (0.0 to 1.0).
    /// A frame miss is when the update took longer than 16.6ms.
    /// </summary>
    public double FrameMissRate => TotalUpdates > 0 ? (double)FrameMisses / TotalUpdates : 0;
}

/// <summary>
/// Dual-layer overlay synchronization service.
/// Layer 1: Event-driven (called by OverlayManager on location change events).
/// Layer 2: High-frequency polling at ~120Hz on a dedicated background thread.
/// Both layers bypass WPF layout, using direct SetWindowPos P/Invoke.
/// </summary>
public sealed class OverlaySyncService : IDisposable
{
    private const int PollingIntervalMs = 8; // ~120Hz
    private const double FrameBudgetMs = 16.6; // 60Hz frame budget

    private readonly ConcurrentDictionary<HWND, TrackedOverlay> _tracked = new();
    private readonly Thread _pollingThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _perfTimer = Stopwatch.StartNew();

    /// <summary>
    /// Fired when a tracked window crosses a monitor boundary during drag.
    /// The HWND is the target window that changed monitors.
    /// WPF overlays must be destroyed and recreated on the new monitor's DPI context.
    /// </summary>
    public event Action<HWND>? MonitorChanged;

    // Instrumentation
    private long _totalUpdates;
    private long _pollingUpdates;
    private long _eventDrivenUpdates;
    private long _frameMisses;
    private double _lastLatencyMs;
    private double _maxLatencyMs;
    private readonly object _latencyLock = new();
    private readonly List<double> _recentLatencies = new(1024);

    private bool _disposed;

    public OverlaySyncService()
    {
        _pollingThread = new Thread(PollingLoop)
        {
            Name = "Harbor.OverlaySync",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        _pollingThread.Start();

        Trace.WriteLine("[Harbor] OverlaySyncService: Started polling thread at ~120Hz.");
    }

    /// <summary>
    /// Begins tracking an overlay for dual-layer synchronization.
    /// </summary>
    public void Track(HWND target, HWND overlay, RECT windowRect, TitleBarOffset offset)
    {
        var monitor = DisplayInterop.GetMonitorForWindow(target);

        var tracked = new TrackedOverlay
        {
            TargetHwnd = target,
            OverlayHwnd = overlay,
            Offset = offset,
            LastWindowRect = windowRect,
            LastMonitor = monitor,
        };

        _tracked[target] = tracked;
        Trace.WriteLine($"[Harbor] OverlaySyncService: Tracking HWND {target} on monitor {monitor}");
    }

    /// <summary>
    /// Stops tracking an overlay.
    /// </summary>
    public void Untrack(HWND target)
    {
        if (_tracked.TryRemove(target, out _))
        {
            Trace.WriteLine($"[Harbor] OverlaySyncService: Untracked HWND {target}");
        }
    }

    /// <summary>
    /// Updates the title bar offset for a tracked overlay (e.g., after maximize/restore changes title bar geometry).
    /// </summary>
    public void UpdateOffset(HWND target, RECT windowRect, TitleBarOffset offset)
    {
        if (_tracked.TryGetValue(target, out var tracked))
        {
            tracked.Offset = offset;
            tracked.LastWindowRect = windowRect;
        }
    }

    /// <summary>
    /// Layer 1: Event-driven fast reposition. Called on EVENT_OBJECT_LOCATIONCHANGE.
    /// Returns the computed title bar rect if the overlay was repositioned, or null if not tracked.
    /// </summary>
    public RECT? RepositionFromEvent(HWND target)
    {
        if (!_tracked.TryGetValue(target, out var tracked))
            return null;

        var startTicks = _perfTimer.ElapsedTicks;

        if (!WindowInterop.GetWindowRect(target, out var windowRect))
            return null;

        // Check if minimized
        if (PInvoke.IsIconic(target))
        {
            tracked.IsMinimized = true;
            return null;
        }

        tracked.IsMinimized = false;

        // Check for monitor boundary crossing
        var currentMonitor = DisplayInterop.GetMonitorForWindow(target);
        if (currentMonitor != tracked.LastMonitor)
        {
            Trace.WriteLine($"[Harbor] OverlaySyncService: HWND {target} crossed monitor boundary ({tracked.LastMonitor} → {currentMonitor})");
            tracked.LastMonitor = currentMonitor;
            MonitorChanged?.Invoke(target);
            // Don't reposition — the overlay will be destroyed and recreated by the handler
            return null;
        }

        // Compute title bar rect from offset
        var titleBarRect = tracked.Offset.Apply(windowRect);
        tracked.LastWindowRect = windowRect;

        // Reposition overlay via direct P/Invoke (bypass WPF layout)
        RepositionOverlay(tracked.OverlayHwnd, titleBarRect);

        // DWM frame sync
        DwmInterop.Flush();

        RecordLatency(startTicks);
        Interlocked.Increment(ref _eventDrivenUpdates);

        return titleBarRect;
    }

    /// <summary>
    /// Returns current instrumentation stats.
    /// </summary>
    public SyncStats GetStats()
    {
        lock (_latencyLock)
        {
            double p99 = 0;
            if (_recentLatencies.Count > 0)
            {
                var sorted = _recentLatencies.OrderBy(x => x).ToList();
                var index = (int)(sorted.Count * 0.99);
                if (index >= sorted.Count) index = sorted.Count - 1;
                p99 = sorted[index];
            }

            return new SyncStats
            {
                TotalUpdates = Interlocked.Read(ref _totalUpdates),
                PollingUpdates = Interlocked.Read(ref _pollingUpdates),
                EventDrivenUpdates = Interlocked.Read(ref _eventDrivenUpdates),
                FrameMisses = Interlocked.Read(ref _frameMisses),
                LastUpdateLatencyMs = _lastLatencyMs,
                MaxUpdateLatencyMs = _maxLatencyMs,
                P99UpdateLatencyMs = p99,
            };
        }
    }

    /// <summary>
    /// Returns true if the given window is currently being tracked.
    /// </summary>
    public bool IsTracking(HWND target) => _tracked.ContainsKey(target);

    /// <summary>
    /// Returns the number of currently tracked overlays.
    /// </summary>
    public int TrackedCount => _tracked.Count;

    private void PollingLoop()
    {
        var token = _cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                PollAllTracked();
                Thread.Sleep(PollingIntervalMs);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] OverlaySyncService: Polling error: {ex.Message}");
            }
        }
    }

    private void PollAllTracked()
    {
        foreach (var kvp in _tracked)
        {
            if (_cts.IsCancellationRequested) break;

            var tracked = kvp.Value;

            // Skip minimized windows
            if (tracked.IsMinimized)
            {
                // Periodically check if un-minimized
                if (!PInvoke.IsIconic(tracked.TargetHwnd))
                    tracked.IsMinimized = false;
                else
                    continue;
            }

            // Check if target window still exists
            if (!WindowInterop.IsWindow(tracked.TargetHwnd))
                continue;

            // Skip invisible windows
            if (!WindowInterop.IsWindowVisible(tracked.TargetHwnd))
                continue;

            if (!WindowInterop.GetWindowRect(tracked.TargetHwnd, out var currentRect))
                continue;

            // Compare with last known position
            if (RectsEqual(currentRect, tracked.LastWindowRect))
                continue;

            // Check for monitor boundary crossing
            var currentMonitor = DisplayInterop.GetMonitorForWindow(tracked.TargetHwnd);
            if (currentMonitor != tracked.LastMonitor)
            {
                Trace.WriteLine($"[Harbor] OverlaySyncService: Poll detected monitor change for HWND {tracked.TargetHwnd}");
                tracked.LastMonitor = currentMonitor;
                tracked.LastWindowRect = currentRect;
                MonitorChanged?.Invoke(tracked.TargetHwnd);
                continue;
            }

            // Position changed — reposition overlay (Layer 2 catch)
            var startTicks = _perfTimer.ElapsedTicks;

            var titleBarRect = tracked.Offset.Apply(currentRect);
            tracked.LastWindowRect = currentRect;

            RepositionOverlay(tracked.OverlayHwnd, titleBarRect);
            DwmInterop.Flush();

            RecordLatency(startTicks);
            Interlocked.Increment(ref _pollingUpdates);
        }
    }

    private static void RepositionOverlay(HWND overlayHwnd, RECT titleBarRect)
    {
        WindowInterop.SetWindowPos(
            overlayHwnd,
            HWND.Null,
            titleBarRect.left,
            titleBarRect.top,
            titleBarRect.right - titleBarRect.left,
            titleBarRect.bottom - titleBarRect.top,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
    }

    private void RecordLatency(long startTicks)
    {
        var elapsed = _perfTimer.ElapsedTicks - startTicks;
        var latencyMs = (double)elapsed / Stopwatch.Frequency * 1000.0;

        Interlocked.Increment(ref _totalUpdates);

        if (latencyMs > FrameBudgetMs)
            Interlocked.Increment(ref _frameMisses);

        lock (_latencyLock)
        {
            _lastLatencyMs = latencyMs;
            if (latencyMs > _maxLatencyMs)
                _maxLatencyMs = latencyMs;

            _recentLatencies.Add(latencyMs);

            // Keep only the last 1024 samples for P99 calculation
            if (_recentLatencies.Count > 1024)
                _recentLatencies.RemoveRange(0, _recentLatencies.Count - 1024);
        }
    }

    /// <summary>
    /// Compares two RECTs for equality.
    /// </summary>
    internal static bool RectsEqual(RECT a, RECT b)
    {
        return a.left == b.left
            && a.top == b.top
            && a.right == b.right
            && a.bottom == b.bottom;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();

        // Wait for polling thread to exit (with timeout)
        if (_pollingThread.IsAlive)
            _pollingThread.Join(500);

        _cts.Dispose();
        _tracked.Clear();

        var stats = GetStats();
        Trace.WriteLine(
            $"[Harbor] OverlaySyncService: Disposed. " +
            $"Total={stats.TotalUpdates}, Event={stats.EventDrivenUpdates}, Poll={stats.PollingUpdates}, " +
            $"FrameMisses={stats.FrameMisses} ({stats.FrameMissRate:P1}), " +
            $"P99={stats.P99UpdateLatencyMs:F2}ms, Max={stats.MaxUpdateLatencyMs:F2}ms");
    }
}
