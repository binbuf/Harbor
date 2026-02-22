using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Harbor.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Harbor.Core.Services;

/// <summary>
/// Monitors the foreground window via SetWinEventHook(EVENT_SYSTEM_FOREGROUND)
/// and exposes the active application name. Raises PropertyChanged when the
/// foreground window changes.
/// </summary>
public sealed class ForegroundWindowService : INotifyPropertyChanged, IDisposable
{
    private UnhookWinEventSafeHandle? _hook;
    private WINEVENTPROC? _callback; // prevent GC of the delegate
    private string _activeAppName = string.Empty;
    private nint _activeWindowHandle;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ActiveAppName
    {
        get => _activeAppName;
        private set
        {
            if (_activeAppName == value) return;
            _activeAppName = value;
            OnPropertyChanged();
        }
    }

    public nint ActiveWindowHandle
    {
        get => _activeWindowHandle;
        private set
        {
            if (_activeWindowHandle == value) return;
            _activeWindowHandle = value;
            OnPropertyChanged();
        }
    }

    public ForegroundWindowService()
    {
        // Read initial foreground window
        UpdateActiveAppName();
        StartHook();
    }

    private void StartHook()
    {
        _callback = OnForegroundChanged;
        _hook = EventHookInterop.SetWinEventHook(
            EventHookInterop.EVENT_SYSTEM_FOREGROUND,
            EventHookInterop.EVENT_SYSTEM_FOREGROUND,
            _callback);

        Trace.WriteLine("[Harbor] ForegroundWindowService: Event hook installed.");
    }

    private void OnForegroundChanged(
        HWINEVENTHOOK hWinEventHook,
        uint @event,
        HWND hwnd,
        int idObject,
        int idChild,
        uint idEventThread,
        uint dwmsEventTime)
    {
        UpdateActiveAppName();
    }

    private void UpdateActiveAppName()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        // Skip updates when the foreground window belongs to Harbor itself
        WindowInterop.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == WindowInterop.GetCurrentProcessId()) return;

        unsafe { ActiveWindowHandle = (nint)hwnd.Value; }
        ActiveAppName = GetAppNameFromWindow(hwnd);
    }

    /// <summary>
    /// Extracts the application name from a window handle.
    /// Uses the process name (e.g. "Notepad", "Code") rather than the window title.
    /// </summary>
    public static string GetAppNameFromWindow(HWND hwnd)
    {
        if (hwnd == HWND.Null) return string.Empty;

        try
        {
            WindowInterop.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return string.Empty;

            using var process = Process.GetProcessById((int)processId);
            var description = process.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description;

            // Fallback to process name
            return CleanProcessName(process.ProcessName);
        }
        catch
        {
            // Process may have exited or access denied
            return string.Empty;
        }
    }

    /// <summary>
    /// Cleans a raw process name for display: strips ".exe" suffix and capitalizes the first letter.
    /// </summary>
    internal static string CleanProcessName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        if (name.Length > 0 && char.IsLower(name[0]))
            name = string.Concat(char.ToUpper(name[0]).ToString(), name.AsSpan(1));

        return name;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _hook?.Dispose();
        _hook = null;
        _callback = null;

        Trace.WriteLine("[Harbor] ForegroundWindowService: Disposed.");
    }
}
