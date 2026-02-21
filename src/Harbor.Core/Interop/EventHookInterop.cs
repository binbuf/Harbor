using Windows.Win32;
using Windows.Win32.UI.Accessibility;

namespace Harbor.Core.Interop;

/// <summary>
/// Wrapper over Win32 event hook APIs for tracking window events.
/// </summary>
public static class EventHookInterop
{
    // Event constants
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;

    // Flags
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    public static UnhookWinEventSafeHandle SetWinEventHook(
        uint eventMin,
        uint eventMax,
        WINEVENTPROC callback,
        uint processId = 0,
        uint threadId = 0,
        uint flags = WINEVENT_OUTOFCONTEXT)
    {
        return PInvoke.SetWinEventHook(eventMin, eventMax, null, callback, processId, threadId, flags);
    }
}
