using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Harbor.Core.Services;

namespace Harbor.Shell.Flyouts;

public partial class VolumeFlyout : Window
{
    private readonly VolumeService _volumeService;
    private bool _suppressSliderEvent;

    public VolumeFlyout(VolumeService volumeService)
    {
        InitializeComponent();

        _volumeService = volumeService;

        // Set initial state
        _suppressSliderEvent = true;
        VolumeSlider.Value = _volumeService.VolumePercent;
        _suppressSliderEvent = false;
        VolumePercentText.Text = $"{_volumeService.VolumePercent}%";

        // Populate device list
        DeviceList.ItemsSource = _volumeService.OutputDevices;

        // Subscribe to live changes
        _volumeService.VolumeChanged += OnVolumeChanged;
        _volumeService.DevicesChanged += OnDevicesChanged;
    }

    private void OnVolumeChanged(object? sender, VolumeChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _suppressSliderEvent = true;
            VolumeSlider.Value = e.VolumePercent;
            _suppressSliderEvent = false;
            VolumePercentText.Text = $"{e.VolumePercent}%";
        });
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            DeviceList.ItemsSource = _volumeService.OutputDevices;
        });
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderEvent) return;

        var percent = (int)e.NewValue;
        _volumeService.SetVolume(percent);
        VolumePercentText.Text = $"{percent}%";
    }

    private void DeviceRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AudioOutputDevice device })
        {
            _volumeService.SetActiveDevice(device.Id);
            DeviceList.ItemsSource = _volumeService.OutputDevices;
        }
    }

    private void DeviceRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)FindResource("FlyoutItemHoverBackground");
        }
    }

    private void DeviceRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
        }
    }

    private void SoundSettings_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] VolumeFlyout: Failed to open Sound Settings: {ex.Message}");
        }
        Close();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _volumeService.VolumeChanged -= OnVolumeChanged;
        _volumeService.DevicesChanged -= OnDevicesChanged;
        base.OnClosed(e);
    }
}
