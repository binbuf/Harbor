using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Harbor.Core.Interop;
using ManagedShell.WindowsTasks;
using Windows.Win32.Foundation;

namespace Harbor.Core.Services;

/// <summary>
/// Manages App Navigator activation: captures F3/Ctrl+Up keyboard shortcuts,
/// enumerates open windows grouped by virtual desktop, and raises events for
/// the overlay UI to consume. Follows the AppSwitcherService pattern.
/// </summary>
public sealed class AppNavigatorService : IDisposable
{
    private const int VK_ESCAPE = 0x1B;
    private const int VK_UP = 0x26;

    private readonly LowLevelKeyboardHookService _keyboard;
    private readonly Tasks _tasks;
    private readonly int _hotkey;
    private bool _isActive;
    private bool _enabled;
    private bool _disposed;
    private HWND _previousForegroundHwnd;

    // Lazy-init VirtualDesktop COM manager; null if unavailable
    private IVirtualDesktopManager? _vdManager;
    private bool _vdManagerInitialized;

    public event Action<List<WindowGroup>>? ShowRequested;
    public event Action<HWND?>? HideRequested;

    public HWND PreviousForegroundHwnd => _previousForegroundHwnd;

    public AppNavigatorService(LowLevelKeyboardHookService keyboard, Tasks tasks, int hotkey = 0x72, bool enabled = true)
    {
        _keyboard = keyboard;
        _tasks = tasks;
        _hotkey = hotkey;

        if (enabled)
            RegisterHooks();
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled) return;

        if (enabled)
            RegisterHooks();
        else
            UnregisterHooks();
    }

    private void RegisterHooks()
    {
        _keyboard.Register(_hotkey, ModifierKeys.None, OnHotkey);
        _keyboard.Register(VK_UP, ModifierKeys.Control, OnCtrlUp);
        _keyboard.RawKeyEvent += OnRawKeyEvent;
        _enabled = true;
        Trace.WriteLine("[Harbor] AppNavigatorService: Registered F3/Ctrl+Up handlers.");
    }

    private void UnregisterHooks()
    {
        if (_isActive)
        {
            _isActive = false;
            HideRequested?.Invoke(null);
        }

        _keyboard.Unregister(_hotkey, ModifierKeys.None);
        _keyboard.Unregister(VK_UP, ModifierKeys.Control);
        _keyboard.RawKeyEvent -= OnRawKeyEvent;
        _enabled = false;
        Trace.WriteLine("[Harbor] AppNavigatorService: Unregistered handlers.");
    }

    private bool OnHotkey(bool isKeyDown)
    {
        if (!isKeyDown) return true;

        if (_isActive)
            Dismiss(null);
        else
            Activate();

        return true; // suppress key
    }

    private bool OnCtrlUp(bool isKeyDown)
    {
        if (!isKeyDown) return true;

        if (!_isActive)
            Activate();

        return true; // suppress key
    }

    private void OnRawKeyEvent(int vkCode, bool isDown)
    {
        if (!isDown && vkCode == VK_ESCAPE && _isActive)
            Dismiss(null);
    }

    /// <summary>Programmatically activate App Navigator (e.g. from gesture).</summary>
    public void Activate()
    {
        if (_isActive || _disposed) return;

        _previousForegroundHwnd = WindowInterop.GetForegroundWindow();
        var groups = BuildWindowGroups();
        _isActive = true;
        Trace.WriteLine($"[Harbor] AppNavigatorService: Showing ({groups.Count} windows).");
        ShowRequested?.Invoke(groups);
    }

    /// <summary>Dismiss App Navigator. If activatedHwnd is non-null, that window is focused on close.</summary>
    public void Dismiss(HWND? activatedHwnd)
    {
        if (!_isActive) return;
        _isActive = false;
        Trace.WriteLine($"[Harbor] AppNavigatorService: Hiding (activating={activatedHwnd}).");
        HideRequested?.Invoke(activatedHwnd);
    }

    private List<WindowGroup> BuildWindowGroups()
    {
        var vdMgr = GetVirtualDesktopManager();
        var groups = new List<WindowGroup>();

        foreach (ApplicationWindow window in _tasks.GroupedWindows)
        {
            Guid desktopId = Guid.Empty;
            try
            {
                if (vdMgr is not null)
                    desktopId = vdMgr.GetWindowDesktopId(window.Handle);
            }
            catch
            {
                // VD call failed — fall back to single desktop
            }

            groups.Add(new WindowGroup
            {
                Window = window,
                DisplayName = ForegroundWindowService.GetFriendlyNameFromPath(window.WinFileName ?? string.Empty),
                DesktopId = desktopId,
            });
        }

        // Sort by Z-order (most-recently-used first)
        groups.Sort((a, b) =>
        {
            var zA = WindowInterop.GetZOrder(new HWND(a.Window.Handle));
            var zB = WindowInterop.GetZOrder(new HWND(b.Window.Handle));
            return zA.CompareTo(zB);
        });

        return groups;
    }

    private IVirtualDesktopManager? GetVirtualDesktopManager()
    {
        if (_vdManagerInitialized) return _vdManager;
        _vdManagerInitialized = true;
        try
        {
            _vdManager = (IVirtualDesktopManager)new VirtualDesktopManagerCoClass();
            Trace.WriteLine("[Harbor] AppNavigatorService: IVirtualDesktopManager acquired.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] AppNavigatorService: IVirtualDesktopManager unavailable: {ex.Message}");
        }
        return _vdManager;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_enabled)
            UnregisterHooks();

        if (_vdManager is not null)
        {
            Marshal.ReleaseComObject(_vdManager);
            _vdManager = null;
        }
    }

    // -------------------------------------------------------------------------
    // Public data types

    public sealed class WindowGroup
    {
        public required ApplicationWindow Window { get; init; }
        public required string DisplayName { get; init; }
        public Guid DesktopId { get; init; }
    }

    // -------------------------------------------------------------------------
    // Virtual Desktop COM interop (stable public API — GUIDs do not change)

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class VirtualDesktopManagerCoClass { }

    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsWindowOnCurrentVirtualDesktop(nint hWnd);

        [return: MarshalAs(UnmanagedType.Struct)]
        Guid GetWindowDesktopId(nint hWnd);

        void MoveWindowToDesktop(nint hWnd, [MarshalAs(UnmanagedType.Struct)] ref Guid desktopId);
    }
}
