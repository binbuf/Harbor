using System.Diagnostics;
using ManagedShell;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTasks;
using ManagedShell.WindowsTray;

namespace Harbor.Core.Services;

/// <summary>
/// Manages ManagedShell's core services lifecycle. Initializes the ShellManager
/// and exposes TasksService, NotificationArea, and AppBarManager for use by
/// Harbor's Top Menu, Dock, and overlay components.
/// </summary>
public sealed class ShellServices : IDisposable
{
    private readonly ShellManager _shellManager;
    private bool _disposed;

    public ShellServices() : this(ShellManager.DefaultShellConfig)
    {
    }

    public ShellServices(ShellConfig config)
    {
        Trace.WriteLine("[Harbor] ShellServices: Initializing ManagedShell...");

        _shellManager = new ShellManager(config);

        Trace.WriteLine($"[Harbor] ShellServices: TasksService available = {config.EnableTasksService}");
        Trace.WriteLine($"[Harbor] ShellServices: TrayService available = {config.EnableTrayService}");
        Trace.WriteLine("[Harbor] ShellServices: ManagedShell initialized successfully.");
    }

    /// <summary>
    /// The underlying ShellManager instance.
    /// </summary>
    public ShellManager ShellManager => _shellManager;

    /// <summary>
    /// Provides task/window enumeration for the Dock.
    /// </summary>
    public TasksService TasksService => _shellManager.TasksService;

    /// <summary>
    /// Provides grouped window collection for Dock UI binding.
    /// </summary>
    public Tasks Tasks => _shellManager.Tasks;

    /// <summary>
    /// Provides system tray icon hosting for the Top Menu.
    /// </summary>
    public NotificationArea NotificationArea => _shellManager.NotificationArea;

    /// <summary>
    /// Manages AppBar registrations for Top Menu and Dock windows.
    /// </summary>
    public AppBarManager AppBarManager => _shellManager.AppBarManager;

    /// <summary>
    /// Provides fullscreen application detection.
    /// </summary>
    public FullScreenHelper FullScreenHelper => _shellManager.FullScreenHelper;

    /// <summary>
    /// Provides Explorer taskbar interaction.
    /// </summary>
    public ExplorerHelper ExplorerHelper => _shellManager.ExplorerHelper;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Trace.WriteLine("[Harbor] ShellServices: Shutting down ManagedShell...");

        _shellManager.AppBarManager.SignalGracefulShutdown();
        _shellManager.Dispose();

        Trace.WriteLine("[Harbor] ShellServices: ManagedShell disposed.");
    }
}
