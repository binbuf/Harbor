using System.Diagnostics;
using System.Windows;
using Harbor.Core.Services;

namespace Harbor.Shell;

public partial class App : Application
{
    private ShellServices? _shellServices;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Trace.WriteLine("[Harbor] App: Starting up...");

        _shellServices = new ShellServices();

        Trace.WriteLine($"[Harbor] App: NotificationArea initialized = {!_shellServices.NotificationArea.IsFailed}");
        Trace.WriteLine($"[Harbor] App: TrayIcons count = {_shellServices.NotificationArea.TrayIcons.Count}");

        Trace.WriteLine("[Harbor] App: Startup complete.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Trace.WriteLine("[Harbor] App: Shutting down...");

        _shellServices?.Dispose();
        _shellServices = null;

        Trace.WriteLine("[Harbor] App: Shutdown complete.");

        base.OnExit(e);
    }
}
