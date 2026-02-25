using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Harbor.Core.Services;

namespace Harbor.Shell;

public partial class SettingsWindow : Window
{
    private readonly DockSettingsService _dockSettings;
    private readonly ShellSettingsService _shellSettings;
    private bool _loading;

    private static SettingsWindow? _instance;

    // Map sidebar index to panel
    private readonly ScrollViewer[] _panels;

    private SettingsWindow(DockSettingsService dockSettings, ShellSettingsService shellSettings)
    {
        _dockSettings = dockSettings;
        _shellSettings = shellSettings;
        InitializeComponent();

        _panels = [GeneralPanel, AppearancePanel, DockPanel, MenuBarPanel, DesktopPanel, AboutPanel];

        LoadCurrentSettings();
    }

    /// <summary>
    /// Shows the singleton settings window. If already open, activates it.
    /// </summary>
    public static void ShowSingleton(DockSettingsService dockSettings, ShellSettingsService shellSettings)
    {
        if (_instance is not null)
        {
            _instance.Activate();
            return;
        }

        _instance = new SettingsWindow(dockSettings, shellSettings);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    private void LoadCurrentSettings()
    {
        _loading = true;

        // General
        ReplaceExplorerToggle.IsChecked = _shellSettings.ReplaceExplorer;
        FilterAppsFolderToggle.IsChecked = _shellSettings.FilterAppsFolder;

        // Appearance
        ThemeCombo.SelectedIndex = _shellSettings.ThemeOverride switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0, // auto
        };

        // Dock
        AutoHideDockToggle.IsChecked = _dockSettings.AutoHideMode == DockAutoHideMode.Always;
        IconSizeSlider.Value = _dockSettings.IconSize;
        IconSizeLabel.Text = $"Icon size: {_dockSettings.IconSize} px";
        MagnificationToggle.IsChecked = _dockSettings.MagnificationEnabled;
        AnimateOpeningAppsToggle.IsChecked = _shellSettings.AnimateOpeningApps;
        ShowRecentAppsToggle.IsChecked = _shellSettings.ShowRecentApps;

        // Menu Bar
        ShowAppMenuItemsToggle.IsChecked = _shellSettings.ShowAppMenuItems;
        AutoHideMenuBarToggle.IsChecked = _shellSettings.AutoHideMenuBar;
        MenuBarOpacitySlider.Value = _shellSettings.MenuBarOpacity * 100;
        MenuBarOpacityLabel.Text = $"Menu bar opacity: {(int)(_shellSettings.MenuBarOpacity * 100)}%";
        MenuBarTextColorCombo.SelectedIndex = _shellSettings.MenuBarTextColor switch
        {
            "black" => 1,
            "auto" => 2,
            _ => 0, // white
        };
        MonochromeTrayIconsToggle.IsChecked = _shellSettings.MonochromeTrayIcons;
        ShowDayOfWeekToggle.IsChecked = _shellSettings.ShowDayOfWeek;
        Use24HourClockToggle.IsChecked = _shellSettings.Use24HourClock;
        ShowSecondsToggle.IsChecked = _shellSettings.ShowSeconds;

        // Desktop
        ShowDesktopIconsToggle.IsChecked = _shellSettings.ShowDesktopIcons;
        HideRecycleBinToggle.IsChecked = _shellSettings.HideRecycleBin;

        // About
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "0.0.0"}";
        DotNetVersionText.Text = $".NET {RuntimeInformation.FrameworkDescription}";
        SystemInfoText.Text = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

        _loading = false;
    }

    #region Category Navigation

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_panels is null) return;

        var index = CategoryList.SelectedIndex;
        for (int i = 0; i < _panels.Length; i++)
        {
            _panels[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    #endregion

    #region General

    private void ReplaceExplorerToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ReplaceExplorer = ReplaceExplorerToggle.IsChecked == true;
    }

    private void FilterAppsFolderToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.FilterAppsFolder = FilterAppsFolderToggle.IsChecked == true;
    }

    #endregion

    #region Appearance

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ThemeOverride = ThemeCombo.SelectedIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "auto",
        };
    }

    #endregion

    #region Dock

    private void AutoHideDockToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dockSettings.AutoHideMode = AutoHideDockToggle.IsChecked == true
            ? DockAutoHideMode.Always
            : DockAutoHideMode.Never;
    }

    private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var size = (int)IconSizeSlider.Value;
        _dockSettings.IconSize = size;
        if (IconSizeLabel is not null)
            IconSizeLabel.Text = $"Icon size: {size} px";
    }

    private void MagnificationToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dockSettings.MagnificationEnabled = MagnificationToggle.IsChecked == true;
    }

    private void AnimateOpeningAppsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.AnimateOpeningApps = AnimateOpeningAppsToggle.IsChecked == true;
    }

    private void ShowRecentAppsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ShowRecentApps = ShowRecentAppsToggle.IsChecked == true;
    }

    #endregion

    #region Menu Bar

    private void ShowAppMenuItemsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ShowAppMenuItems = ShowAppMenuItemsToggle.IsChecked == true;
    }

    private void AutoHideMenuBarToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.AutoHideMenuBar = AutoHideMenuBarToggle.IsChecked == true;
    }

    private void MenuBarOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var pct = (int)MenuBarOpacitySlider.Value;
        _shellSettings.MenuBarOpacity = pct / 100.0;
        if (MenuBarOpacityLabel is not null)
            MenuBarOpacityLabel.Text = $"Menu bar opacity: {pct}%";
    }

    private void MenuBarTextColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.MenuBarTextColor = MenuBarTextColorCombo.SelectedIndex switch
        {
            1 => "black",
            2 => "auto",
            _ => "white",
        };
    }

    private void MonochromeTrayIconsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.MonochromeTrayIcons = MonochromeTrayIconsToggle.IsChecked == true;
    }

    private void ShowDayOfWeekToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ShowDayOfWeek = ShowDayOfWeekToggle.IsChecked == true;
    }

    private void Use24HourClockToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.Use24HourClock = Use24HourClockToggle.IsChecked == true;
    }

    private void ShowSecondsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ShowSeconds = ShowSecondsToggle.IsChecked == true;
    }

    #endregion

    #region Desktop

    private void ShowDesktopIconsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ShowDesktopIcons = ShowDesktopIconsToggle.IsChecked == true;
    }

    private void HideRecycleBinToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.HideRecycleBin = HideRecycleBinToggle.IsChecked == true;
    }

    #endregion
}
