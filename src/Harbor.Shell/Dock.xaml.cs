using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using Harbor.Shell.Converters;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTasks;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

public partial class Dock : AppBarWindow
{
    private Tasks? _tasks;
    private readonly IconExtractionService _iconService = new();

    public Dock(
        AppBarManager appBarManager,
        ExplorerHelper explorerHelper,
        FullScreenHelper fullScreenHelper,
        AppBarScreen screen,
        AppBarEdge edge,
        AppBarMode mode,
        double desiredHeight)
        : base(appBarManager, explorerHelper, fullScreenHelper, screen, edge, mode, desiredHeight)
    {
        InitializeComponent();
        WireUpIconConverter();
    }

    /// <summary>
    /// Initializes the Dock with task data after the window has been shown.
    /// Called after Show() so we have an HWND for acrylic.
    /// </summary>
    public void Initialize(Tasks tasks)
    {
        _tasks = tasks;
        TaskIconsControl.ItemsSource = _tasks.GroupedWindows;

        Trace.WriteLine("[Harbor] Dock: Initialized with task binding and icon extraction.");
    }

    private void WireUpIconConverter()
    {
        if (Resources["AppIconConverter"] is AppIconConverter converter)
        {
            converter.IconService = _iconService;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyAcrylic();
    }

    private void ApplyAcrylic()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // AABBGGRR format: 50% opacity (#80), color #1E1E1E
        const uint acrylicColor = 0x801E1E1E;
        var result = CompositionInterop.EnableAcrylic(new HWND(hwnd), acrylicColor);

        if (result)
            Trace.WriteLine("[Harbor] Dock: Acrylic background applied.");
        else
            Trace.WriteLine("[Harbor] Dock: Acrylic failed, using solid fallback.");
    }

    /// <summary>
    /// Handles click on a dock icon. Activates the window, or minimizes if already focused.
    /// </summary>
    private void DockIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ApplicationWindow appWindow })
            return;

        HandleDockIconClick(appWindow);
    }

    /// <summary>
    /// Click-to-activate / click-to-minimize logic extracted for testability.
    /// </summary>
    public static void HandleDockIconClick(ApplicationWindow appWindow)
    {
        if (appWindow.State == ApplicationWindow.WindowState.Active)
        {
            Trace.WriteLine($"[Harbor] Dock: Minimizing active window: {appWindow.Title}");
            appWindow.Minimize();
        }
        else
        {
            Trace.WriteLine($"[Harbor] Dock: Activating window: {appWindow.Title}");
            appWindow.BringToFront();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        TaskIconsControl.ItemsSource = null;
        _tasks = null;
        _iconService.ClearCache();

        base.OnClosing(e);
    }
}
