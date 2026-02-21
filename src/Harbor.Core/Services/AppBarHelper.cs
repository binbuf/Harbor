using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;

namespace Harbor.Core.Services;

/// <summary>
/// Helper for registering and managing WPF windows as AppBars using ManagedShell.
/// Supports top-docked and bottom-docked AppBar windows and handles
/// WM_DISPLAYCHANGE for monitor connect/disconnect.
/// </summary>
public static class AppBarHelper
{
    private const int WM_DISPLAYCHANGE = 0x007E;

    /// <summary>
    /// Registers a WPF window as an AppBar at the specified screen edge.
    /// Hooks WM_DISPLAYCHANGE to rebuild position on monitor changes.
    /// Returns an IDisposable that unregisters the AppBar when disposed.
    /// </summary>
    public static AppBarRegistration Register(
        AppBarWindow appBarWindow,
        AppBarEdge edge)
    {
        appBarWindow.AppBarEdge = edge;

        Trace.WriteLine($"[Harbor] AppBarHelper: Registering AppBar at {edge} edge.");

        appBarWindow.Show();

        var registration = new AppBarRegistration(appBarWindow);

        Trace.WriteLine("[Harbor] AppBarHelper: AppBar registered successfully.");
        return registration;
    }

    /// <summary>
    /// Creates an AppBarWindow-derived instance with proper ShellServices dependencies.
    /// </summary>
    public static T CreateAppBar<T>(
        ShellServices shellServices,
        AppBarScreen screen,
        AppBarEdge edge,
        double desiredHeight,
        AppBarMode mode = AppBarMode.Normal) where T : AppBarWindow
    {
        var window = (T)Activator.CreateInstance(
            typeof(T),
            shellServices.AppBarManager,
            shellServices.ExplorerHelper,
            shellServices.FullScreenHelper,
            screen,
            edge,
            mode,
            desiredHeight)!;

        return window;
    }
}

/// <summary>
/// Represents a registered AppBar. Dispose to unregister and close the AppBar window.
/// Listens for WM_DISPLAYCHANGE to update position on monitor changes.
/// </summary>
public sealed class AppBarRegistration : IDisposable
{
    private const int WM_DISPLAYCHANGE = 0x007E;

    private readonly AppBarWindow _window;
    private HwndSource? _hwndSource;
    private bool _disposed;

    internal AppBarRegistration(AppBarWindow window)
    {
        _window = window;
        HookDisplayChange();
    }

    private void HookDisplayChange()
    {
        var handle = _window.Handle;
        if (handle == IntPtr.Zero)
        {
            _window.SourceInitialized += OnSourceInitialized;
            return;
        }

        AttachHook(handle);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _window.SourceInitialized -= OnSourceInitialized;
        AttachHook(_window.Handle);
    }

    private void AttachHook(IntPtr handle)
    {
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            Trace.WriteLine("[Harbor] AppBarRegistration: WM_DISPLAYCHANGE received, updating position.");
            _window.UpdatePosition();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Trace.WriteLine("[Harbor] AppBarRegistration: Unregistering AppBar.");

        _hwndSource?.RemoveHook(WndProc);

        _window.AllowClose = true;
        _window.Close();

        Trace.WriteLine("[Harbor] AppBarRegistration: AppBar unregistered.");
    }
}
