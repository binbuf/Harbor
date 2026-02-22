using System.Diagnostics;
using System.Windows;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;

namespace Harbor.Shell;

public partial class App : Application
{
    private ShellServices? _shellServices;
    private WindowEventManager? _windowEventManager;
    private ForegroundWindowService? _foregroundService;
    private TitleBarDiscoveryService? _titleBarService;
    private WindowCommandService? _windowCommandService;
    private ColorOverrideService? _colorOverrideService;
    private TitleBarColorService? _titleBarColorService;
    private OverlaySyncService? _overlaySyncService;
    private OverlayManager? _overlayManager;
    private FullscreenDetectionService? _fullscreenDetectionService;
    private FullscreenRetreatCoordinator? _fullscreenCoordinator;
    private DockPinningService? _dockPinningService;
    private DisplayChangeService? _displayChangeService;
    private ThemeService? _themeService;
    private AppBarRegistration? _menuBarRegistration;
    private AppBarRegistration? _dockRegistration;
    private TopMenuBar? _menuBar;
    private Dock? _dock;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Trace.WriteLine("[Harbor] App: Starting up...");

        _shellServices = new ShellServices();

        Trace.WriteLine($"[Harbor] App: NotificationArea initialized = {!_shellServices.NotificationArea.IsFailed}");
        Trace.WriteLine($"[Harbor] App: TrayIcons count = {_shellServices.NotificationArea.TrayIcons.Count}");

        // Create centralized window event subscription system
        _windowEventManager = new WindowEventManager(Dispatcher);

        // Create foreground window tracking service
        _foregroundService = new ForegroundWindowService();

        // Create title bar discovery, command service, color detection, and overlay management
        _titleBarService = new TitleBarDiscoveryService(_windowEventManager);
        _windowCommandService = new WindowCommandService();
        _colorOverrideService = new ColorOverrideService();
        _titleBarColorService = new TitleBarColorService(_colorOverrideService);
        _overlaySyncService = new OverlaySyncService();
        _overlayManager = new OverlayManager(_windowEventManager, _titleBarService, _windowCommandService, _overlaySyncService, _titleBarColorService);

        // Create theme detection service and apply initial theme
        _themeService = new ThemeService();
        ApplyTheme(_themeService.CurrentTheme);
        _themeService.ThemeChanged += OnThemeChanged;

        // Create display change monitoring
        _displayChangeService = new DisplayChangeService();
        _displayChangeService.DisplayChanged += OnDisplayChanged;

        // Create and register the top menu bar as an AppBar
        _menuBar = AppBarHelper.CreateAppBar<TopMenuBar>(
            _shellServices,
            AppBarScreen.FromPrimaryScreen(),
            AppBarEdge.Top,
            24);

        _menuBarRegistration = AppBarHelper.Register(_menuBar, AppBarEdge.Top);
        _menuBar.Initialize(_foregroundService, _shellServices.NotificationArea);

        // Create dock pinning service for persistent app pinning
        _dockPinningService = new DockPinningService();

        // Create and register the Dock as a bottom AppBar
        _dock = AppBarHelper.CreateAppBar<Dock>(
            _shellServices,
            AppBarScreen.FromPrimaryScreen(),
            AppBarEdge.Bottom,
            62);

        _dockRegistration = AppBarHelper.Register(_dock, AppBarEdge.Bottom);
        _dock.Initialize(_shellServices.Tasks, _dockPinningService);

        // Create fullscreen detection and retreat coordination
        _fullscreenDetectionService = new FullscreenDetectionService();
        _fullscreenCoordinator = new FullscreenRetreatCoordinator(
            _fullscreenDetectionService, _windowEventManager, _overlayManager);

        // Register AppBars on the primary monitor for retreat management
        var primaryMonitor = DisplayInterop.GetMonitorForWindow(
            new Windows.Win32.Foundation.HWND(_menuBar.Handle));
        _fullscreenCoordinator.RegisterAppBar(_menuBar, primaryMonitor);
        _fullscreenCoordinator.RegisterAppBar(_dock, primaryMonitor);

        Trace.WriteLine("[Harbor] App: Startup complete.");
    }

    /// <summary>
    /// Swaps the active theme resource dictionary and notifies shell chrome.
    /// </summary>
    private void ApplyTheme(AppTheme theme)
    {
        var uri = theme == AppTheme.Light
            ? new Uri("LightTheme.xaml", UriKind.Relative)
            : new Uri("DarkTheme.xaml", UriKind.Relative);

        var newDict = new ResourceDictionary { Source = uri };

        var merged = Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(newDict);

        Trace.WriteLine($"[Harbor] App: Applied {theme} theme.");
    }

    /// <summary>
    /// Handles real-time theme changes from the system.
    /// Swaps resource dictionary, reapplies acrylic, and invalidates title bar color cache.
    /// </summary>
    private void OnThemeChanged(AppTheme theme)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyTheme(theme);

            // Reapply acrylic with new theme colors
            _menuBar?.ApplyThemedAcrylic(theme);
            _dock?.ApplyThemedAcrylic(theme);

            // Invalidate all cached title bar colors — apps may have changed appearance
            _titleBarColorService?.InvalidateAll();

            Trace.WriteLine($"[Harbor] App: Theme switch to {theme} complete.");
        });
    }

    /// <summary>
    /// Handles WM_DISPLAYCHANGE — rebuilds AppBar registrations for the new display configuration.
    /// </summary>
    private void OnDisplayChanged()
    {
        Trace.WriteLine("[Harbor] App: Display configuration changed, rebuilding AppBars.");

        // Update AppBar positions on the primary screen.
        // ManagedShell's AppBarWindow.UpdatePosition() handles re-querying the screen bounds.
        _menuBar?.UpdatePosition();
        _dock?.UpdatePosition();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Trace.WriteLine("[Harbor] App: Shutting down...");

        _fullscreenCoordinator?.Dispose();
        _fullscreenCoordinator = null;
        _fullscreenDetectionService = null;

        _overlayManager?.Dispose();
        _overlayManager = null;

        _overlaySyncService?.Dispose();
        _overlaySyncService = null;

        _titleBarColorService?.Dispose();
        _titleBarColorService = null;

        _colorOverrideService?.Dispose();
        _colorOverrideService = null;

        _titleBarService?.Dispose();
        _titleBarService = null;

        _dockRegistration?.Dispose();
        _dockRegistration = null;

        _menuBarRegistration?.Dispose();
        _menuBarRegistration = null;

        if (_themeService is not null)
        {
            _themeService.ThemeChanged -= OnThemeChanged;
            _themeService.Dispose();
            _themeService = null;
        }

        if (_displayChangeService is not null)
        {
            _displayChangeService.DisplayChanged -= OnDisplayChanged;
            _displayChangeService.Dispose();
            _displayChangeService = null;
        }

        _foregroundService?.Dispose();
        _foregroundService = null;

        _windowEventManager?.Dispose();
        _windowEventManager = null;

        _dockPinningService?.Dispose();
        _dockPinningService = null;

        _shellServices?.Dispose();
        _shellServices = null;

        Trace.WriteLine("[Harbor] App: Shutdown complete.");

        base.OnExit(e);
    }
}
