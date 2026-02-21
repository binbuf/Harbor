using System.Diagnostics;
using System.Windows;
using Harbor.Core.Services;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;

namespace Harbor.Shell;

public partial class App : Application
{
    private ShellServices? _shellServices;
    private ForegroundWindowService? _foregroundService;
    private AppBarRegistration? _menuBarRegistration;
    private AppBarRegistration? _dockRegistration;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Trace.WriteLine("[Harbor] App: Starting up...");

        _shellServices = new ShellServices();

        Trace.WriteLine($"[Harbor] App: NotificationArea initialized = {!_shellServices.NotificationArea.IsFailed}");
        Trace.WriteLine($"[Harbor] App: TrayIcons count = {_shellServices.NotificationArea.TrayIcons.Count}");

        // Create foreground window tracking service
        _foregroundService = new ForegroundWindowService();

        // Create and register the top menu bar as an AppBar
        var menuBar = AppBarHelper.CreateAppBar<TopMenuBar>(
            _shellServices,
            AppBarScreen.FromPrimaryScreen(),
            AppBarEdge.Top,
            24);

        _menuBarRegistration = AppBarHelper.Register(menuBar, AppBarEdge.Top);
        menuBar.Initialize(_foregroundService, _shellServices.NotificationArea);

        // Create and register the Dock as a bottom AppBar
        var dock = AppBarHelper.CreateAppBar<Dock>(
            _shellServices,
            AppBarScreen.FromPrimaryScreen(),
            AppBarEdge.Bottom,
            62);

        _dockRegistration = AppBarHelper.Register(dock, AppBarEdge.Bottom);
        dock.Initialize(_shellServices.Tasks);

        Trace.WriteLine("[Harbor] App: Startup complete.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Trace.WriteLine("[Harbor] App: Shutting down...");

        _dockRegistration?.Dispose();
        _dockRegistration = null;

        _menuBarRegistration?.Dispose();
        _menuBarRegistration = null;

        _foregroundService?.Dispose();
        _foregroundService = null;

        _shellServices?.Dispose();
        _shellServices = null;

        Trace.WriteLine("[Harbor] App: Shutdown complete.");

        base.OnExit(e);
    }
}
