using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Harbor.Core.Interop;
using Harbor.Core.Models;
using Harbor.Core.Services;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

public partial class AppsLauncherWindow : Window
{
    private readonly InstalledAppService _appService;
    private readonly ObservableCollection<AppInfo> _filteredApps = [];
    private bool _isVisible;
    private string _searchQuery = string.Empty;
    private DateTime _lastDeactivateHide = DateTime.MinValue;

    // Animation constants
    private static readonly Duration FadeInDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration FadeOutDuration = new(TimeSpan.FromMilliseconds(150));
    private static readonly IEasingFunction EaseOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };

    public AppsLauncherWindow(InstalledAppService appService)
    {
        InitializeComponent();

        _appService = appService;
        AppsGrid.ItemsSource = _filteredApps;

        _appService.AppsChanged += OnAppsChanged;
        Deactivated += OnDeactivated;

        // Initial populate
        RefreshFilteredApps();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new HWND(new WindowInteropHelper(this).Handle);

        // Add WS_EX_TOOLWINDOW to hide from Alt+Tab
        var exStyle = WindowInterop.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (nint)WindowInterop.WS_EX_TOOLWINDOW;
        WindowInterop.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);

        // Position to cover the work area (between menu bar and dock)
        PositionWindow();

        // Start hidden
        Opacity = 0;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Positions the window to fill the primary screen work area.
    /// </summary>
    private void PositionWindow()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
    }

    /// <summary>
    /// Toggles the launcher visibility with animation.
    /// </summary>
    public void Toggle()
    {
        if (_isVisible)
        {
            HideWithAnimation();
        }
        else
        {
            // If we just hid due to deactivation (e.g. dock icon click), don't re-show
            if ((DateTime.UtcNow - _lastDeactivateHide).TotalMilliseconds < 300)
                return;
            ShowWithAnimation();
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_isVisible) return;
        _lastDeactivateHide = DateTime.UtcNow;
        HideWithAnimation();
    }

    private void ShowWithAnimation()
    {
        _isVisible = true;
        PositionWindow();

        // Reset search
        SearchBox.Text = string.Empty;
        _searchQuery = string.Empty;
        RefreshFilteredApps();

        Visibility = Visibility.Visible;

        // Fade in + scale up
        var fadeIn = new DoubleAnimation(0, 1, FadeInDuration) { EasingFunction = EaseOut };
        var scaleIn = new DoubleAnimation(0.95, 1.0, FadeInDuration) { EasingFunction = EaseOut };

        fadeIn.Completed += (_, _) =>
        {
            // Focus search box after animation
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        };

        BeginAnimation(OpacityProperty, fadeIn);
        ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleIn);
        ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleIn);
    }

    private void HideWithAnimation()
    {
        _isVisible = false;

        var fadeOut = new DoubleAnimation(1, 0, FadeOutDuration) { EasingFunction = EaseIn };
        var scaleOut = new DoubleAnimation(1.0, 0.95, FadeOutDuration) { EasingFunction = EaseIn };

        fadeOut.Completed += (_, _) =>
        {
            Visibility = Visibility.Collapsed;
        };

        BeginAnimation(OpacityProperty, fadeOut);
        ContentScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleOut);
        ContentScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleOut);
    }

    private void OnAppsChanged()
    {
        Dispatcher.Invoke(RefreshFilteredApps);
    }

    private void RefreshFilteredApps()
    {
        _filteredApps.Clear();

        var query = _searchQuery;
        var source = _appService.Apps;

        foreach (var app in source)
        {
            if (string.IsNullOrEmpty(query) ||
                app.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _filteredApps.Add(app);
            }
        }
    }

    #region Event Handlers

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchQuery)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshFilteredApps();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideWithAnimation();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Click on the transparent outer area dismisses the launcher.
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only dismiss if clicking outside the content panel
        HideWithAnimation();
    }

    /// <summary>
    /// Prevent clicks on the content panel from dismissing.
    /// </summary>
    private void ContentPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void AppItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not AppInfo app) return;

        LaunchApp(app);
        HideWithAnimation();
    }

    private void AppItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    }

    private void AppItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = Brushes.Transparent;
    }

    #endregion

    #region App Launching

    private static void LaunchApp(AppInfo app)
    {
        try
        {
            Trace.WriteLine($"[Harbor] AppsLauncher: Launching {app.DisplayName} ({app.ExecutablePath})");

            Process.Start(new ProcessStartInfo
            {
                FileName = app.ExecutablePath,
                Arguments = app.LaunchArguments ?? string.Empty,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] AppsLauncher: Failed to launch {app.DisplayName}: {ex.Message}");
        }
    }

    #endregion

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Deactivated -= OnDeactivated;
        _appService.AppsChanged -= OnAppsChanged;
        base.OnClosing(e);
    }
}
