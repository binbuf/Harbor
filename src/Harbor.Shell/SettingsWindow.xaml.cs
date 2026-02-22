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

    private SettingsWindow(DockSettingsService dockSettings, ShellSettingsService shellSettings)
    {
        _dockSettings = dockSettings;
        _shellSettings = shellSettings;
        InitializeComponent();
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

        AutoHideModeCombo.SelectedIndex = (int)_dockSettings.AutoHideMode;
        IconSizeSlider.Value = _dockSettings.IconSize;
        IconSizeLabel.Text = $"Icon size: {_dockSettings.IconSize} px";
        MagnificationCheckBox.IsChecked = _dockSettings.MagnificationEnabled;
        ReplaceExplorerCheckBox.IsChecked = _shellSettings.ReplaceExplorer;

        _loading = false;
    }

    private void AutoHideModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _dockSettings.AutoHideMode = (DockAutoHideMode)AutoHideModeCombo.SelectedIndex;
    }

    private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var size = (int)IconSizeSlider.Value;
        _dockSettings.IconSize = size;
        IconSizeLabel.Text = $"Icon size: {size} px";
    }

    private void MagnificationCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _dockSettings.MagnificationEnabled = MagnificationCheckBox.IsChecked == true;
    }

    private void ReplaceExplorerCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _shellSettings.ReplaceExplorer = ReplaceExplorerCheckBox.IsChecked == true;
    }
}
