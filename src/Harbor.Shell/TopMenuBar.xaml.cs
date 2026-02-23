using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
using ManagedShell.WindowsTray;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

public partial class TopMenuBar : AppBarWindow
{
    private ForegroundWindowService? _foregroundService;
    private GlobalMenuBarService? _globalMenuService;
    private ShellSettingsService? _shellSettings;
    private NotificationArea? _notificationArea;
    private DispatcherTimer? _clockTimer;
    private CalendarFlyout? _calendarFlyout;

    // Hover animation colors — updated on theme change
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly Color DarkHoverColor = Color.FromArgb(26, 255, 255, 255);   // #FFFFFF @ 10%
    private static readonly Color DarkPressedColor = Color.FromArgb(51, 255, 255, 255);  // #FFFFFF @ 20%
    private static readonly Color LightHoverColor = Color.FromArgb(20, 0, 0, 0);         // #000000 @ 8%
    private static readonly Color LightPressedColor = Color.FromArgb(41, 0, 0, 0);       // #000000 @ 16%

    // Acrylic color constants (AABBGGRR format for SetWindowCompositionAttribute)
    public const uint DarkAcrylicColor = 0xCC1E1E1E;  // #1E1E1E @ 80%
    public const uint LightAcrylicColor = 0xCCF6F6F6; // #F6F6F6 @ 80%

    // Solid fallback colors (fully opaque, no acrylic)
    public const uint DarkSolidColor = 0xFF1E1E1E;
    public const uint LightSolidColor = 0xFFF6F6F6;

    private Color _hoverColor = DarkHoverColor;
    private Color _pressedColor = DarkPressedColor;

    // Clock format string, rebuilt when settings change
    private string _clockFormat = "ddd MMM d  h:mm tt";

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
    public void Initialize(ForegroundWindowService foregroundService, NotificationArea notificationArea, GlobalMenuBarService globalMenuService)
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

        // Wire up global menu bar
        _globalMenuService = globalMenuService;
        _globalMenuService.MenuItemsChanged += OnGlobalMenuItemsChanged;

        // Query initial menu items
        if (_foregroundService.ActiveWindowHandle != 0)
            _globalMenuService.UpdateForWindow(_foregroundService.ActiveWindowHandle);

        StartClock();
    }

    /// <summary>
    /// Connects ShellSettingsService for configurable appearance options.
    /// Call after Initialize().
    /// </summary>
    public void ConnectSettings(ShellSettingsService shellSettings)
    {
        _shellSettings = shellSettings;
        _shellSettings.SettingsChanged += OnShellSettingsChanged;
        ApplySettingsToUI();
    }

    private void OnShellSettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(ApplySettingsToUI);
    }

    private void ApplySettingsToUI()
    {
        if (_shellSettings is null) return;

        // Update clock format
        RebuildClockFormat();
        UpdateClock();

        // Toggle menu items visibility
        MenuItemsControl.Visibility = _shellSettings.ShowAppMenuItems
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Toggle translucency
        ApplyTranslucency(ThemeService.ReadThemeFromRegistry());
    }

    private void RebuildClockFormat()
    {
        if (_shellSettings is null)
        {
            _clockFormat = "ddd MMM d  h:mm tt";
            return;
        }

        var parts = new List<string>();

        if (_shellSettings.ShowDayOfWeek)
            parts.Add("ddd");

        parts.Add("MMM d");

        string timePart;
        if (_shellSettings.Use24HourClock)
        {
            timePart = _shellSettings.ShowSeconds ? "HH:mm:ss" : "HH:mm";
        }
        else
        {
            timePart = _shellSettings.ShowSeconds ? "h:mm:ss tt" : "h:mm tt";
        }

        // Join date parts with space, then double-space before time
        _clockFormat = string.Join(" ", parts) + "  " + timePart;
    }

    /// <summary>
    /// Applies translucency or solid background based on settings.
    /// </summary>
    private void ApplyTranslucency(AppTheme theme)
    {
        if (_shellSettings?.MenuBarTranslucency == true)
        {
            ApplyThemedAcrylic(theme);
        }
        else
        {
            // Disable acrylic, use solid background
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var solidColor = theme == AppTheme.Light ? LightSolidColor : DarkSolidColor;
            CompositionInterop.EnableAcrylic(new HWND(hwnd), solidColor);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyAcrylic();
    }

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

        // Update hover/pressed colors for the new theme
        _hoverColor = theme == AppTheme.Light ? LightHoverColor : DarkHoverColor;
        _pressedColor = theme == AppTheme.Light ? LightPressedColor : DarkPressedColor;

        if (result)
            Trace.WriteLine($"[Harbor] TopMenuBar: Acrylic applied for {theme} theme.");
        else
            Trace.WriteLine("[Harbor] TopMenuBar: Acrylic failed, using solid fallback.");
    }

    private void OnForegroundChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ForegroundWindowService.ActiveAppName))
        {
            Dispatcher.Invoke(() =>
            {
                var name = _foregroundService?.ActiveAppName;
                AppNameText.Text = string.IsNullOrEmpty(name) ? "Harbor" : name;
            });
        }
        else if (e.PropertyName == nameof(ForegroundWindowService.ActiveWindowHandle))
        {
            var hwnd = _foregroundService?.ActiveWindowHandle ?? 0;
            _globalMenuService?.UpdateForWindow(hwnd);
        }
    }

    private void OnGlobalMenuItemsChanged(IReadOnlyList<GlobalMenuItem> items)
    {
        Dispatcher.Invoke(() => MenuItemsControl.ItemsSource = items);
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
        ClockText.Text = FormatClock(DateTime.Now, _clockFormat);
    }

    /// <summary>
    /// Formats a DateTime for the menu bar clock display.
    /// </summary>
    public static string FormatClock(DateTime time, string format = "ddd MMM d  h:mm tt")
    {
        return time.ToString(format, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Legacy overload for backward compatibility with tests.
    /// </summary>
    public static string FormatClock(DateTime time)
    {
        return FormatClock(time, "ddd MMM d  h:mm tt");
    }

    private void Clock_Click(object sender, MouseButtonEventArgs e)
    {
        if (_calendarFlyout is not null)
        {
            _calendarFlyout.Close();
            _calendarFlyout = null;
            return;
        }

        _calendarFlyout = new CalendarFlyout();
        _calendarFlyout.Closed += (_, _) => _calendarFlyout = null;

        // Position below the clock
        var clockScreenPos = ClockContainer.PointToScreen(new Point(0, ClockContainer.ActualHeight));
        _calendarFlyout.Left = clockScreenPos.X - 200 + ClockContainer.ActualWidth;
        _calendarFlyout.Top = clockScreenPos.Y + 4;

        _calendarFlyout.Show();
    }

    #endregion

    #region Hover / Press interaction states

    private void SetupInteractionStates()
    {
        var hoverTargets = new[] { AppNameItem, WindowsLogoItem };
        foreach (var item in hoverTargets)
        {
            item.Background = TransparentBrush.Clone();
            item.MouseEnter += OnMenuItemMouseEnter;
            item.MouseLeave += OnMenuItemMouseLeave;
            item.MouseLeftButtonDown += OnMenuItemMouseDown;
            item.MouseLeftButtonUp += OnMenuItemMouseUp;
        }
    }

    private void OnMenuItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var brush = element.GetValue(Border.BackgroundProperty) as SolidColorBrush;
        // Template items start with a shared Transparent brush — clone it so we can animate
        if (brush is null || brush.IsFrozen)
        {
            brush = TransparentBrush.Clone();
            element.SetValue(Border.BackgroundProperty, brush);
        }

        var animation = new ColorAnimation
        {
            To = _hoverColor,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void OnMenuItemMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        if (element.GetValue(Border.BackgroundProperty) is not SolidColorBrush brush || brush.IsFrozen)
            return;

        var animation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void OnMenuItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        if (element.GetValue(Border.BackgroundProperty) is not SolidColorBrush brush || brush.IsFrozen)
            return;

        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = _pressedColor;
    }

    private void OnMenuItemMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        if (element.GetValue(Border.BackgroundProperty) is not SolidColorBrush brush || brush.IsFrozen)
            return;

        var animation = new ColorAnimation
        {
            To = _hoverColor,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    #endregion

    #region Windows Logo System Menu

    private void WindowsLogo_Click(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();

        AddMenuItem(menu, $"About This PC", SystemActionService.OpenAboutThisPC);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "System Settings...", SystemActionService.OpenSystemSettings);
        AddMenuItem(menu, "App Store...", SystemActionService.OpenAppStore);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Lock Screen", SystemActionService.LockScreen);
        AddMenuItem(menu, $"Log Out {SystemActionService.GetCurrentUserName()}...", SystemActionService.LogOut);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Sleep", SystemActionService.Sleep);
        AddMenuItem(menu, "Restart...", SystemActionService.Restart);
        AddMenuItem(menu, "Shut Down...", SystemActionService.ShutDown);

        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static void AddMenuItem(ContextMenu menu, string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    #endregion

    #region Global Menu Item Interaction

    private void GlobalMenuItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: GlobalMenuItem item }) return;
        _globalMenuService?.ActivateMenuItem(item);
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

        _calendarFlyout?.Close();
        _calendarFlyout = null;

        TrayIconsControl.ItemsSource = null;
        MenuItemsControl.ItemsSource = null;
        _notificationArea = null;

        if (_globalMenuService != null)
            _globalMenuService.MenuItemsChanged -= OnGlobalMenuItemsChanged;
        _globalMenuService = null;

        if (_foregroundService != null)
            _foregroundService.PropertyChanged -= OnForegroundChanged;

        if (_shellSettings != null)
            _shellSettings.SettingsChanged -= OnShellSettingsChanged;
        _shellSettings = null;

        base.OnClosing(e);
    }
}
