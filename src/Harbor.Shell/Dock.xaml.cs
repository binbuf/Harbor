using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTasks;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

public partial class Dock : AppBarWindow
{
    private DockItemManager? _itemManager;
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
    }

    /// <summary>
    /// Initializes the Dock with task data and pinning service.
    /// Called after Show() so we have an HWND for acrylic.
    /// </summary>
    public void Initialize(Tasks tasks, DockPinningService pinningService)
    {
        _itemManager = new DockItemManager(pinningService, _iconService);
        _itemManager.Initialize(tasks);

        PinnedIconsControl.ItemsSource = _itemManager.PinnedItems;
        RunningIconsControl.ItemsSource = _itemManager.RunningItems;

        _itemManager.PinnedItems.CollectionChanged += OnItemsChanged;
        _itemManager.RunningItems.CollectionChanged += OnItemsChanged;

        UpdateSeparatorVisibility();

        Trace.WriteLine("[Harbor] Dock: Initialized with pinning support and icon extraction.");
    }

    /// <summary>
    /// Legacy overload for backward compatibility. Creates a default DockPinningService.
    /// </summary>
    public void Initialize(Tasks tasks)
    {
        Initialize(tasks, new DockPinningService());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyAcrylic();
    }

    // Acrylic color constants (AABBGGRR format for SetWindowCompositionAttribute)
    public const uint DarkAcrylicColor = 0x801E1E1E;  // #1E1E1E @ 50%
    public const uint LightAcrylicColor = 0x80F6F6F6; // #F6F6F6 @ 50%

    private void ApplyAcrylic()
    {
        ApplyThemedAcrylic(ThemeService.ReadThemeFromRegistry());
    }

    /// <summary>
    /// Applies acrylic blur with the correct background tint for the given theme.
    /// Called on startup and when the system theme changes.
    /// </summary>
    public void ApplyThemedAcrylic(AppTheme theme)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var acrylicColor = theme == AppTheme.Light ? LightAcrylicColor : DarkAcrylicColor;
        var result = CompositionInterop.EnableAcrylic(new HWND(hwnd), acrylicColor);

        if (result)
            Trace.WriteLine($"[Harbor] Dock: Acrylic applied for {theme} theme.");
        else
            Trace.WriteLine("[Harbor] Dock: Acrylic failed, using solid fallback.");
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSeparatorVisibility();
    }

    private void UpdateSeparatorVisibility()
    {
        if (_itemManager is null) return;
        DockSeparator.Visibility = _itemManager.ShowSeparator
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles left-click on a dock icon.
    /// </summary>
    private void DockIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItem dockItem })
            return;

        HandleDockIconClick(dockItem);
    }

    /// <summary>
    /// Click-to-activate / click-to-minimize / launch logic.
    /// </summary>
    public static void HandleDockIconClick(DockItem dockItem)
    {
        if (dockItem.IsRunning && dockItem.Window is not null)
        {
            if (dockItem.Window.State == ApplicationWindow.WindowState.Active)
            {
                Trace.WriteLine($"[Harbor] Dock: Minimizing active window: {dockItem.DisplayName}");
                dockItem.Window.Minimize();
            }
            else
            {
                Trace.WriteLine($"[Harbor] Dock: Activating window: {dockItem.DisplayName}");
                dockItem.Window.BringToFront();
            }
        }
        else if (dockItem.IsPinned && !dockItem.IsRunning)
        {
            // Pinned app that isn't running — launch it
            LaunchApplication(dockItem.ExecutablePath);
        }
    }

    /// <summary>
    /// Launches an application from its executable path.
    /// </summary>
    private static void LaunchApplication(string executablePath)
    {
        try
        {
            Trace.WriteLine($"[Harbor] Dock: Launching pinned app: {executablePath}");
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] Dock: Failed to launch {executablePath}: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles right-click on a dock icon — shows context menu.
    /// </summary>
    private void DockIcon_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DockItem dockItem })
            return;

        if (_itemManager is null) return;

        var isOpenAtLogin = StartupShortcutService.IsOpenAtLogin(dockItem.ExecutablePath);
        var menuItems = DockContextMenuService.GetMenuItems(
            dockItem.ExecutablePath,
            dockItem.DisplayName,
            dockItem.IsPinned,
            dockItem.IsRunning,
            isOpenAtLogin);

        var contextMenu = BuildContextMenu(menuItems, dockItem);
        contextMenu.PlacementTarget = (UIElement)sender;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        contextMenu.IsOpen = true;

        e.Handled = true;
    }

    private ContextMenu BuildContextMenu(List<DockMenuItem> items, DockItem dockItem)
    {
        var menu = new ContextMenu();

        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            if (item.IsSubmenuHeader && item.Children is not null)
            {
                var parent = new MenuItem { Header = item.Label };
                foreach (var child in item.Children)
                {
                    var childMenuItem = new MenuItem
                    {
                        Header = child.Label,
                        IsCheckable = true,
                        IsChecked = child.IsChecked,
                    };
                    var childAction = child.Action;
                    childMenuItem.Click += (_, _) => HandleMenuAction(childAction, dockItem);
                    parent.Items.Add(childMenuItem);
                }
                menu.Items.Add(parent);
                continue;
            }

            var menuItem = new MenuItem { Header = item.Label };
            var action = item.Action;
            menuItem.Click += (_, _) => HandleMenuAction(action, dockItem);
            menu.Items.Add(menuItem);
        }

        return menu;
    }

    private void HandleMenuAction(DockMenuAction action, DockItem dockItem)
    {
        switch (action)
        {
            case DockMenuAction.Open:
                if (dockItem.IsRunning && dockItem.Window is not null)
                    dockItem.Window.BringToFront();
                else
                    LaunchApplication(dockItem.ExecutablePath);
                break;

            case DockMenuAction.KeepInDock:
                if (_itemManager is not null)
                {
                    if (_itemManager.PinningService.IsPinned(dockItem.ExecutablePath))
                        _itemManager.PinningService.Unpin(dockItem.ExecutablePath);
                    else
                        _itemManager.PinningService.Pin(dockItem.ExecutablePath, dockItem.DisplayName);
                }
                break;

            case DockMenuAction.RemoveFromDock:
                _itemManager?.PinningService.Unpin(dockItem.ExecutablePath);
                break;

            case DockMenuAction.OpenAtLogin:
                StartupShortcutService.Toggle(dockItem.ExecutablePath);
                break;

            case DockMenuAction.Quit:
                QuitApplication(dockItem);
                break;
        }
    }

    /// <summary>
    /// Sends a close message to the application's main window.
    /// If the app doesn't close within 3 seconds, offers to force-kill.
    /// </summary>
    private static async void QuitApplication(DockItem dockItem)
    {
        if (dockItem.Window is null) return;

        var hwnd = dockItem.Window.Handle;
        Trace.WriteLine($"[Harbor] Dock: Sending close to {dockItem.DisplayName}");

        // Send SC_CLOSE via WM_SYSCOMMAND to the main window
        WindowInterop.PostSysCommand(new HWND(hwnd), WindowInterop.SC_CLOSE);

        // Wait up to 3 seconds for the app to close
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            if (!WindowInterop.IsWindow(new HWND(hwnd)))
            {
                Trace.WriteLine($"[Harbor] Dock: {dockItem.DisplayName} closed gracefully.");
                return;
            }
        }

        // App didn't close — offer to force-kill
        Trace.WriteLine($"[Harbor] Dock: {dockItem.DisplayName} did not close within 3 seconds.");
        var result = MessageBox.Show(
            $"\"{dockItem.DisplayName}\" is not responding. Do you want to force quit?",
            "Harbor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                WindowInterop.GetWindowThreadProcessId(new HWND(hwnd), out var processId);
                if (processId != 0)
                {
                    var process = Process.GetProcessById((int)processId);
                    process.Kill();
                    Trace.WriteLine($"[Harbor] Dock: Force-killed {dockItem.DisplayName} (PID {processId}).");
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] Dock: Failed to force-kill: {ex.Message}");
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        PinnedIconsControl.ItemsSource = null;
        RunningIconsControl.ItemsSource = null;

        if (_itemManager is not null)
        {
            _itemManager.PinnedItems.CollectionChanged -= OnItemsChanged;
            _itemManager.RunningItems.CollectionChanged -= OnItemsChanged;
            _itemManager.Dispose();
            _itemManager = null;
        }

        _iconService.ClearCache();

        base.OnClosing(e);
    }
}
