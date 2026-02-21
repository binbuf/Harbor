using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.WindowsTray;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

public partial class TopMenuBar : AppBarWindow
{
    private ForegroundWindowService? _foregroundService;
    private NotificationArea? _notificationArea;
    private DispatcherTimer? _clockTimer;

    // Hover animation colors
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly Color HoverColor = Color.FromArgb(26, 255, 255, 255);   // 10% white
    private static readonly Color PressedColor = Color.FromArgb(51, 255, 255, 255);  // 20% white

    public TopMenuBar(
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
        SetupInteractionStates();
    }

    /// <summary>
    /// Initializes services after the window source is available.
    /// Called after Show() so we have an HWND for acrylic.
    /// </summary>
    public void Initialize(ForegroundWindowService foregroundService, NotificationArea notificationArea)
    {
        _foregroundService = foregroundService;
        _foregroundService.PropertyChanged += OnForegroundChanged;

        // Set initial app name
        AppNameText.Text = string.IsNullOrEmpty(_foregroundService.ActiveAppName)
            ? "Harbor"
            : _foregroundService.ActiveAppName;

        // Bind system tray icons
        _notificationArea = notificationArea;
        TrayIconsControl.ItemsSource = _notificationArea.TrayIcons;

        StartClock();
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

        // AABBGGRR format: 80% opacity (#CC), color #1E1E1E → BB=1E, GG=1E, RR=1E
        const uint acrylicColor = 0xCC1E1E1E;
        var result = CompositionInterop.EnableAcrylic(new HWND(hwnd), acrylicColor);

        if (result)
            Trace.WriteLine("[Harbor] TopMenuBar: Acrylic background applied.");
        else
            Trace.WriteLine("[Harbor] TopMenuBar: Acrylic failed, using solid fallback.");
    }

    private void OnForegroundChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ForegroundWindowService.ActiveAppName)) return;

        Dispatcher.Invoke(() =>
        {
            var name = _foregroundService?.ActiveAppName;
            AppNameText.Text = string.IsNullOrEmpty(name) ? "Harbor" : name;
        });
    }

    #region Clock

    private void StartClock()
    {
        UpdateClock();

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        Trace.WriteLine("[Harbor] TopMenuBar: Clock started.");
    }

    private void UpdateClock()
    {
        ClockText.Text = FormatClock(DateTime.Now);
    }

    /// <summary>
    /// Formats a DateTime for the menu bar clock display using Windows regional settings.
    /// </summary>
    public static string FormatClock(DateTime time)
    {
        return time.ToString("h:mm tt", CultureInfo.CurrentCulture);
    }

    #endregion

    #region Hover / Press interaction states

    private void SetupInteractionStates()
    {
        var hoverTargets = new[] { AppNameItem, FileMenuItem, EditMenuItem, ViewMenuItem };
        foreach (var item in hoverTargets)
        {
            item.Background = TransparentBrush.Clone();
            item.MouseEnter += OnMenuItemMouseEnter;
            item.MouseLeave += OnMenuItemMouseLeave;
            item.MouseLeftButtonDown += OnMenuItemMouseDown;
            item.MouseLeftButtonUp += OnMenuItemMouseUp;
        }
    }

    private static void OnMenuItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var brush = (SolidColorBrush)element.GetValue(System.Windows.Controls.Border.BackgroundProperty);
        var animation = new ColorAnimation
        {
            To = HoverColor,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static void OnMenuItemMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var brush = (SolidColorBrush)element.GetValue(System.Windows.Controls.Border.BackgroundProperty);
        var animation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private static void OnMenuItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var brush = (SolidColorBrush)element.GetValue(System.Windows.Controls.Border.BackgroundProperty);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = PressedColor;
    }

    private static void OnMenuItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var brush = (SolidColorBrush)element.GetValue(System.Windows.Controls.Border.BackgroundProperty);
        var animation = new ColorAnimation
        {
            To = HoverColor,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    #endregion

    #region System Tray Icon Interaction

    private static uint GetPackedMousePosition(MouseEventArgs e, FrameworkElement relativeTo)
    {
        var pos = relativeTo.PointToScreen(e.GetPosition(relativeTo));
        var x = (ushort)pos.X;
        var y = (ushort)pos.Y;
        return (uint)((y << 16) | x);
    }

    private static NotifyIcon? GetNotifyIcon(object sender)
    {
        return (sender as FrameworkElement)?.DataContext as NotifyIcon;
    }

    private void TrayIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var icon = GetNotifyIcon(sender);
        if (icon == null) return;

        var mouse = GetPackedMousePosition(e, (FrameworkElement)sender);
        var doubleClickTime = SystemInterop.GetDoubleClickTime();
        icon.IconMouseUp(MouseButton.Left, mouse, (int)doubleClickTime);

        Trace.WriteLine($"[Harbor] TopMenuBar: Tray icon left-clicked: {icon.Title}");
    }

    private void TrayIcon_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var icon = GetNotifyIcon(sender);
        if (icon == null) return;

        var mouse = GetPackedMousePosition(e, (FrameworkElement)sender);
        var doubleClickTime = SystemInterop.GetDoubleClickTime();
        icon.IconMouseUp(MouseButton.Right, mouse, (int)doubleClickTime);

        Trace.WriteLine($"[Harbor] TopMenuBar: Tray icon right-clicked: {icon.Title}");
    }

    private void TrayIcon_MouseEnter(object sender, MouseEventArgs e)
    {
        var icon = GetNotifyIcon(sender);
        if (icon == null) return;

        var mouse = GetPackedMousePosition(e, (FrameworkElement)sender);
        icon.IconMouseEnter(mouse);
    }

    private void TrayIcon_MouseLeave(object sender, MouseEventArgs e)
    {
        var icon = GetNotifyIcon(sender);
        if (icon == null) return;

        var mouse = GetPackedMousePosition(e, (FrameworkElement)sender);
        icon.IconMouseLeave(mouse);
    }

    #endregion

    protected override void OnClosing(CancelEventArgs e)
    {
        _clockTimer?.Stop();
        _clockTimer = null;

        TrayIconsControl.ItemsSource = null;
        _notificationArea = null;

        if (_foregroundService != null)
            _foregroundService.PropertyChanged -= OnForegroundChanged;

        base.OnClosing(e);
    }
}
