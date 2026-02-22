using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private DockAutoHideService? _autoHideService;
    private bool _isAutoHideEnabled;

    // Animation constants (match Design.md Section 5B / 5D)
    public const double IconDefaultSize = 48.0;
    public const double IconHoverSize = 56.0;
    public const double IconPressedSize = 44.0;
    public const double HoverScaleFactor = IconHoverSize / IconDefaultSize;   // 1.167
    public const double PressedScaleFactor = IconPressedSize / IconDefaultSize; // 0.917

    public static readonly Duration HoverScaleDuration = new(TimeSpan.FromMilliseconds(150));
    public static readonly Duration PressScaleDownDuration = new(TimeSpan.FromMilliseconds(80));
    public static readonly Duration PressScaleUpDuration = new(TimeSpan.FromMilliseconds(100));

    public const double BounceTranslation = -12.0; // 12 DIP upward (negative Y)
    public const int BounceCount = 3;
    public static readonly Duration SingleBounceDuration = new(TimeSpan.FromMilliseconds(300));
    public static readonly Duration TotalBounceDuration = new(TimeSpan.FromMilliseconds(900));

    public static readonly Duration ShowAnimationDuration = new(TimeSpan.FromMilliseconds(250));
    public static readonly Duration HideAnimationDuration = new(TimeSpan.FromMilliseconds(200));

    // Easing functions matching the spec cubic-bezier curves
    private static readonly IEasingFunction EaseOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
    private static readonly IEasingFunction ShowEasing = new SplineEase(0.16, 1, 0.3, 1);
    private static readonly IEasingFunction HideEasing = new SplineEase(0.7, 0, 0.84, 0);

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

        // Initialize auto-hide service
        _autoHideService = new DockAutoHideService();
        _autoHideService.ShowRequested += OnAutoHideShowRequested;
        _autoHideService.HideRequested += OnAutoHideHideRequested;

        Trace.WriteLine("[Harbor] Dock: Initialized with pinning support, animations, and auto-hide.");
    }

    /// <summary>
    /// Legacy overload for backward compatibility. Creates a default DockPinningService.
    /// </summary>
    public void Initialize(Tasks tasks)
    {
        Initialize(tasks, new DockPinningService());
    }

    /// <summary>
    /// Enables or disables auto-hide behavior.
    /// </summary>
    public void SetAutoHide(bool enabled)
    {
        _isAutoHideEnabled = enabled;
        AutoHideTriggerZone.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled && _autoHideService?.State == DockAutoHideService.AutoHideState.Hidden)
        {
            // Restore dock if it was hidden
            DockSlideTransform.Y = 0;
            DockContainer.Visibility = Visibility.Visible;
        }
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

    #region Icon Hover Animation

    private void DockIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var scaleTransform = FindScaleTransform(element);
        if (scaleTransform is null) return;

        AnimateScale(scaleTransform, HoverScaleFactor, HoverScaleDuration, EaseOut);
    }

    private void DockIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var scaleTransform = FindScaleTransform(element);
        if (scaleTransform is null) return;

        AnimateScale(scaleTransform, 1.0, HoverScaleDuration, EaseOut);
    }

    #endregion

    #region Icon Press Animation

    private void DockIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var scaleTransform = FindScaleTransform(element);
        if (scaleTransform is null) return;

        AnimateScale(scaleTransform, PressedScaleFactor, PressScaleDownDuration, EaseIn);
    }

    /// <summary>
    /// Handles left-click release on a dock icon: restore scale and perform action.
    /// </summary>
    private void DockIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        // Animate scale back to default (or hover size if still hovering)
        var scaleTransform = FindScaleTransform(element);
        if (scaleTransform is not null)
        {
            var targetScale = element.IsMouseOver ? HoverScaleFactor : 1.0;
            AnimateScale(scaleTransform, targetScale, PressScaleUpDuration, EaseOut);
        }

        if (element.DataContext is not DockItem dockItem)
            return;

        // If launching a pinned non-running app, trigger bounce
        if (dockItem.IsPinned && !dockItem.IsRunning)
        {
            StartBounceAnimation(element, dockItem);
        }

        HandleDockIconClick(dockItem);
    }

    #endregion

    #region Launch Bounce Animation

    private void StartBounceAnimation(FrameworkElement element, DockItem dockItem)
    {
        var translateTransform = FindTranslateTransform(element);
        if (translateTransform is null) return;

        dockItem.IsLaunching = true;

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TotalBounceDuration,
        };

        // 3 bounces: each 300ms (150ms up ease-out, 150ms down ease-in)
        for (int i = 0; i < BounceCount; i++)
        {
            var baseTime = TimeSpan.FromMilliseconds(i * 300);
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                BounceTranslation,
                KeyTime.FromTimeSpan(baseTime + TimeSpan.FromMilliseconds(150)),
                EaseOut));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(baseTime + TimeSpan.FromMilliseconds(300)),
                EaseIn));
        }

        animation.Completed += (_, _) => dockItem.IsLaunching = false;
        translateTransform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    #endregion

    #region Auto-Hide

    private void TriggerZone_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isAutoHideEnabled)
            _autoHideService?.OnTriggerZoneEnter();
    }

    private void DockContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoHideService?.OnDockAreaEnter();
    }

    private void DockContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isAutoHideEnabled)
            _autoHideService?.OnDockAreaLeave();
    }

    private void OnAutoHideShowRequested()
    {
        Dispatcher.Invoke(() =>
        {
            DockContainer.Visibility = Visibility.Visible;
            var slideUp = new DoubleAnimation(62, 0, ShowAnimationDuration)
            {
                EasingFunction = ShowEasing,
            };
            slideUp.Completed += (_, _) => _autoHideService?.OnShowAnimationCompleted();
            DockSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        });
    }

    private void OnAutoHideHideRequested()
    {
        Dispatcher.Invoke(() =>
        {
            var slideDown = new DoubleAnimation(0, 62, HideAnimationDuration)
            {
                EasingFunction = HideEasing,
            };
            slideDown.Completed += (_, _) =>
            {
                DockContainer.Visibility = Visibility.Collapsed;
                _autoHideService?.OnHideAnimationCompleted();
            };
            DockSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        });
    }

    #endregion

    #region Click Handling

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

    #endregion

    #region Context Menu

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

    #endregion

    #region Helpers

    private static ScaleTransform? FindScaleTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var transform in group.Children)
            {
                if (transform is ScaleTransform scale)
                    return scale;
            }
        }
        return element.RenderTransform as ScaleTransform;
    }

    private static TranslateTransform? FindTranslateTransform(FrameworkElement element)
    {
        if (element.RenderTransform is TransformGroup group)
        {
            foreach (var transform in group.Children)
            {
                if (transform is TranslateTransform translate)
                    return translate;
            }
        }
        return element.RenderTransform as TranslateTransform;
    }

    private static void AnimateScale(ScaleTransform transform, double targetScale, Duration duration, IEasingFunction easing)
    {
        var animX = new DoubleAnimation(targetScale, duration) { EasingFunction = easing };
        var animY = new DoubleAnimation(targetScale, duration) { EasingFunction = easing };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }

    #endregion

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

        if (_autoHideService is not null)
        {
            _autoHideService.ShowRequested -= OnAutoHideShowRequested;
            _autoHideService.HideRequested -= OnAutoHideHideRequested;
            _autoHideService.Dispose();
            _autoHideService = null;
        }

        _iconService.ClearCache();

        base.OnClosing(e);
    }
}

/// <summary>
/// A spline-based easing function that maps to CSS cubic-bezier(x1, y1, x2, y2).
/// WPF doesn't have a built-in cubic-bezier easing, so we approximate using a KeySpline.
/// </summary>
internal sealed class SplineEase : IEasingFunction
{
    private readonly KeySpline _spline;

    public SplineEase(double x1, double y1, double x2, double y2)
    {
        _spline = new KeySpline(x1, y1, x2, y2);
    }

    public double Ease(double normalizedTime)
    {
        return _spline.GetSplineProgress(normalizedTime);
    }
}
