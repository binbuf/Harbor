using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
    private DockSettingsService? _settingsService;
    private bool _isAutoHideEnabled;
    private bool _magnificationEnabled;

    // Long-press detection for window picker
    private DispatcherTimer? _longPressTimer;
    private FrameworkElement? _longPressElement;
    private bool _longPressTriggered;

    // Recycle bin
    private RecycleBinService? _recycleBinService;

    // Drag reorder state
    private Point _dragStartPoint;
    private DockItem? _dragItem;
    private FrameworkElement? _dragElement;
    private bool _isDragging;
    private System.Windows.Controls.Primitives.Popup? _dragGhost;
    private int _dragFromIndex;
    private int _dragTargetIndex;

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

    // Dock layout constants
    public const double DockWindowHeight = 86.0;   // AppBar window height (includes magnification headroom)
    public const double DockVisibleHeight = 62.0;   // Visible pill height for slide animations

    // Magnification constants
    public const double MagnificationMaxScale = 1.5;
    public const double MagnificationEffectRadius = 3.0;
    public const double MagnificationIconPitch = 56.0; // 48 + 8 margin

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
    /// Initializes the Dock with task data, pinning service, and settings service.
    /// Called after Show() so we have an HWND.
    /// </summary>
    public void Initialize(Tasks tasks, DockPinningService pinningService, DockSettingsService settingsService)
    {
        _settingsService = settingsService;
        _magnificationEnabled = settingsService.MagnificationEnabled;
        settingsService.SettingsChanged += OnSettingsChanged;
        Initialize(tasks, pinningService);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_settingsService is null) return;
        Dispatcher.Invoke(() =>
        {
            _magnificationEnabled = _settingsService.MagnificationEnabled;
            if (!_magnificationEnabled)
                ResetMagnification();
        });
    }

    /// <summary>
    /// Initializes the Dock with task data and pinning service.
    /// Called after Show() so we have an HWND.
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
    /// <param name="enabled">Whether auto-hide is enabled.</param>
    /// <param name="startHidden">If true, the dock starts in the hidden state immediately.</param>
    public void SetAutoHide(bool enabled, bool startHidden = false)
    {
        _isAutoHideEnabled = enabled;
        AutoHideTriggerZone.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled && _autoHideService?.State == DockAutoHideService.AutoHideState.Hidden)
        {
            // Clear animation hold before setting local value (WPF animation precedence fix)
            DockSlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
            DockSlideTransform.Y = 0;
            DockContainer.Visibility = Visibility.Visible;
        }

        if (enabled && startHidden && _autoHideService is not null)
        {
            _autoHideService.ForceHidden();
            DockContainer.Visibility = Visibility.Visible;
            // Animate slide-down instead of instantly hiding
            DockSlideTransform.BeginAnimation(TranslateTransform.YProperty, null);
            DockSlideTransform.Y = 0;
            var slideDown = new DoubleAnimation(0, DockVisibleHeight, HideAnimationDuration)
            {
                EasingFunction = HideEasing,
            };
            slideDown.Completed += (_, _) =>
            {
                DockContainer.Visibility = Visibility.Collapsed;
            };
            DockSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Window-level acrylic removed — DockContainer uses DynamicResource DockBackground
        // for its semi-transparent fill, keeping the dock content-sized (not full-width).
    }

    /// <summary>
    /// No-op: dock uses DynamicResource background on DockContainer only,
    /// not window-level acrylic (which would make the full-width AppBar visible).
    /// </summary>
    public void ApplyThemedAcrylic(AppTheme theme)
    {
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
        if (_magnificationEnabled) return; // Magnification handles scaling globally
        if (sender is not FrameworkElement element) return;

        var scaleTransform = FindScaleTransform(element);
        if (scaleTransform is null) return;

        AnimateScale(scaleTransform, HoverScaleFactor, HoverScaleDuration, EaseOut);
    }

    private void DockIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        // Cancel long-press if mouse leaves without dragging
        if (!_isDragging)
        {
            CancelLongPress();
            element.MouseMove -= DockIcon_MouseMove;
        }

        if (_magnificationEnabled) return; // Magnification handles scaling globally

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

        // Record drag start point
        _dragStartPoint = e.GetPosition(DockPanel);
        _dragElement = element;
        _dragItem = element.DataContext as DockItem;
        _isDragging = false;

        // Start long-press timer for window picker
        _longPressTriggered = false;
        _longPressElement = element;
        _longPressTimer?.Stop();
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _longPressTimer.Tick += OnLongPressTick;
        _longPressTimer.Start();

        // Subscribe to MouseMove on the element for drag detection
        element.MouseMove += DockIcon_MouseMove;
    }

    /// <summary>
    /// Handles left-click release on a dock icon: restore scale and perform action.
    /// </summary>
    private void DockIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        // Unsubscribe mouse move
        element.MouseMove -= DockIcon_MouseMove;

        // Cancel long-press timer
        CancelLongPress();

        // Handle drag end
        if (_isDragging)
        {
            EndDrag();
            return;
        }

        // If long-press was triggered, don't do normal click
        if (_longPressTriggered) return;

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

        if (_magnificationEnabled && !_isDragging)
            AnimateResetMagnification();
    }

    private void OnAutoHideShowRequested()
    {
        Dispatcher.Invoke(() =>
        {
            DockContainer.Visibility = Visibility.Visible;
            var slideUp = new DoubleAnimation(DockVisibleHeight, 0, ShowAnimationDuration)
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
            var slideDown = new DoubleAnimation(0, DockVisibleHeight, HideAnimationDuration)
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

    #region Magnification

    /// <summary>
    /// Handles mouse movement over the dock container when magnification is enabled.
    /// </summary>
    private void DockContainer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_magnificationEnabled || _isDragging) return;

        var mousePos = e.GetPosition(DockPanel);
        ApplyMagnification(mousePos.X);
    }

    private void ApplyMagnification(double mouseX)
    {
        // Collect all icon centers from pinned and running items, plus trash
        var centers = new List<double>();
        var elements = new List<FrameworkElement>();

        CollectIconElements(PinnedIconsControl, centers, elements, DockPanel);
        CollectIconElements(RunningIconsControl, centers, elements, DockPanel);

        // Add trash icon
        var trashPoint = TrashIcon.TranslatePoint(new Point(TrashIcon.ActualWidth / 2, 0), DockPanel);
        centers.Add(trashPoint.X);
        elements.Add(TrashIcon);

        if (centers.Count == 0) return;

        var scales = DockMagnificationCalculator.ComputeScales(
            mouseX,
            centers.ToArray(),
            MagnificationMaxScale,
            MagnificationEffectRadius,
            MagnificationIconPitch);

        for (int i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var scale = scales[i];

            var scaleTransform = FindScaleTransform(element);
            var translateTransform = FindTranslateTransform(element);

            if (scaleTransform is not null)
            {
                // Clear animation clocks before setting local values (WPF animation precedence fix)
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scaleTransform.ScaleX = scale;
                scaleTransform.ScaleY = scale;
            }

            if (translateTransform is not null)
            {
                translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                var offset = DockMagnificationCalculator.ComputeVerticalOffset(scale, IconDefaultSize);
                translateTransform.Y = offset;
            }
        }
    }

    private void ResetMagnification()
    {
        ResetItemsControlScales(PinnedIconsControl);
        ResetItemsControlScales(RunningIconsControl);
    }

    /// <summary>
    /// Called from DockContainer_MouseLeave when magnification is active — resets all scales smoothly.
    /// </summary>
    private void AnimateResetMagnification()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(150));

        void AnimateElement(FrameworkElement element)
        {
            var scaleTransform = FindScaleTransform(element);
            var translateTransform = FindTranslateTransform(element);

            if (scaleTransform is not null)
            {
                AnimateScale(scaleTransform, 1.0, duration, EaseOut);
            }

            if (translateTransform is not null)
            {
                var anim = new DoubleAnimation(0, duration) { EasingFunction = EaseOut };
                translateTransform.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        AnimateItemsControlElements(PinnedIconsControl, AnimateElement);
        AnimateItemsControlElements(RunningIconsControl, AnimateElement);
    }

    private static void CollectIconElements(ItemsControl itemsControl, List<double> centers, List<FrameworkElement> elements, UIElement referencePanel)
    {
        for (int i = 0; i < itemsControl.Items.Count; i++)
        {
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;

            // Find the Grid inside the DataTemplate
            var grid = FindVisualChild<Grid>(container);
            if (grid is null) continue;

            var centerPoint = grid.TranslatePoint(new Point(grid.ActualWidth / 2, 0), referencePanel);
            centers.Add(centerPoint.X);
            elements.Add(grid);
        }
    }

    private static void AnimateItemsControlElements(ItemsControl itemsControl, Action<FrameworkElement> animate)
    {
        for (int i = 0; i < itemsControl.Items.Count; i++)
        {
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;
            var grid = FindVisualChild<Grid>(container);
            if (grid is not null)
                animate(grid);
        }
    }

    private static void ResetItemsControlScales(ItemsControl itemsControl)
    {
        for (int i = 0; i < itemsControl.Items.Count; i++)
        {
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;
            var grid = FindVisualChild<Grid>(container);
            if (grid is null) continue;

            var scaleTransform = FindScaleTransform(grid);
            var translateTransform = FindTranslateTransform(grid);

            if (scaleTransform is not null)
            {
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scaleTransform.ScaleX = 1.0;
                scaleTransform.ScaleY = 1.0;
            }

            if (translateTransform is not null)
            {
                translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                translateTransform.Y = 0;
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
            var result = FindVisualChild<T>(child);
            if (result is not null)
                return result;
        }
        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent is not null)
        {
            if (parent is T typedParent)
                return typedParent;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    #endregion

    #region Click Handling

    /// <summary>
    /// Click-to-activate / click-to-minimize / launch logic.
    /// Supports grouped windows: brings all windows to front or minimizes all.
    /// </summary>
    public static void HandleDockIconClick(DockItem dockItem)
    {
        if (dockItem.IsRunning && dockItem.Windows.Count > 0)
        {
            // Check if any window in the group is currently active
            bool anyActive = dockItem.Windows.Exists(w => w.State == ApplicationWindow.WindowState.Active);

            if (anyActive)
            {
                // Minimize all windows in the group
                Trace.WriteLine($"[Harbor] Dock: Minimizing all windows for: {dockItem.DisplayName}");
                foreach (var window in dockItem.Windows)
                {
                    window.Minimize();
                }
            }
            else
            {
                // Sort by Z-order descending (bottom-of-stack first), so topmost gets activated last
                Trace.WriteLine($"[Harbor] Dock: Activating all windows for: {dockItem.DisplayName}");
                var sorted = dockItem.Windows
                    .OrderByDescending(w => WindowInterop.GetZOrder(new HWND(w.Handle)))
                    .ToList();
                foreach (var window in sorted)
                {
                    window.BringToFront();
                }
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
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not DockItem dockItem) return;

        ShowContextMenuForIcon(element, dockItem);
        e.Handled = true;
    }

    /// <summary>
    /// Shows the context menu for a dock icon. Shared by right-click and long-press.
    /// </summary>
    private void ShowContextMenuForIcon(FrameworkElement element, DockItem dockItem)
    {
        if (_itemManager is null) return;

        var isOpenAtLogin = StartupShortcutService.IsOpenAtLogin(dockItem.ExecutablePath);

        // Build window list for grouped icons with multiple windows
        List<(string Title, IntPtr Handle)>? windowList = null;
        if (dockItem.Windows.Count > 1)
        {
            windowList = dockItem.Windows
                .Select(w => (Title: w.Title ?? "(Untitled)", Handle: w.Handle))
                .ToList();
        }

        var menuItems = DockContextMenuService.GetMenuItems(
            dockItem.ExecutablePath,
            dockItem.DisplayName,
            dockItem.IsPinned,
            dockItem.IsRunning,
            isOpenAtLogin,
            windowList);

        var contextMenu = BuildContextMenu(menuItems, dockItem);
        contextMenu.PlacementTarget = element;
        contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        contextMenu.IsOpen = true;
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
            var windowHandle = item.WindowHandle;
            menuItem.Click += (_, _) => HandleMenuAction(action, dockItem, windowHandle);
            menu.Items.Add(menuItem);
        }

        return menu;
    }

    private void HandleMenuAction(DockMenuAction action, DockItem dockItem, IntPtr windowHandle = default)
    {
        switch (action)
        {
            case DockMenuAction.Open:
                if (dockItem.IsRunning && dockItem.Windows.Count > 0)
                {
                    // Sort by Z-order descending (bottom first), so topmost gets activated last
                    var sortedWindows = dockItem.Windows
                        .OrderByDescending(w => WindowInterop.GetZOrder(new HWND(w.Handle)))
                        .ToList();
                    foreach (var w in sortedWindows)
                    {
                        w.BringToFront();
                    }
                }
                else
                {
                    LaunchApplication(dockItem.ExecutablePath);
                }
                break;

            case DockMenuAction.SwitchToWindow:
                // Find the specific window by handle and bring it to front
                var targetWindow = dockItem.Windows.Find(w => w.Handle == windowHandle);
                if (targetWindow is not null)
                {
                    Trace.WriteLine($"[Harbor] Dock: Switching to window: {targetWindow.Title}");
                    targetWindow.BringToFront();
                }
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

            case DockMenuAction.NewWindow:
                LaunchApplication(dockItem.ExecutablePath);
                break;

            case DockMenuAction.ShowAllWindows:
                if (dockItem.Windows.Count > 0)
                {
                    Trace.WriteLine($"[Harbor] Dock: Showing all windows for: {dockItem.DisplayName}");
                    var sorted = dockItem.Windows
                        .OrderByDescending(w => WindowInterop.GetZOrder(new HWND(w.Handle)))
                        .ToList();
                    foreach (var w in sorted)
                    {
                        w.BringToFront();
                    }
                }
                break;

            case DockMenuAction.Hide:
                if (dockItem.Windows.Count > 0)
                {
                    Trace.WriteLine($"[Harbor] Dock: Hiding all windows for: {dockItem.DisplayName}");
                    foreach (var w in dockItem.Windows)
                    {
                        w.Minimize();
                    }
                }
                break;

            case DockMenuAction.Quit:
                QuitApplication(dockItem);
                break;
        }
    }

    /// <summary>
    /// Sends a close message to all windows belonging to the application.
    /// If the app doesn't close within 3 seconds, offers to force-kill.
    /// </summary>
    private static async void QuitApplication(DockItem dockItem)
    {
        if (dockItem.Windows.Count == 0) return;

        Trace.WriteLine($"[Harbor] Dock: Sending close to all windows of {dockItem.DisplayName}");

        // Send SC_CLOSE to all windows in the group
        var handles = dockItem.Windows.Select(w => w.Handle).ToList();
        foreach (var hwnd in handles)
        {
            WindowInterop.PostSysCommand(new HWND(hwnd), WindowInterop.SC_CLOSE);
        }

        // Wait up to 3 seconds for all windows to close
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            if (handles.All(h => !WindowInterop.IsWindow(new HWND(h))))
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
                // Get unique process IDs from remaining windows
                var processIds = new HashSet<uint>();
                foreach (var hwnd in handles)
                {
                    if (WindowInterop.IsWindow(new HWND(hwnd)))
                    {
                        WindowInterop.GetWindowThreadProcessId(new HWND(hwnd), out var processId);
                        if (processId != 0)
                            processIds.Add(processId);
                    }
                }

                foreach (var pid in processIds)
                {
                    var process = Process.GetProcessById((int)pid);
                    process.Kill();
                    Trace.WriteLine($"[Harbor] Dock: Force-killed {dockItem.DisplayName} (PID {pid}).");
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

    #region Long-Press Window Picker

    private void OnLongPressTick(object? sender, EventArgs e)
    {
        _longPressTimer?.Stop();
        _longPressTriggered = true;

        if (_longPressElement?.DataContext is DockItem dockItem)
        {
            // Restore scale
            var scaleTransform = FindScaleTransform(_longPressElement);
            if (scaleTransform is not null)
                AnimateScale(scaleTransform, 1.0, PressScaleUpDuration, EaseOut);

            ShowContextMenuForIcon(_longPressElement, dockItem);
        }
    }

    private void CancelLongPress()
    {
        _longPressTimer?.Stop();
        _longPressTimer = null;
        _longPressElement = null;
    }

    #endregion

    #region Trash / Recycle Bin

    public void SetRecycleBinService(RecycleBinService service)
    {
        _recycleBinService = service;
        _recycleBinService.PropertyChanged += OnRecycleBinChanged;
        TrashIconImage.Source = _recycleBinService.CurrentIcon;
    }

    private void OnRecycleBinChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RecycleBinService.CurrentIcon))
        {
            Dispatcher.Invoke(() => TrashIconImage.Source = _recycleBinService?.CurrentIcon);
        }
    }

    private void TrashIcon_Click(object sender, MouseButtonEventArgs e)
    {
        _recycleBinService?.Open();
    }

    private void TrashIcon_RightClick(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "Open" };
        openItem.Click += (_, _) => _recycleBinService?.Open();
        menu.Items.Add(openItem);

        var emptyItem = new MenuItem { Header = "Empty Trash" };
        emptyItem.Click += (_, _) => _recycleBinService?.Empty();
        menu.Items.Add(emptyItem);

        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void TrashIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        // No scale animation for trash — it's a fixed dock element
    }

    private void TrashIcon_MouseLeave(object sender, MouseEventArgs e)
    {
    }

    #endregion

    #region Drag Reorder

    private void DockIcon_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var currentPos = e.GetPosition(DockPanel);
        var delta = currentPos - _dragStartPoint;

        // Movement threshold — if moved more than 5px, start drag (cancel long-press)
        if (!_isDragging && Math.Abs(delta.X) > 5)
        {
            // Only drag pinned items
            if (_dragItem is not { IsPinned: true }) return;

            CancelLongPress();
            _isDragging = true;
            _dragFromIndex = GetPinnedIndex(_dragItem);
            _dragTargetIndex = _dragFromIndex;

            Mouse.Capture(element);

            // Create ghost popup
            _dragGhost = new System.Windows.Controls.Primitives.Popup
            {
                AllowsTransparency = true,
                IsHitTestVisible = false,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute,
                Child = new Border
                {
                    Opacity = 0.7,
                    Child = new Image
                    {
                        Source = _dragItem.Icon,
                        Width = 48,
                        Height = 48,
                    }
                }
            };

            element.Opacity = 0.3;
            UpdateGhostPosition(element, e);
            _dragGhost.IsOpen = true;
        }

        if (_isDragging)
        {
            UpdateGhostPosition(element, e);
            UpdateDragTarget(e);
        }
    }

    private void UpdateGhostPosition(FrameworkElement element, MouseEventArgs e)
    {
        if (_dragGhost is null) return;
        var screenPos = element.PointToScreen(e.GetPosition(element));
        _dragGhost.HorizontalOffset = screenPos.X - 24;
        _dragGhost.VerticalOffset = screenPos.Y - 24;
    }

    private void UpdateDragTarget(MouseEventArgs e)
    {
        if (_itemManager is null) return;

        var pos = e.GetPosition(PinnedIconsControl);
        var itemWidth = 56.0; // 48 icon + 4+4 margin
        var newIndex = Math.Clamp((int)(pos.X / itemWidth), 0, _itemManager.PinnedItems.Count - 1);
        _dragTargetIndex = newIndex;
    }

    private void EndDrag()
    {
        if (_dragElement is not null)
        {
            _dragElement.Opacity = 1.0;
            _dragElement.MouseMove -= DockIcon_MouseMove;
            Mouse.Capture(null);
        }

        if (_dragGhost is not null)
        {
            _dragGhost.IsOpen = false;
            _dragGhost = null;
        }

        // Perform reorder if moved
        if (_dragFromIndex != _dragTargetIndex && _itemManager is not null)
        {
            _itemManager.PinningService.Reorder(_dragFromIndex, _dragTargetIndex);
        }

        // Restore scale
        if (_dragElement is not null)
        {
            var scaleTransform = FindScaleTransform(_dragElement);
            if (scaleTransform is not null)
                AnimateScale(scaleTransform, 1.0, PressScaleUpDuration, EaseOut);
        }

        _isDragging = false;
        _dragItem = null;
        _dragElement = null;
    }

    private int GetPinnedIndex(DockItem item)
    {
        if (_itemManager is null) return -1;
        for (int i = 0; i < _itemManager.PinnedItems.Count; i++)
        {
            if (string.Equals(_itemManager.PinnedItems[i].ExecutablePath, item.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
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

        if (_settingsService is not null)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
            _settingsService = null;
        }

        if (_autoHideService is not null)
        {
            _autoHideService.ShowRequested -= OnAutoHideShowRequested;
            _autoHideService.HideRequested -= OnAutoHideHideRequested;
            _autoHideService.Dispose();
            _autoHideService = null;
        }

        if (_recycleBinService is not null)
        {
            _recycleBinService.PropertyChanged -= OnRecycleBinChanged;
            _recycleBinService = null;
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
