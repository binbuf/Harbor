using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using ManagedShell.WindowsTasks;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

public partial class Dock : Window, IRetreatable
{
    private DockItemManager? _itemManager;
    private readonly IconExtractionService _iconService = new();
    private DockAutoHideService? _autoHideService;
    private DockSettingsService? _settingsService;
    private bool _isAutoHideEnabled;
    private bool _magnificationEnabled;
    private bool _isMagnificationTracking;

    // Long-press detection for window picker
    private DispatcherTimer? _longPressTimer;
    private FrameworkElement? _longPressElement;
    private bool _longPressTriggered;

    // Recycle bin
    private RecycleBinService? _recycleBinService;

    // Apps launcher
    private AppsLauncherWindow? _appsLauncher;

    // Active context menu tracking
    private ContextMenu? _activeContextMenu;
    private ContextMenuMouseHook? _contextMenuMouseHook;

    // Auto-hide bottom-edge tracking: polls cursor position when mouse exits
    // DockContainer horizontally at the bottom edge (transparent area = no WPF mouse events)
    private DispatcherTimer? _bottomEdgeTimer;

    // Drag reorder state
    private Point _dragStartPoint;
    private DockItem? _dragItem;
    private FrameworkElement? _dragElement;
    private bool _isDragging;
    private System.Windows.Controls.Primitives.Popup? _dragGhost;
    private int _dragFromIndex;
    private int _dragTargetIndex;
    private bool _dragSourceWasPinned;

    // Animation constants (match Design.md Section 5B / 5D)
    public const double IconDefaultSize = 52.0;
    public const double IconHoverSize = 61.0;
    public const double IconPressedSize = 48.0;
    public const double HoverScaleFactor = IconHoverSize / IconDefaultSize;   // 1.173
    public const double PressedScaleFactor = IconPressedSize / IconDefaultSize; // 0.923

    public static readonly Duration HoverScaleDuration = new(TimeSpan.FromMilliseconds(150));
    public static readonly Duration PressScaleDownDuration = new(TimeSpan.FromMilliseconds(80));
    public static readonly Duration PressScaleUpDuration = new(TimeSpan.FromMilliseconds(100));

    public const double BounceTranslation = -13.0; // 13 DIP upward (negative Y)
    public const int BounceCount = 3;
    public static readonly Duration SingleBounceDuration = new(TimeSpan.FromMilliseconds(300));
    public static readonly Duration TotalBounceDuration = new(TimeSpan.FromMilliseconds(900));

    public static readonly Duration ShowAnimationDuration = new(TimeSpan.FromMilliseconds(400));
    public static readonly Duration HideAnimationDuration = new(TimeSpan.FromMilliseconds(350));

    // Dock layout constants
    public const double DockWindowHeight = 160.0;   // AppBar window height (includes magnification headroom)
    public const double DockVisibleHeight = 83.0;   // Visible pill height for slide animations

    // Magnification constants
    public const double MagnificationMaxScale = 1.4;
    public const double MagnificationEffectRadius = 2.1;
    public const double MagnificationIconPitch = 80.0; // 52 + 28 margin
    public const double DockContainerVerticalPadding = 22.0; // top(12) + bottom(8) + border for container height calc
    public static readonly Duration MagnificationHeightAnimDuration = new(TimeSpan.FromMilliseconds(80));

    // Easing functions matching the spec cubic-bezier curves
    private static readonly IEasingFunction EaseOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
    private static readonly IEasingFunction ShowEasing = new SplineEase(0.16, 1, 0.3, 1);
    private static readonly IEasingFunction HideEasing = new SplineEase(0.7, 0, 0.84, 0);

    public Dock()
    {
        InitializeComponent();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        // Close any open context menu when the dock window loses focus
        if (_activeContextMenu is { IsOpen: true })
            _activeContextMenu.IsOpen = false;
    }

    /// <summary>
    /// Returns the native window handle.
    /// </summary>
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>
    /// Positions the dock at the absolute bottom of the primary monitor,
    /// spanning the full monitor width. Uses physical monitor bounds (rcMonitor)
    /// and SetWindowPos to bypass WPF's layout system entirely.
    /// </summary>
    public void UpdatePosition()
    {
        var hwnd = new HWND(Handle);
        if (hwnd == HWND.Null)
        {
            Trace.WriteLine("[Harbor] Dock: UpdatePosition skipped — HWND is null.");
            return;
        }

        var bounds = DisplayInterop.GetMonitorBounds(hwnd);
        if (bounds is null)
        {
            Trace.WriteLine("[Harbor] Dock: UpdatePosition skipped — GetMonitorBounds returned null.");
            return;
        }

        var rc = bounds.Value;
        var scale = DisplayInterop.GetScaleFactorForWindow(hwnd);
        var physicalHeight = (int)(DockWindowHeight * scale);
        var x = rc.left;
        var y = rc.bottom - physicalHeight;
        var cx = rc.right - rc.left;
        var cy = physicalHeight;

        Trace.WriteLine($"[Harbor] Dock: UpdatePosition: rcMonitor=({rc.left},{rc.top},{rc.right},{rc.bottom}) scale={scale} phyH={physicalHeight} -> SetWindowPos({x},{y},{cx},{cy})");

        // Use SetWindowPos directly with physical pixel coordinates from rcMonitor.
        // This is more reliable than WPF's Left/Top properties, which may be overridden
        // by the layout system during window initialization.
        WindowInterop.SetWindowPos(
            hwnd,
            new HWND(-1), // HWND_TOPMOST
            x, y, cx, cy,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
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
            {
                StopMagnificationTracking();
                ResetMagnification();
            }
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
            PeekIndicator.Visibility = Visibility.Collapsed;
        }

        if (enabled && startHidden && _autoHideService is not null)
        {
            _autoHideService.ForceHidden();
            DockContainer.Visibility = Visibility.Visible;
            PeekIndicator.Visibility = Visibility.Collapsed;
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
                PeekIndicator.Visibility = Visibility.Visible;
            };
            DockSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        }
    }

    // WndProc hook constants
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private static readonly int WposInsertAfterOffset = IntPtr.Size; // hwndInsertAfter follows hwnd in WINDOWPOS

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Add WS_EX_TOOLWINDOW to prevent the dock from appearing in Alt+Tab
        var hwnd = new HWND(Handle);
        var exStyle = WindowInterop.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (nint)(WindowInterop.WS_EX_TOOLWINDOW | WindowInterop.WS_EX_NOACTIVATE);
        WindowInterop.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);

        UpdatePosition();

        // Hook WndProc to enforce HWND_TOPMOST on any position change
        var source = HwndSource.FromHwnd(Handle);
        source?.AddHook(DockWndProc);
    }

    private IntPtr DockWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_WINDOWPOSCHANGING:
                // Force HWND_TOPMOST (-1) so WPF or other windows can't push us down the z-order
                Marshal.WriteIntPtr(lParam, WposInsertAfterOffset, (IntPtr)(-1));
                break;

            case WM_SYSCOMMAND:
                // Block minimize (triggered by "show desktop" or Win+D)
                if ((wParam.ToInt32() & 0xFFF0) == SC_MINIMIZE)
                    handled = true;
                break;
        }
        return IntPtr.Zero;
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

        // Skip ScaleTransform press animation when magnification is active —
        // magnification uses Width/Height and the two would compound (double-scaling)
        if (!_magnificationEnabled)
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
        // Skip when magnification is active — it controls sizing via Width/Height
        if (!_magnificationEnabled)
        {
            var scaleTransform = FindScaleTransform(element);
            if (scaleTransform is not null)
            {
                var targetScale = element.IsMouseOver ? HoverScaleFactor : 1.0;
                AnimateScale(scaleTransform, targetScale, PressScaleUpDuration, EaseOut);
            }
        }

        if (element.DataContext is not DockItem dockItem)
            return;

        // Special handling for the Apps launcher sentinel
        if (string.Equals(dockItem.ExecutablePath, IconExtractionService.AppsLauncherSentinel, StringComparison.OrdinalIgnoreCase))
        {
            _appsLauncher?.Toggle();
            return;
        }

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

    private void TriggerZone_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isAutoHideEnabled) return;

        // If the mouse is still in the bottom region (trigger zone + container margin gap),
        // don't trigger hide — wait for DockContainer_MouseEnter or DockRoot_MouseMove to decide.
        var pos = e.GetPosition(DockRoot);
        var bottomRegionTop = DockRoot.ActualHeight - AutoHideTriggerZone.ActualHeight
                              - DockContainer.Margin.Bottom;
        if (pos.Y >= bottomRegionTop)
            return;

        _autoHideService?.OnDockAreaLeave();
    }

    private void DockContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        StopBottomEdgeTimer();
        _autoHideService?.OnDockAreaEnter();
        StartMagnificationTracking();
    }

    private void DockContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isAutoHideEnabled)
        {
            // When magnification tracking is active, layout changes from icon resizing generate
            // synthetic MouseLeave events. Skip auto-hide logic here — OnMagnificationFrame
            // polls the actual cursor position and will trigger hide when the mouse truly exits.
            if (!_isMagnificationTracking)
            {
                // Keep dock visible while mouse is anywhere at the bottom screen edge
                // (trigger zone + container margin gap). Only hide when mouse moves above this region.
                var pos = e.GetPosition(DockRoot);
                var bottomRegionTop = DockRoot.ActualHeight - AutoHideTriggerZone.ActualHeight
                                      - DockContainer.Margin.Bottom;
                if (pos.Y < bottomRegionTop)
                {
                    StopBottomEdgeTimer();
                    _autoHideService?.OnDockAreaLeave();
                }
                else
                {
                    // Mouse exited DockContainer horizontally at the bottom edge.
                    // Start polling cursor position since transparent areas don't get mouse events.
                    StartBottomEdgeTimer();
                }
            }
        }
    }

    private void StartBottomEdgeTimer()
    {
        if (_bottomEdgeTimer is not null) return;
        _bottomEdgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _bottomEdgeTimer.Tick += BottomEdgeTimer_Tick;
        _bottomEdgeTimer.Start();
    }

    private void StopBottomEdgeTimer()
    {
        if (_bottomEdgeTimer is null) return;
        _bottomEdgeTimer.Stop();
        _bottomEdgeTimer.Tick -= BottomEdgeTimer_Tick;
        _bottomEdgeTimer = null;
    }

    private void BottomEdgeTimer_Tick(object? sender, EventArgs e)
    {
        // If dock is no longer visible/hiding, or mouse re-entered DockContainer, stop polling
        var state = _autoHideService?.State;
        if (state != DockAutoHideService.AutoHideState.Visible &&
            state != DockAutoHideService.AutoHideState.Hiding)
        {
            StopBottomEdgeTimer();
            return;
        }

        // Check if mouse is inside DockContainer — if so, it's handled by container events
        var containerPos = Mouse.GetPosition(DockContainer);
        if (containerPos.X >= 0 && containerPos.X <= DockContainer.ActualWidth
            && containerPos.Y >= 0 && containerPos.Y <= DockContainer.ActualHeight)
        {
            StopBottomEdgeTimer();
            return;
        }

        // Check if mouse moved above the bottom safe region
        var pos = Mouse.GetPosition(DockRoot);
        var bottomRegionTop = DockRoot.ActualHeight - AutoHideTriggerZone.ActualHeight
                              - DockContainer.Margin.Bottom;
        if (pos.Y < bottomRegionTop)
        {
            StopBottomEdgeTimer();
            _autoHideService?.OnDockAreaLeave();
        }
    }

    private void OnAutoHideShowRequested()
    {
        Dispatcher.Invoke(() =>
        {
            PeekIndicator.Visibility = Visibility.Collapsed;
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
        StopBottomEdgeTimer();
        Dispatcher.Invoke(() =>
        {
            // Stop magnification tracking and reset icon scales before hiding
            StopMagnificationTracking();
            ResetMagnification();

            var slideDown = new DoubleAnimation(0, DockVisibleHeight, HideAnimationDuration)
            {
                EasingFunction = HideEasing,
            };
            slideDown.Completed += (_, _) =>
            {
                DockContainer.Visibility = Visibility.Collapsed;
                PeekIndicator.Visibility = Visibility.Visible;
                _autoHideService?.OnHideAnimationCompleted();
            };
            DockSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        });
    }

    #endregion

    #region Magnification

    /// <summary>
    /// Handles mouse movement over the dock container (non-magnification uses, e.g. drag).
    /// Magnification is driven by CompositionTarget.Rendering to avoid layout-triggered
    /// false MouseLeave events.
    /// </summary>
    private void DockContainer_MouseMove(object sender, MouseEventArgs e)
    {
        // Magnification is handled by the rendering loop (OnMagnificationFrame),
        // not by this MouseMove handler.
    }

    private void StartMagnificationTracking()
    {
        if (!_magnificationEnabled || _isMagnificationTracking) return;
        _isMagnificationTracking = true;
        CompositionTarget.Rendering += OnMagnificationFrame;
    }

    private void StopMagnificationTracking()
    {
        if (!_isMagnificationTracking) return;
        _isMagnificationTracking = false;
        CompositionTarget.Rendering -= OnMagnificationFrame;
    }

    private void OnMagnificationFrame(object? sender, EventArgs e)
    {
        if (!_magnificationEnabled || _isDragging)
        {
            StopMagnificationTracking();
            AnimateResetMagnification();
            return;
        }

        // Poll cursor position relative to DockRoot (stable bounds — never changes during magnification)
        var pos = Mouse.GetPosition(DockRoot);

        // Check if mouse has left the dock zone.
        // DockRoot is 160px tall; the dock pill occupies the lower ~90px.
        // Use a generous upper bound to account for magnified icons popping upward.
        var dockZoneTop = DockRoot.ActualHeight - 120;
        if (pos.X < -20 || pos.X > DockRoot.ActualWidth + 20 ||
            pos.Y < dockZoneTop || pos.Y > DockRoot.ActualHeight + 10)
        {
            StopMagnificationTracking();
            AnimateResetMagnification();

            // Only trigger auto-hide when the mouse moved upward out of the bottom region.
            // If the mouse exited horizontally but is still near the bottom screen edge,
            // keep the dock visible (consistent with DockContainer_MouseLeave behavior).
            if (_isAutoHideEnabled)
            {
                var bottomRegionTop = DockRoot.ActualHeight - AutoHideTriggerZone.ActualHeight
                                      - DockContainer.Margin.Bottom;
                if (pos.Y < bottomRegionTop)
                    _autoHideService?.OnDockAreaLeave();
                else
                    StartBottomEdgeTimer();
            }

            return;
        }

        var panelPos = Mouse.GetPosition(DockPanel);
        ApplyMagnification(panelPos.X);
    }

    private void ApplyMagnification(double mouseX)
    {
        // Collect icon elements (no TranslatePoint — we compute rest centers from indices)
        var elements = new List<FrameworkElement>();
        CollectIconElementsOnly(PinnedIconsControl, elements);
        CollectIconElementsOnly(RunningIconsControl, elements);
        elements.Add(TrashIcon);

        if (elements.Count == 0) return;

        // Compute rest centers relative to DockPanel (starting from 0)
        var (centers, restPanelWidth) = ComputeRestCenters(elements.Count);

        // Correct mouseX for container re-centering shift.
        // DockContainer is HorizontalAlignment="Center", so when icons magnify and DockPanel
        // grows wider, the container shifts left by (growth / 2). The mouse position relative
        // to DockPanel increases by the same amount. Subtracting growth/2 maps back to
        // rest-space, giving a stable coordinate immune to magnification feedback.
        var growth = DockPanel.ActualWidth - restPanelWidth;
        var mouseRestX = mouseX - growth / 2.0;

        var scales = DockMagnificationCalculator.ComputeScales(
            mouseRestX,
            centers,
            MagnificationMaxScale,
            MagnificationEffectRadius,
            MagnificationIconPitch);

        for (int i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var scale = scales[i];
            var scaledIconSize = IconDefaultSize * scale;
            var image = FindVisualChild<Image>(element);
            var translateTransform = FindTranslateTransform(element);

            // Only grow Width (layout) — pill expands horizontally but not vertically.
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            element.Width = scaledIconSize;

            // Scale the Image visually via RenderTransform (no layout change, no height growth).
            // Origin (0.5, 0) = top-center, so the image grows downward + sideways.
            // The Grid's TranslateTransform pulls everything upward, keeping it within the pill.
            if (image is not null)
            {
                image.RenderTransformOrigin = new Point(0.5, 0);
                if (image.RenderTransform is ScaleTransform imgScale)
                {
                    imgScale.ScaleX = scale;
                    imgScale.ScaleY = scale;
                }
                else
                {
                    image.RenderTransform = new ScaleTransform(scale, scale);
                }
            }

            if (translateTransform is not null)
            {
                translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                // Shift up to compensate for the visual growth downward + pop-out effect
                var layoutOffset = -IconDefaultSize * (scale - 1);
                var popOut = DockMagnificationCalculator.ComputeVerticalOffset(scale, IconDefaultSize);
                translateTransform.Y = layoutOffset + popOut;
            }
        }
    }

    private void ResetMagnification()
    {
        ResetItemsControlScales(PinnedIconsControl);
        ResetItemsControlScales(RunningIconsControl);

        // Reset trash icon
        ResetElementMagnification(TrashIcon);
    }

    /// <summary>
    /// Called from DockContainer_MouseLeave when magnification is active — resets all scales smoothly.
    /// </summary>
    private void AnimateResetMagnification()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(150));

        void AnimateElement(FrameworkElement element)
        {
            var image = FindVisualChild<Image>(element);
            var translateTransform = FindTranslateTransform(element);

            // Animate Width back to default (layout-affecting, pill shrinks horizontally)
            var widthAnim = new DoubleAnimation(IconDefaultSize, duration) { EasingFunction = EaseOut };
            element.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

            // Animate image RenderTransform scale back to 1.0
            if (image?.RenderTransform is ScaleTransform imgScale)
            {
                imgScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(1.0, duration) { EasingFunction = EaseOut });
                imgScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(1.0, duration) { EasingFunction = EaseOut });
            }

            if (translateTransform is not null)
            {
                var anim = new DoubleAnimation(0, duration) { EasingFunction = EaseOut };
                translateTransform.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }

        AnimateItemsControlElements(PinnedIconsControl, AnimateElement);
        AnimateItemsControlElements(RunningIconsControl, AnimateElement);

        // Also animate trash icon back
        AnimateElement(TrashIcon);
    }

    private static void CollectIconElementsOnly(ItemsControl itemsControl, List<FrameworkElement> elements)
    {
        for (int i = 0; i < itemsControl.Items.Count; i++)
        {
            var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;

            var grid = FindVisualChild<Grid>(container);
            if (grid is not null)
                elements.Add(grid);
        }
    }

    /// <summary>
    /// Computes rest-state center X positions for all dock icons relative to DockPanel.
    /// Uses pure math based on item count and separator visibility — no TranslatePoint,
    /// so magnification-induced layout shifts cannot feed back into the calculation.
    /// Also returns the rest-state width of DockPanel content for growth correction.
    /// </summary>
    private (double[] centers, double restPanelWidth) ComputeRestCenters(int totalIconCount)
    {
        // Count icons per section
        var pinnedCount = PinnedIconsControl.Items.Count;
        var runningCount = RunningIconsControl.Items.Count;
        const int trashCount = 1;

        // Separator widths (1px border + 15px margin each side = 31px each, when visible)
        var dockSepWidth = DockSeparator.Visibility == Visibility.Visible ? 31.0 : 0.0;
        const double trashSepWidth = 31.0; // TrashSeparator is always visible

        // Total content width at rest inside DockPanel
        var iconSlots = pinnedCount + runningCount + trashCount;
        var restPanelWidth = iconSlots * MagnificationIconPitch + dockSepWidth + trashSepWidth;

        // Centers are relative to DockPanel's content origin (0,0)
        var centers = new double[totalIconCount];
        var offset = 0.0;
        var idx = 0;

        // Pinned icons
        for (int i = 0; i < pinnedCount && idx < totalIconCount; i++, idx++)
        {
            centers[idx] = offset + MagnificationIconPitch / 2.0;
            offset += MagnificationIconPitch;
        }

        // Dock separator
        offset += dockSepWidth;

        // Running icons
        for (int i = 0; i < runningCount && idx < totalIconCount; i++, idx++)
        {
            centers[idx] = offset + MagnificationIconPitch / 2.0;
            offset += MagnificationIconPitch;
        }

        // Trash separator
        offset += trashSepWidth;

        // Trash icon
        if (idx < totalIconCount)
        {
            centers[idx] = offset + MagnificationIconPitch / 2.0;
        }

        return (centers, restPanelWidth);
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

            ResetElementMagnification(grid);
        }
    }

    private static void ResetElementMagnification(FrameworkElement element)
    {
        // Reset layout-affecting Width (Height is never changed during magnification)
        element.BeginAnimation(FrameworkElement.WidthProperty, null);
        element.Width = IconDefaultSize;

        // Reset image RenderTransform (visual-only scaling used by magnification)
        var image = FindVisualChild<Image>(element);
        if (image is not null)
        {
            if (image.RenderTransform is ScaleTransform imgScale)
            {
                imgScale.ScaleX = 1.0;
                imgScale.ScaleY = 1.0;
            }
        }

        // Reset ScaleTransform (may have leftover state from hover/press animations)
        var scaleTransform = FindScaleTransform(element);
        if (scaleTransform is not null)
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scaleTransform.ScaleX = 1.0;
            scaleTransform.ScaleY = 1.0;
        }

        var translateTransform = FindTranslateTransform(element);
        if (translateTransform is not null)
        {
            translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            translateTransform.Y = 0;
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

        _autoHideService?.SuppressHide();
        _activeContextMenu = contextMenu;
        contextMenu.Closed += OnContextMenuClosed;

        contextMenu.IsOpen = true;

        // Install a low-level mouse hook to dismiss the menu on clicks outside.
        // WPF's built-in dismissal doesn't work because the dock has WS_EX_NOACTIVATE.
        _contextMenuMouseHook?.Dispose();
        _contextMenuMouseHook = new ContextMenuMouseHook(contextMenu, () =>
        {
            if (_activeContextMenu is { IsOpen: true })
                _activeContextMenu.IsOpen = false;
        });
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
                        IsCheckable = child.IsEnabled,
                        IsChecked = child.IsChecked,
                        IsEnabled = child.IsEnabled,
                    };
                    var childAction = child.Action;
                    childMenuItem.Click += (_, _) => HandleMenuAction(childAction, dockItem);
                    parent.Items.Add(childMenuItem);
                }
                menu.Items.Add(parent);
                continue;
            }

            var menuItem = new MenuItem { Header = item.Label, IsEnabled = item.IsEnabled };
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

    public void SetAppsLauncher(AppsLauncherWindow launcher)
    {
        _appsLauncher = launcher;
    }

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

        _autoHideService?.SuppressHide();
        _activeContextMenu = menu;
        menu.Closed += OnContextMenuClosed;

        menu.IsOpen = true;
        e.Handled = true;

        _contextMenuMouseHook?.Dispose();
        _contextMenuMouseHook = new ContextMenuMouseHook(menu, () =>
        {
            if (_activeContextMenu is { IsOpen: true })
                _activeContextMenu.IsOpen = false;
        });
    }

    private void TrashIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        // No scale animation for trash — it's a fixed dock element
    }

    private void TrashIcon_MouseLeave(object sender, MouseEventArgs e)
    {
    }

    #endregion

    #region Context Menu Lifecycle

    private void OnContextMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            menu.Closed -= OnContextMenuClosed;

        _contextMenuMouseHook?.Dispose();
        _contextMenuMouseHook = null;
        _activeContextMenu = null;
        _autoHideService?.ResumeHide();

        // If mouse is no longer over the dock, trigger hide
        if (_isAutoHideEnabled)
        {
            var pos = Mouse.GetPosition(DockContainer);
            var isOverDock = pos.X >= 0 && pos.X <= DockContainer.ActualWidth
                          && pos.Y >= 0 && pos.Y <= DockContainer.ActualHeight;
            if (!isOverDock)
                _autoHideService?.OnDockAreaLeave();
        }
    }

    #endregion

    #region Drag Reorder

    // Gap animation duration for icons sliding apart
    private static readonly Duration DragGapDuration = new(TimeSpan.FromMilliseconds(200));
    private const double DragGapWidth = 80.0; // full icon slot width for the gap
    private int _dragPrevTargetIndex = -1; // track to avoid redundant gap animations

    private void DockIcon_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var currentPos = e.GetPosition(DockPanel);
        var delta = currentPos - _dragStartPoint;

        // Movement threshold — if moved more than 5px, start drag (cancel long-press)
        if (!_isDragging && Math.Abs(delta.X) > 5)
        {
            if (_dragItem is null) return;

            CancelLongPress();
            _isDragging = true;
            _dragSourceWasPinned = _dragItem.IsPinned;
            _dragFromIndex = _dragSourceWasPinned ? GetPinnedIndex(_dragItem) : -1;
            _dragTargetIndex = _dragFromIndex;
            _dragPrevTargetIndex = -1;

            Mouse.Capture(element);

            // Create ghost popup — icon follows cursor at full size
            _dragGhost = new System.Windows.Controls.Primitives.Popup
            {
                AllowsTransparency = true,
                IsHitTestVisible = false,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Absolute,
                Child = new Border
                {
                    Opacity = 0.85,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 12,
                        ShadowDepth = 4,
                        Opacity = 0.4,
                    },
                    Child = new Image
                    {
                        Source = _dragItem.Icon,
                        Width = IconDefaultSize,
                        Height = IconDefaultSize,
                    }
                }
            };

            // Hide the original icon in place
            element.Opacity = 0.0;

            UpdateGhostPosition(e);
            _dragGhost.IsOpen = true;
        }

        if (_isDragging)
        {
            UpdateGhostPosition(e);
            UpdateDragTarget(e);
        }
    }

    /// <summary>
    /// Positions the ghost popup centered on the cursor using DIP screen coordinates.
    /// </summary>
    private void UpdateGhostPosition(MouseEventArgs e)
    {
        if (_dragGhost is null) return;

        // Get cursor position in screen DIPs via the dock window
        var cursorInWindow = e.GetPosition(this);
        var screenPoint = PointToScreen(cursorInWindow);

        // PointToScreen returns physical pixels — convert to DIPs for Popup.Absolute placement
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            var dpiX = source.CompositionTarget.TransformToDevice.M11;
            var dpiY = source.CompositionTarget.TransformToDevice.M22;
            screenPoint.X /= dpiX;
            screenPoint.Y /= dpiY;
        }

        _dragGhost.HorizontalOffset = screenPoint.X - IconDefaultSize / 2;
        _dragGhost.VerticalOffset = screenPoint.Y - IconDefaultSize / 2;
    }

    private void UpdateDragTarget(MouseEventArgs e)
    {
        if (_itemManager is null) return;

        var pos = e.GetPosition(PinnedIconsControl);
        var itemWidth = MagnificationIconPitch; // 80px — actual icon slot width (52 + 14+14 margin)
        var maxIndex = _dragSourceWasPinned
            ? _itemManager.PinnedItems.Count - 1
            : _itemManager.PinnedItems.Count;
        var newIndex = Math.Clamp((int)((pos.X + itemWidth / 2) / itemWidth), 0, Math.Max(0, maxIndex));
        _dragTargetIndex = newIndex;

        // Animate gap at the target position (macOS-style icons sliding apart)
        if (_dragTargetIndex != _dragPrevTargetIndex)
        {
            _dragPrevTargetIndex = _dragTargetIndex;
            AnimateDragGap();
        }
    }

    /// <summary>
    /// Animates pinned icons to slide apart at the drag target index, creating a visual gap.
    /// </summary>
    private void AnimateDragGap()
    {
        if (_itemManager is null) return;

        for (int i = 0; i < _itemManager.PinnedItems.Count; i++)
        {
            var container = PinnedIconsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;

            var grid = FindVisualChild<Grid>(container);
            if (grid is null) continue;

            var translateTransform = FindTranslateTransform(grid);
            if (translateTransform is null) continue;

            double targetX = 0;

            if (_dragSourceWasPinned)
            {
                // Dragging a pinned item — skip the dragged item, shift others to make gap
                if (i == _dragFromIndex) continue;

                if (_dragTargetIndex <= _dragFromIndex)
                {
                    // Moving left: items at [targetIndex, fromIndex) shift right
                    if (i >= _dragTargetIndex && i < _dragFromIndex)
                        targetX = DragGapWidth;
                }
                else
                {
                    // Moving right: items at (fromIndex, targetIndex] shift left
                    if (i > _dragFromIndex && i <= _dragTargetIndex)
                        targetX = -DragGapWidth;
                }
            }
            else
            {
                // Dragging a running item into pinned area — shift items at and after target right
                if (i >= _dragTargetIndex)
                    targetX = DragGapWidth;
            }

            var anim = new DoubleAnimation(targetX, DragGapDuration) { EasingFunction = EaseOut };
            translateTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }
    }

    /// <summary>
    /// Resets all gap animations on pinned icons back to zero offset.
    /// </summary>
    private void ResetDragGap()
    {
        if (_itemManager is null) return;

        for (int i = 0; i < _itemManager.PinnedItems.Count; i++)
        {
            var container = PinnedIconsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;

            var grid = FindVisualChild<Grid>(container);
            if (grid is null) continue;

            var translateTransform = FindTranslateTransform(grid);
            if (translateTransform is null) continue;

            translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            translateTransform.X = 0;
        }
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

        // Reset gap animations before reorder so the collection change doesn't fight them
        ResetDragGap();

        // Perform reorder or pin
        if (_itemManager is not null)
        {
            if (_dragSourceWasPinned)
            {
                // Reorder within pinned items
                if (_dragFromIndex != _dragTargetIndex)
                    _itemManager.PinningService.Reorder(_dragFromIndex, _dragTargetIndex);
            }
            else if (_dragItem is not null)
            {
                // Pin the running item at the target position
                _itemManager.PinningService.PinAt(_dragTargetIndex, _dragItem.ExecutablePath, _dragItem.DisplayName);
            }
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
        _dragPrevTargetIndex = -1;
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

        StopMagnificationTracking();

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
