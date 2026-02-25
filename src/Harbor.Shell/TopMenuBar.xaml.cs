using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using Harbor.Shell.Flyouts;
using ManagedShell.WindowsTray;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

public partial class TopMenuBar : AppBarWindow
{
    private ForegroundWindowService? _foregroundService;
    private GlobalMenuBarService? _globalMenuService;
    private ShellSettingsService? _shellSettings;
    private NotificationArea? _notificationArea;
    private DispatcherTimer? _clockTimer;
    private CalendarFlyout? _calendarFlyout;

    // Volume
    private VolumeService? _volumeService;
    private VolumeFlyout? _volumeFlyout;

    // Icon geometries (loaded from resource dictionary)
    private static ResourceDictionary? _indicatorIcons;

    // Auto-hide
    private MenuBarAutoHideService? _autoHideService;
    private bool _isAutoHideEnabled;

    // Dynamic color
    private WallpaperBrightnessService? _brightnessService;
    private bool _dynamicColorOverrideIsLight;

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

    // Auto-hide animation durations
    private static readonly Duration ShowAnimationDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration HideAnimationDuration = new(TimeSpan.FromMilliseconds(150));
    private static readonly IEasingFunction ShowEasing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction HideEasing = new QuadraticEase { EasingMode = EasingMode.EaseIn };

    // Tracks original tray icon sources so we can restore them when monochrome is toggled off
    private readonly Dictionary<System.Windows.Controls.Image, ImageSource> _originalTrayIconSources = new();

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
    public void Initialize(ForegroundWindowService foregroundService, NotificationArea notificationArea, GlobalMenuBarService globalMenuService, TrayIconFilterService? trayIconFilter = null)
    {
        _foregroundService = foregroundService;
        _foregroundService.PropertyChanged += OnForegroundChanged;

        // Set initial app name
        AppNameText.Text = string.IsNullOrEmpty(_foregroundService.ActiveAppName)
            ? "Harbor"
            : _foregroundService.ActiveAppName;

        // Bind system tray icons (use filtered view if available)
        _notificationArea = notificationArea;
        if (trayIconFilter is not null)
            TrayIconsControl.ItemsSource = trayIconFilter.FilteredTrayIcons;
        else
            TrayIconsControl.ItemsSource = _notificationArea.TrayIcons;

        // Wire up global menu bar
        _globalMenuService = globalMenuService;
        _globalMenuService.MenuItemsChanged += OnGlobalMenuItemsChanged;

        // Query initial menu items
        if (_foregroundService.ActiveWindowHandle != 0)
            _globalMenuService.UpdateForWindow(_foregroundService.ActiveWindowHandle);

        // Initialize auto-hide service
        _autoHideService = new MenuBarAutoHideService();
        _autoHideService.ShowRequested += OnAutoHideShowRequested;
        _autoHideService.HideRequested += OnAutoHideHideRequested;

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

    /// <summary>
    /// Connects the volume service and wires up the volume indicator icon.
    /// </summary>
    public void ConnectVolumeService(VolumeService volumeService)
    {
        _volumeService = volumeService;
        _volumeService.VolumeChanged += OnVolumeServiceChanged;

        // Load icon geometries
        _indicatorIcons ??= new ResourceDictionary
        {
            Source = new Uri("Resources/SystemIndicatorIcons.xaml", UriKind.Relative),
        };

        // Set initial icon state
        UpdateVolumeIcon(_volumeService.IconState);

        // Wire click handler
        VolumeIcon.Clicked += OnVolumeIconClicked;

        Trace.WriteLine("[Harbor] TopMenuBar: Volume service connected.");
    }

    private void OnVolumeServiceChanged(object? sender, VolumeChangedEventArgs e)
    {
        Dispatcher.Invoke(() => UpdateVolumeIcon(e.IconState));
    }

    private void UpdateVolumeIcon(VolumeIconState state)
    {
        if (_indicatorIcons is null) return;

        var key = state switch
        {
            VolumeIconState.Muted => "VolumeMutedIcon",
            VolumeIconState.Low => "VolumeLowIcon",
            VolumeIconState.Medium => "VolumeMediumIcon",
            VolumeIconState.High => "VolumeHighIcon",
            _ => "VolumeHighIcon",
        };

        if (_indicatorIcons[key] is System.Windows.Media.Geometry geometry)
            VolumeIcon.IconData = geometry;
    }

    private void OnVolumeIconClicked(object? sender, EventArgs e)
    {
        if (_volumeService is null) return;

        if (_volumeFlyout is not null)
        {
            _volumeFlyout.Close();
            _volumeFlyout = null;
            return;
        }

        _volumeFlyout = new VolumeFlyout(_volumeService);
        _volumeFlyout.Closed += (_, _) => _volumeFlyout = null;

        // Position below the volume icon, converting physical pixels to DIPs
        var iconScreenPos = VolumeIcon.PointToScreen(new Point(0, VolumeIcon.ActualHeight));
        var dpi = GetDpiScale();
        _volumeFlyout.Left = iconScreenPos.X / dpi - 120 + VolumeIcon.ActualWidth / 2;
        _volumeFlyout.Top = iconScreenPos.Y / dpi + 4;

        _volumeFlyout.Show();
    }

    /// <summary>
    /// Connects the wallpaper brightness service for dynamic menu bar color.
    /// </summary>
    public void ConnectBrightnessService(WallpaperBrightnessService brightnessService)
    {
        _brightnessService = brightnessService;
        _brightnessService.BrightnessChanged += OnBrightnessChanged;
        _dynamicColorOverrideIsLight = _brightnessService.IsLightBackground;
        ApplyDynamicColor();
    }

    private void OnBrightnessChanged(bool isLight)
    {
        Dispatcher.Invoke(() =>
        {
            _dynamicColorOverrideIsLight = isLight;
            ApplyDynamicColor();
        });
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

        // Apply auto-hide mode
        ApplyAutoHideMode(_shellSettings.AutoHideMenuBar);

        // Apply monochrome tray icons
        ApplyMonochromeTrayIcons();

        // Apply dynamic color
        ApplyDynamicColor();
    }

    #region Auto-Hide

    private void ApplyAutoHideMode(bool enabled)
    {
        _isAutoHideEnabled = enabled;
        AutoHideTriggerZone.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (!enabled && _autoHideService?.State == MenuBarAutoHideService.AutoHideState.Hidden)
        {
            // Restore menu bar if it was hidden
            MenuBarSlideTransform.Y = 0;
            MenuBarContainer.Visibility = Visibility.Visible;
        }

        if (enabled)
        {
            _autoHideService?.ForceHidden();
            MenuBarSlideTransform.Y = -24; // slide up (negative Y = off-screen top)
            MenuBarContainer.Visibility = Visibility.Collapsed;
        }
    }

    private void TriggerZone_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_isAutoHideEnabled)
            _autoHideService?.OnTriggerZoneEnter();
    }

    private void MenuBarContainer_MouseEnter(object sender, MouseEventArgs e)
    {
        _autoHideService?.OnMenuBarEnter();
    }

    private void MenuBarContainer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isAutoHideEnabled)
            _autoHideService?.OnMenuBarLeave();
    }

    private void OnAutoHideShowRequested()
    {
        Dispatcher.Invoke(() =>
        {
            MenuBarContainer.Visibility = Visibility.Visible;
            var slideDown = new DoubleAnimation(-24, 0, ShowAnimationDuration)
            {
                EasingFunction = ShowEasing,
            };
            slideDown.Completed += (_, _) => _autoHideService?.OnShowAnimationCompleted();
            MenuBarSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);
        });
    }

    private void OnAutoHideHideRequested()
    {
        Dispatcher.Invoke(() =>
        {
            var slideUp = new DoubleAnimation(0, -24, HideAnimationDuration)
            {
                EasingFunction = HideEasing,
            };
            slideUp.Completed += (_, _) =>
            {
                MenuBarContainer.Visibility = Visibility.Collapsed;
                _autoHideService?.OnHideAnimationCompleted();
            };
            MenuBarSlideTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        });
    }

    #endregion

    #region Monochrome Tray Icons

    private void ApplyMonochromeTrayIcons()
    {
        if (_shellSettings is null) return;

        for (int i = 0; i < TrayIconsControl.Items.Count; i++)
        {
            var container = TrayIconsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container is null) continue;

            var image = FindVisualChild<System.Windows.Controls.Image>(container);
            if (image is null) continue;

            if (_shellSettings.MonochromeTrayIcons)
            {
                // Save original source if not already tracked
                if (!_originalTrayIconSources.ContainsKey(image) && image.Source is not null)
                    _originalTrayIconSources[image] = image.Source;

                // Convert to grayscale via FormatConvertedBitmap
                var original = _originalTrayIconSources.GetValueOrDefault(image) ?? image.Source;
                if (original is BitmapSource bitmapSource)
                {
                    try
                    {
                        var grayscale = new FormatConvertedBitmap(bitmapSource, PixelFormats.Gray8, null, 0);
                        // Convert back to Pbgra32 so WPF can render it with proper alpha
                        var colorGrayscale = new FormatConvertedBitmap(grayscale, PixelFormats.Pbgra32, null, 0);
                        colorGrayscale.Freeze();
                        image.Source = colorGrayscale;
                    }
                    catch
                    {
                        // If conversion fails, leave the original image
                    }
                }
            }
            else
            {
                // Restore original source
                if (_originalTrayIconSources.TryGetValue(image, out var original))
                {
                    image.Source = original;
                    _originalTrayIconSources.Remove(image);
                }
            }
        }
    }

    #endregion

    #region Dynamic Color

    /// <summary>
    /// Applies menu bar text color based on the MenuBarTextColor setting:
    /// "white" = forced white text, "black" = forced black text,
    /// "auto" = wallpaper-brightness-based dynamic color.
    /// </summary>
    private void ApplyDynamicColor()
    {
        if (_shellSettings is null) return;

        var mode = _shellSettings.MenuBarTextColor;

        if (mode == "auto" && _brightnessService is not null)
        {
            // Auto mode: pick color based on wallpaper brightness
            var textColor = _dynamicColorOverrideIsLight ? Colors.Black : Colors.White;
            var textBrush = new SolidColorBrush(textColor);
            textBrush.Freeze();

            AppNameText.Foreground = textBrush;
            ClockText.Foreground = textBrush;

            _hoverColor = _dynamicColorOverrideIsLight ? LightHoverColor : DarkHoverColor;
            _pressedColor = _dynamicColorOverrideIsLight ? LightPressedColor : DarkPressedColor;

            // Tint the acrylic with the wallpaper's dominant color when translucency is enabled
            if (_shellSettings.MenuBarOpacity < 1.0)
            {
                var wallColor = _brightnessService.DominantColor;
                uint acrylicTint = ((uint)0x99 << 24)
                    | ((uint)wallColor.B << 16)
                    | ((uint)wallColor.G << 8)
                    | wallColor.R;

                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                    CompositionInterop.EnableAcrylic(new HWND(hwnd), acrylicTint);
            }

            Trace.WriteLine($"[Harbor] TopMenuBar: Auto color applied (isLight={_dynamicColorOverrideIsLight})");
        }
        else
        {
            // White or Black explicit mode
            var isBlack = mode == "black";
            var textColor = isBlack ? Colors.Black : Colors.White;
            var textBrush = new SolidColorBrush(textColor);
            textBrush.Freeze();

            AppNameText.Foreground = textBrush;
            ClockText.Foreground = textBrush;

            _hoverColor = isBlack ? LightHoverColor : DarkHoverColor;
            _pressedColor = isBlack ? LightPressedColor : DarkPressedColor;

            // Restore standard acrylic with the user's opacity setting
            ApplyTranslucency(ThemeService.ReadThemeFromRegistry());

            Trace.WriteLine($"[Harbor] TopMenuBar: Explicit color applied (mode={mode})");
        }
    }

    #endregion

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
        var opacity = _shellSettings?.MenuBarOpacity ?? 0.8;

        if (opacity < 1.0)
        {
            // Build acrylic color with alpha derived from opacity setting
            var alpha = (byte)(opacity * 255);
            var baseColor = theme == AppTheme.Light ? LightAcrylicColor : DarkAcrylicColor;
            // Replace alpha byte (top 8 bits) with the slider value
            var acrylicColor = (baseColor & 0x00FFFFFF) | ((uint)alpha << 24);

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                CompositionInterop.EnableAcrylic(new HWND(hwnd), acrylicColor);

            // Update hover/pressed colors for the theme
            _hoverColor = theme == AppTheme.Light ? LightHoverColor : DarkHoverColor;
            _pressedColor = theme == AppTheme.Light ? LightPressedColor : DarkPressedColor;

            // Let the DWM acrylic show through — don't paint over it
            BackgroundBorder.Background = Brushes.Transparent;
            BackgroundBorder.BorderBrush = Brushes.Transparent;
        }
        else
        {
            // Disable acrylic, use solid background
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var solidColor = theme == AppTheme.Light ? LightSolidColor : DarkSolidColor;
            CompositionInterop.EnableAcrylic(new HWND(hwnd), solidColor);

            // Restore theme brushes on the BackgroundBorder for solid mode
            BackgroundBorder.SetResourceReference(Border.BackgroundProperty, "MenuBarBackground");
            BackgroundBorder.SetResourceReference(Border.BorderBrushProperty, "MenuBarBorderBrush");
        }
    }

    // WndProc constants for Z-order and minimize protection
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private static readonly int WposInsertAfterOffset = IntPtr.Size;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;

        // Add WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE to prevent Alt+Tab entry and
        // ensure the menu bar is excluded from "show desktop" minimization
        var exStyle = WindowInterop.GetWindowLongPtr(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (nint)(WindowInterop.WS_EX_TOOLWINDOW | WindowInterop.WS_EX_NOACTIVATE);
        WindowInterop.SetWindowLongPtr(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);

        // Hook WndProc to enforce TOPMOST and block minimization
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(MenuBarWndProc);

        ApplyAcrylic();
    }

    private IntPtr MenuBarWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_WINDOWPOSCHANGING:
                // Force HWND_TOPMOST so nothing can push us down the Z-order
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
        {
            // Let the DWM acrylic show through
            BackgroundBorder.Background = Brushes.Transparent;
            BackgroundBorder.BorderBrush = Brushes.Transparent;
            Trace.WriteLine($"[Harbor] TopMenuBar: Acrylic applied for {theme} theme.");
        }
        else
        {
            Trace.WriteLine("[Harbor] TopMenuBar: Acrylic failed, using solid fallback.");
        }
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
        Dispatcher.Invoke(() =>
        {
            MenuItemsControl.ItemsSource = items;

            // Re-apply monochrome effect when tray icons change
            if (_shellSettings?.MonochromeTrayIcons == true)
            {
                // Defer to allow ItemContainerGenerator to create containers
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyMonochromeTrayIcons);
            }
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

        // Position below the clock, converting physical pixels to DIPs
        var clockScreenPos = ClockContainer.PointToScreen(new Point(0, ClockContainer.ActualHeight));
        var dpi = GetDpiScale();
        _calendarFlyout.Left = clockScreenPos.X / dpi - 200 + ClockContainer.ActualWidth;
        _calendarFlyout.Top = clockScreenPos.Y / dpi + 4;

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

    #region Helpers

    /// <summary>
    /// Returns the DPI scale factor for this window (physical pixels per DIP).
    /// PointToScreen returns physical pixels; Window.Left/Top expect DIPs.
    /// </summary>
    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
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

    #endregion

    protected override void OnClosing(CancelEventArgs e)
    {
        _clockTimer?.Stop();
        _clockTimer = null;

        _calendarFlyout?.Close();
        _calendarFlyout = null;

        _volumeFlyout?.Close();
        _volumeFlyout = null;

        if (_volumeService is not null)
        {
            _volumeService.VolumeChanged -= OnVolumeServiceChanged;
            _volumeService = null;
        }
        VolumeIcon.Clicked -= OnVolumeIconClicked;

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

        if (_autoHideService != null)
        {
            _autoHideService.ShowRequested -= OnAutoHideShowRequested;
            _autoHideService.HideRequested -= OnAutoHideHideRequested;
            _autoHideService.Dispose();
            _autoHideService = null;
        }

        if (_brightnessService != null)
        {
            _brightnessService.BrightnessChanged -= OnBrightnessChanged;
            _brightnessService = null;
        }

        base.OnClosing(e);
    }
}
