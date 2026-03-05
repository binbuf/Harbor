using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

public partial class App : Application
{
    private ShellServices? _shellServices;
    private WindowEventManager? _windowEventManager;
    private ForegroundWindowService? _foregroundService;
    private GlobalMenuBarService? _globalMenuService;
    private TitleBarDiscoveryService? _titleBarService;
    private WindowCommandService? _windowCommandService;
    private ColorOverrideService? _colorOverrideService;
    private TitleBarColorService? _titleBarColorService;
    private OverlaySyncService? _overlaySyncService;
    private OverlayManager? _overlayManager;
    private FullscreenDetectionService? _fullscreenDetectionService;
    private FullscreenRetreatCoordinator? _fullscreenCoordinator;
    private DockPinningService? _dockPinningService;
    private DockSettingsService? _dockSettingsService;
    private ShellSettingsService? _shellSettingsService;
    private DisplayChangeService? _displayChangeService;
    private ThemeService? _themeService;
    private HiddenWindowRegistry? _hiddenWindowRegistry;
    private HeartbeatService? _heartbeatService;
    private Process? _watchdogProcess;
    private WorkAreaService? _workAreaService;
    private LowLevelKeyboardHookService? _lowLevelKeyboardHook;
    private WindowCycleService? _windowCycleService;
    private AppSwitcherService? _appSwitcherService;
    private AppSwitcherOverlay? _appSwitcherOverlay;
    private AppNavigatorService? _appNavigatorService;
    private AppNavigatorOverlay? _appNavigatorOverlay;
    private HarborTrayIcon? _harborTrayIcon;
    private RecycleBinService? _recycleBinService;
    private WallpaperService? _wallpaperService;
    private WallpaperBrightnessService? _wallpaperBrightnessService;
    private DesktopBackgroundWindow? _desktopBackground;
    private DockOverlapMonitorService? _overlapMonitor;
    private ExplorerSuppressionService? _explorerSuppression;
    private AppBarRegistration? _menuBarRegistration;
    private TopMenuBar? _menuBar;
    private Dock? _dock;
    private VolumeService? _volumeService;
    private BluetoothService? _bluetoothService;
    private NetworkService? _networkService;
    private BatteryService? _batteryService;
    private SafeRemoveService? _safeRemoveService;
    private TrayIconFilterService? _trayIconFilter;
    private InstalledAppService? _installedAppService;
    private AppsLauncherWindow? _appsLauncher;
    private DesktopIconService? _desktopIconService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Trace.WriteLine("[Harbor] App: Starting up...");

        // Safe startup: register global exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Safe startup: clear stale hidden window entries from a previous session
        CrashRecoveryService.ClearStaleRegistry();

        // Initialize hidden window registry and heartbeat
        _hiddenWindowRegistry = new HiddenWindowRegistry();
        _heartbeatService = new HeartbeatService();

        // Launch watchdog process
        LaunchWatchdog();

        // Load shell settings and suppress explorer's UI if configured
        _shellSettingsService = new ShellSettingsService();
        // Create wallpaper service (needed for dynamic menu bar color and desktop background)
        _wallpaperService = new WallpaperService();

        // Always hide the Windows taskbar so its AppBar reservation is released.
        // This lets ManagedShell position Harbor's dock at the true screen bottom.
        // The taskbar is restored on exit (Dispose/ProcessExit).
        _explorerSuppression = new ExplorerSuppressionService();
        _explorerSuppression.Suppress();

        if (_shellSettingsService.ReplaceExplorer && !_shellSettingsService.ShowDesktopIcons)
        {
            // Render the desktop wallpaper to cover explorer's desktop
            _desktopBackground = new DesktopBackgroundWindow();
            _desktopBackground.Show();
            _desktopBackground.Initialize(_wallpaperService);
        }

        _shellServices = new ShellServices();

        Trace.WriteLine($"[Harbor] App: NotificationArea initialized = {!_shellServices.NotificationArea.IsFailed}");
        Trace.WriteLine($"[Harbor] App: TrayIcons count = {_shellServices.NotificationArea.TrayIcons.Count}");

        // Create centralized window event subscription system
        _windowEventManager = new WindowEventManager(Dispatcher);

        // Create foreground window tracking service
        _foregroundService = new ForegroundWindowService();
        _globalMenuService = new GlobalMenuBarService();

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
            30);

        _menuBarRegistration = AppBarHelper.Register(_menuBar, AppBarEdge.Top);
        // Create volume service, Bluetooth service, network service, and tray icon filter before menu bar initialization
        _volumeService = new VolumeService();
        _bluetoothService = new BluetoothService();
        _networkService = new NetworkService();
        _batteryService = new BatteryService();
        _safeRemoveService = new SafeRemoveService();
        _trayIconFilter = new TrayIconFilterService(_shellServices.NotificationArea);

        _menuBar.Initialize(_foregroundService, _shellServices.NotificationArea, _globalMenuService, _trayIconFilter);
        _menuBar.ConnectSettings(_shellSettingsService);

        // Connect wallpaper brightness service for dynamic menu bar color
        _wallpaperBrightnessService = new WallpaperBrightnessService(_wallpaperService);
        _menuBar.ConnectBrightnessService(_wallpaperBrightnessService);

        // Connect volume, Bluetooth, network, and battery services to menu bar
        _menuBar.ConnectVolumeService(_volumeService);
        _menuBar.ConnectBluetoothService(_bluetoothService);
        _menuBar.ConnectNetworkService(_networkService);
        _menuBar.ConnectBatteryService(_batteryService);
        _menuBar.ConnectSafeRemoveService(_safeRemoveService);

        // Create dock pinning and settings services
        _dockPinningService = new DockPinningService();
        _dockSettingsService = new DockSettingsService();

        // Create and show the Dock as a plain HWND_TOPMOST overlay (not an AppBar)
        _dock = new Dock();
        _dock.Show();
        _dock.Initialize(_shellServices.Tasks, _dockPinningService, _dockSettingsService);

        // Reserve screen space for the menu bar and dock via work area adjustment.
        // This overrides any existing reservation (e.g. the Windows taskbar) so that
        // Harbor's bars own the screen edges. The original work area is restored on exit.
        _workAreaService = new WorkAreaService();
        _workAreaService.Apply(topInset: ScaledTopInset(), bottomInset: ScaledBottomInset());

        // Apply initial auto-hide mode
        ApplyAutoHideMode(_dockSettingsService.AutoHideMode);

        // Subscribe to settings changes for runtime mode switching
        _dockSettingsService.SettingsChanged += OnDockSettingsChanged;
        _shellSettingsService.SettingsChanged += OnShellSettingsChanged;

        // Register ProcessExit to restore explorer even on forced termination (e.g. VS debugger stop)
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        // Create fullscreen detection and retreat coordination
        _fullscreenDetectionService = new FullscreenDetectionService();
        _fullscreenCoordinator = new FullscreenRetreatCoordinator(
            _fullscreenDetectionService, _windowEventManager, _overlayManager);

        // Register UI elements on the primary monitor for retreat management
        var primaryMonitor = DisplayInterop.GetMonitorForWindow(
            new Windows.Win32.Foundation.HWND(_menuBar.Handle));
        _fullscreenCoordinator.RegisterAppBar(_menuBar, primaryMonitor);
        _fullscreenCoordinator.RegisterRetreatable(_dock, primaryMonitor);

        // Auto-pin File Manager (explorer.exe) at front of dock
        _dockPinningService.PinAt(0, @"C:\Windows\explorer.exe", "Finder");

        // Auto-pin Apps launcher at position 1 (after Finder)
        _dockPinningService.PinAt(1, IconExtractionService.AppsLauncherSentinel, "Apps");

        // Create keyboard hook and hotkey services
        _lowLevelKeyboardHook = new LowLevelKeyboardHookService();
        _windowCycleService = new WindowCycleService(_lowLevelKeyboardHook, _shellServices.Tasks);

        // Create app switcher with overlay
        var iconService = new IconExtractionService();
        _appSwitcherService = new AppSwitcherService(_lowLevelKeyboardHook, _shellServices.Tasks, iconService,
            enabled: _shellSettingsService.UseCustomAppSwitcher);
        _appSwitcherOverlay = new AppSwitcherOverlay();

        _appSwitcherService.ShowRequested += (apps, index) =>
            Dispatcher.Invoke(() => _appSwitcherOverlay.ShowWithApps(apps, index));
        _appSwitcherService.SelectionChanged += (index) =>
            Dispatcher.Invoke(() => _appSwitcherOverlay.UpdateSelectedIndex(index));
        _appSwitcherService.HideRequested += () =>
            Dispatcher.Invoke(() => _appSwitcherOverlay.Hide());

        // Create App Navigator service and overlay
        _appNavigatorService = new AppNavigatorService(
            _lowLevelKeyboardHook,
            _shellServices.Tasks,
            hotkey: _shellSettingsService.AppNavigatorHotkey,
            enabled: _shellSettingsService.AppNavigatorEnabled);

        _appNavigatorOverlay = new AppNavigatorOverlay();
        _appNavigatorOverlay.BindService(_appNavigatorService);

        _appNavigatorService.ShowRequested += (groups) =>
            Dispatcher.Invoke(() => _appNavigatorOverlay.ShowAppNavigator(groups));
        _appNavigatorService.HideRequested += (hwnd) =>
            Dispatcher.Invoke(() => _appNavigatorOverlay.DismissAppNavigator(hwnd));

        // Create installed app service and apps launcher
        _installedAppService = new InstalledAppService(_shellSettingsService);
        _ = _installedAppService.ScanAsync(); // fire-and-forget background scan

        _appsLauncher = new AppsLauncherWindow(_installedAppService, _foregroundService!);
        _appsLauncher.Show(); // Show once to get HWND, then collapse via OnSourceInitialized
        _dock.SetAppsLauncher(_appsLauncher);

        // Create system tray icon
        _harborTrayIcon = new HarborTrayIcon(_dockSettingsService, _shellSettingsService);

        // Create recycle bin service and connect to dock
        _recycleBinService = new RecycleBinService();
        _dock.SetRecycleBinService(_recycleBinService);

        // Hide Recycle Bin desktop icon if configured
        _desktopIconService = new DesktopIconService();
        if (_shellSettingsService.HideRecycleBin)
            _desktopIconService.SetRecycleBinHidden(true);

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

    private void OnDockSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_dockSettingsService is not null)
                ApplyAutoHideMode(_dockSettingsService.AutoHideMode);
        });
    }

    private void OnShellSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_shellSettingsService is null) return;

            if (_shellSettingsService.ReplaceExplorer)
            {
                var showIcons = _shellSettingsService.ShowDesktopIcons;

                if (showIcons && _desktopBackground is not null)
                {
                    // User wants desktop icons visible — remove the covering window
                    _desktopBackground.Close();
                    _desktopBackground = null;
                    Trace.WriteLine("[Harbor] App: Desktop background hidden (ShowDesktopIcons=true).");
                }
                else if (!showIcons && _desktopBackground is null && _wallpaperService is not null)
                {
                    // User wants desktop icons hidden — show the covering window
                    _desktopBackground = new DesktopBackgroundWindow();
                    _desktopBackground.Show();
                    _desktopBackground.Initialize(_wallpaperService);
                    Trace.WriteLine("[Harbor] App: Desktop background shown (ShowDesktopIcons=false).");
                }
            }

            // Toggle custom app switcher on/off
            _appSwitcherService?.SetEnabled(_shellSettingsService.UseCustomAppSwitcher);

            // Apply Recycle Bin visibility change (works regardless of ReplaceExplorer)
            _desktopIconService?.SetRecycleBinHidden(_shellSettingsService.HideRecycleBin);
        });
    }

    private void ApplyAutoHideMode(DockAutoHideMode mode)
    {
        // Dispose existing overlap monitor if any
        _overlapMonitor?.Dispose();
        _overlapMonitor = null;

        switch (mode)
        {
            case DockAutoHideMode.Never:
                _dock?.SetAutoHide(false);
                _workAreaService?.Reapply(topInset: ScaledTopInset(), bottomInset: ScaledBottomInset());
                break;

            case DockAutoHideMode.Always:
                _dock?.SetAutoHide(true, startHidden: true);
                _workAreaService?.Reapply(topInset: ScaledTopInset(), bottomInset: 0);
                break;

            case DockAutoHideMode.WhenOverlapped:
                _dock?.SetAutoHide(false);
                _workAreaService?.Reapply(topInset: ScaledTopInset(), bottomInset: ScaledBottomInset());
                if (_windowEventManager is not null)
                {
                    _overlapMonitor = new DockOverlapMonitorService(_windowEventManager, ScaledBottomInset());
                    if (_dock is not null)
                        _overlapMonitor.ExcludedWindow = new Windows.Win32.Foundation.HWND(_dock.Handle);
                    _overlapMonitor.OverlapChanged += OnOverlapChanged;
                }
                break;
        }

        Trace.WriteLine($"[Harbor] App: Applied auto-hide mode: {mode}");
    }

    private DispatcherTimer? _overlapCooldownTimer;

    private void OnOverlapChanged(bool isOverlapped)
    {
        Dispatcher.Invoke(() =>
        {
            // Cancel any pending cooldown
            _overlapCooldownTimer?.Stop();
            _overlapCooldownTimer = null;

            if (isOverlapped)
            {
                _dock?.SetAutoHide(true, startHidden: true);
                _workAreaService?.Reapply(topInset: ScaledTopInset(), bottomInset: 0);
            }
            else
            {
                // Brief cooldown before restoring dock to prevent immediate re-hide feedback loop
                _overlapCooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _overlapCooldownTimer.Tick += (_, _) =>
                {
                    _overlapCooldownTimer?.Stop();
                    _overlapCooldownTimer = null;
                    _dock?.SetAutoHide(false);
                    _workAreaService?.Reapply(topInset: ScaledTopInset(), bottomInset: ScaledBottomInset());
                };
                _overlapCooldownTimer.Start();
            }
        });
    }

    /// <summary>
    /// Menu bar height in DIPs (matches TopMenuBar.xaml Grid height).
    /// </summary>
    private const double MenuBarHeight = 30.0;

    /// <summary>
    /// Returns the DPI scale factor for the dock window (physical pixels per DIP).
    /// Falls back to 1.0 if the dock has no HWND yet.
    /// </summary>
    private double GetDockScale()
    {
        if (_dock is null) return 1.0;
        var hwnd = new HWND(_dock.Handle);
        return hwnd == HWND.Null ? 1.0 : DisplayInterop.GetScaleFactorForWindow(hwnd);
    }

    /// <summary>
    /// Computes the physical-pixel top inset for the menu bar, scaled by DPI.
    /// </summary>
    private int ScaledTopInset() => (int)(MenuBarHeight * GetDockScale());

    /// <summary>
    /// Computes the physical-pixel bottom inset for the dock, scaled by DPI.
    /// </summary>
    private int ScaledBottomInset() => (int)(Dock.DockVisibleHeight * GetDockScale());

    /// <summary>
    /// Fires when the process exits — including forced termination by the debugger.
    /// Restores explorer and work area as a last-resort safety net.
    /// </summary>
    private void OnProcessExit(object? sender, EventArgs e)
    {
        Trace.WriteLine("[Harbor] App: ProcessExit fired.");

        _workAreaService?.Restore();
        _explorerSuppression?.Restore();
        _desktopIconService?.Restore();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Trace.WriteLine($"[Harbor] App: Unhandled exception: {e.ExceptionObject}");
        CrashRecoveryService.ExecuteRecovery(e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Trace.WriteLine($"[Harbor] App: Unobserved task exception: {e.Exception}");
        CrashRecoveryService.ExecuteRecovery(e.Exception);
        e.SetObserved();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Trace.WriteLine($"[Harbor] App: Dispatcher unhandled exception: {e.Exception}");
        CrashRecoveryService.ExecuteRecovery(e.Exception);
    }

    private void LaunchWatchdog()
    {
        try
        {
            var shellDir = AppContext.BaseDirectory;
            // The watchdog is built alongside the shell — look for it relative to the shell output
            var watchdogPath = Path.Combine(shellDir, "..", "harbor-watchdog", "harbor-watchdog.exe");
            watchdogPath = Path.GetFullPath(watchdogPath);

            // Also check same directory (single-folder publish scenario)
            if (!File.Exists(watchdogPath))
            {
                watchdogPath = Path.Combine(shellDir, "harbor-watchdog.exe");
            }

            if (!File.Exists(watchdogPath))
            {
                Trace.WriteLine($"[Harbor] App: Watchdog not found at expected paths. Skipping launch.");
                return;
            }

            _watchdogProcess = Process.Start(new ProcessStartInfo
            {
                FileName = watchdogPath,
                Arguments = Environment.ProcessId.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            Trace.WriteLine($"[Harbor] App: Watchdog launched (PID {_watchdogProcess?.Id}).");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] App: Failed to launch watchdog: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Trace.WriteLine("[Harbor] App: Shutting down...");

        // Stop watchdog before disposing services
        if (_watchdogProcess is { HasExited: false })
        {
            try
            {
                _watchdogProcess.Kill();
                _watchdogProcess.WaitForExit(2000);
                Trace.WriteLine("[Harbor] App: Watchdog process stopped.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] App: Failed to stop watchdog: {ex.Message}");
            }
        }
        _watchdogProcess?.Dispose();
        _watchdogProcess = null;

        // Dispose new services in reverse order of creation
        _trayIconFilter?.Dispose();
        _trayIconFilter = null;

        _safeRemoveService?.Dispose();
        _safeRemoveService = null;

        _batteryService?.Dispose();
        _batteryService = null;

        _networkService?.Dispose();
        _networkService = null;

        _bluetoothService?.Dispose();
        _bluetoothService = null;

        _volumeService?.Dispose();
        _volumeService = null;

        _recycleBinService?.Dispose();
        _recycleBinService = null;

        _appsLauncher?.Close();
        _appsLauncher = null;

        _installedAppService?.Dispose();
        _installedAppService = null;

        _harborTrayIcon?.Dispose();
        _harborTrayIcon = null;

        _appSwitcherService?.Dispose();
        _appSwitcherService = null;

        _appSwitcherOverlay?.Close();
        _appSwitcherOverlay = null;

        _appNavigatorService?.Dispose();
        _appNavigatorService = null;

        _appNavigatorOverlay?.Close();
        _appNavigatorOverlay = null;

        _windowCycleService?.Dispose();
        _windowCycleService = null;

        _lowLevelKeyboardHook?.Dispose();
        _lowLevelKeyboardHook = null;

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

        _dock?.Close();
        _dock = null;

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

        _globalMenuService?.Dispose();
        _globalMenuService = null;

        _foregroundService?.Dispose();
        _foregroundService = null;

        _windowEventManager?.Dispose();
        _windowEventManager = null;

        _overlapMonitor?.Dispose();
        _overlapMonitor = null;

        if (_dockSettingsService is not null)
        {
            _dockSettingsService.SettingsChanged -= OnDockSettingsChanged;
            _dockSettingsService.Dispose();
        }
        _dockSettingsService = null;

        _dockPinningService?.Dispose();
        _dockPinningService = null;

        _heartbeatService?.Dispose();
        _heartbeatService = null;

        _hiddenWindowRegistry?.Dispose();
        _hiddenWindowRegistry = null;

        // Close desktop background and wallpaper services
        _desktopBackground?.Close();
        _desktopBackground = null;

        _wallpaperBrightnessService?.Dispose();
        _wallpaperBrightnessService = null;

        _wallpaperService?.Dispose();
        _wallpaperService = null;

        // Restore work area before restarting explorer
        _workAreaService?.Dispose();
        _workAreaService = null;

        // Unregister exception and process-exit handlers
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;

        // Restore explorer's UI if we suppressed it on startup
        _explorerSuppression?.Dispose();
        _explorerSuppression = null;

        // Restore Recycle Bin AFTER explorer is visible, so the
        // SHChangeNotify refresh is processed by a running shell.
        _desktopIconService?.Dispose();
        _desktopIconService = null;

        if (_shellSettingsService is not null)
        {
            _shellSettingsService.SettingsChanged -= OnShellSettingsChanged;
            _shellSettingsService.Dispose();
        }
        _shellSettingsService = null;

        _shellServices?.Dispose();
        _shellServices = null;

        Trace.WriteLine("[Harbor] App: Shutdown complete.");

        base.OnExit(e);
    }
}
