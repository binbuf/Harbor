using System.Collections.Concurrent;
using System.Diagnostics;
using Harbor.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Harbor.Core.Services;

/// <summary>
/// The type of window event received from SetWinEventHook.
/// </summary>
public enum WindowEventType
{
    Foreground,
    LocationChange,
    ObjectCreate,
    ObjectDestroy,
}

/// <summary>
/// Data associated with a window event.
/// </summary>
public readonly record struct WindowEventArgs(
    WindowEventType EventType,
    HWND WindowHandle,
    int ObjectId,
    int ChildId,
    uint EventThreadId,
    uint EventTime);

/// <summary>
/// Callback signature for window event subscribers.
/// </summary>
public delegate void WindowEventHandler(WindowEventArgs args);

/// <summary>
/// Options for subscribing to window events.
/// </summary>
public sealed class WindowEventSubscription
{
    /// <summary>
    /// The event types to subscribe to. If empty, all events are received.
    /// </summary>
    public required IReadOnlySet<WindowEventType> EventTypes { get; init; }

    /// <summary>
    /// Optional HWND filter. If set, only events for this window handle are delivered.
    /// </summary>
    public HWND? FilterHwnd { get; init; }

    /// <summary>
    /// If true, the handler is invoked on the WPF dispatcher (UI) thread.
    /// If false, the handler is invoked on the hook's thread.
    /// </summary>
    public bool MarshalToUiThread { get; init; }

    /// <summary>
    /// The callback to invoke when a matching event is received.
    /// </summary>
    public required WindowEventHandler Handler { get; init; }
}

/// <summary>
/// Centralized window event subscription system using SetWinEventHook.
/// Distributes window lifecycle and geometry events to registered subscribers.
/// </summary>
/// <remarks>
/// Uses out-of-context hooks (WINEVENT_OUTOFCONTEXT) to avoid DLL injection.
/// Filters out events from Harbor's own windows and invisible/tool windows.
/// Thread-safe: subscriptions can be added/removed from any thread.
/// </remarks>
public sealed class WindowEventManager : IDisposable
{
    private readonly uint _ownProcessId;
    private readonly ConcurrentDictionary<Guid, WindowEventSubscription> _subscribers = new();
    private readonly System.Windows.Threading.Dispatcher? _dispatcher;

    private UnhookWinEventSafeHandle? _hookForeground;
    private UnhookWinEventSafeHandle? _hookLocation;
    private UnhookWinEventSafeHandle? _hookCreate;
    private UnhookWinEventSafeHandle? _hookDestroy;

    // prevent GC of the delegates
    private WINEVENTPROC? _callbackForeground;
    private WINEVENTPROC? _callbackLocation;
    private WINEVENTPROC? _callbackCreate;
    private WINEVENTPROC? _callbackDestroy;

    private bool _disposed;

    private static readonly IReadOnlyDictionary<uint, WindowEventType> EventMap =
        new Dictionary<uint, WindowEventType>
        {
            [EventHookInterop.EVENT_SYSTEM_FOREGROUND] = WindowEventType.Foreground,
            [EventHookInterop.EVENT_OBJECT_LOCATIONCHANGE] = WindowEventType.LocationChange,
            [EventHookInterop.EVENT_OBJECT_CREATE] = WindowEventType.ObjectCreate,
            [EventHookInterop.EVENT_OBJECT_DESTROY] = WindowEventType.ObjectDestroy,
        };

    /// <param name="dispatcher">
    /// The WPF dispatcher for UI-thread marshaling. Pass null if UI marshaling is not needed.
    /// </param>
    public WindowEventManager(System.Windows.Threading.Dispatcher? dispatcher = null)
    {
        _ownProcessId = WindowInterop.GetCurrentProcessId();
        _dispatcher = dispatcher;

        InstallHooks();
        Trace.WriteLine("[Harbor] WindowEventManager: Event hooks installed.");
    }

    /// <summary>
    /// Registers a subscriber for window events. Returns a token that can be used to unsubscribe.
    /// </summary>
    public Guid Subscribe(WindowEventSubscription subscription)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Guid.NewGuid();
        _subscribers[id] = subscription;
        return id;
    }

    /// <summary>
    /// Removes a previously registered subscription.
    /// </summary>
    public bool Unsubscribe(Guid subscriptionId)
    {
        return _subscribers.TryRemove(subscriptionId, out _);
    }

    private void InstallHooks()
    {
        _callbackForeground = OnWinEvent;
        _hookForeground = EventHookInterop.SetWinEventHook(
            EventHookInterop.EVENT_SYSTEM_FOREGROUND,
            EventHookInterop.EVENT_SYSTEM_FOREGROUND,
            _callbackForeground);

        _callbackLocation = OnWinEvent;
        _hookLocation = EventHookInterop.SetWinEventHook(
            EventHookInterop.EVENT_OBJECT_LOCATIONCHANGE,
            EventHookInterop.EVENT_OBJECT_LOCATIONCHANGE,
            _callbackLocation);

        _callbackCreate = OnWinEvent;
        _hookCreate = EventHookInterop.SetWinEventHook(
            EventHookInterop.EVENT_OBJECT_CREATE,
            EventHookInterop.EVENT_OBJECT_CREATE,
            _callbackCreate);

        _callbackDestroy = OnWinEvent;
        _hookDestroy = EventHookInterop.SetWinEventHook(
            EventHookInterop.EVENT_OBJECT_DESTROY,
            EventHookInterop.EVENT_OBJECT_DESTROY,
            _callbackDestroy);
    }

    private void OnWinEvent(
        HWINEVENTHOOK hWinEventHook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        if (_disposed) return;
        if (hwnd == HWND.Null) return;

        // Map the raw event constant to our enum
        if (!EventMap.TryGetValue(@event, out var eventType))
            return;

        // Filter out events from Harbor's own windows
        if (IsOwnWindow(hwnd))
            return;

        // For location/foreground events, filter out invisible and non-top-level windows.
        // Create/Destroy events are allowed for non-visible windows (they may be becoming visible or just disappeared).
        if (eventType is WindowEventType.Foreground or WindowEventType.LocationChange)
        {
            if (!WindowInterop.IsWindowVisible(hwnd))
                return;
        }

        var args = new WindowEventArgs(eventType, hwnd, idObject, idChild, idEventThread, dwmsEventTime);
        DispatchToSubscribers(args);
    }

    private bool IsOwnWindow(HWND hwnd)
    {
        WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == _ownProcessId;
    }

    private void DispatchToSubscribers(WindowEventArgs args)
    {
        foreach (var kvp in _subscribers)
        {
            var sub = kvp.Value;

            // Filter by event type
            if (sub.EventTypes.Count > 0 && !sub.EventTypes.Contains(args.EventType))
                continue;

            // Filter by HWND
            if (sub.FilterHwnd.HasValue && sub.FilterHwnd.Value != args.WindowHandle)
                continue;

            try
            {
                if (sub.MarshalToUiThread && _dispatcher is not null)
                {
                    _dispatcher.BeginInvoke(() => sub.Handler(args));
                }
                else
                {
                    sub.Handler(args);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] WindowEventManager: Subscriber {kvp.Key} threw: {ex.Message}");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hookForeground?.Dispose();
        _hookLocation?.Dispose();
        _hookCreate?.Dispose();
        _hookDestroy?.Dispose();

        _hookForeground = null;
        _hookLocation = null;
        _hookCreate = null;
        _hookDestroy = null;

        _callbackForeground = null;
        _callbackLocation = null;
        _callbackCreate = null;
        _callbackDestroy = null;

        _subscribers.Clear();

        Trace.WriteLine("[Harbor] WindowEventManager: Disposed.");
    }
}
