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
    private FlyoutMouseHook? _mouseHook;
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

        // Clamp position to monitor bounds once layout is known
        ContentRendered += OnContentRendered;

        // Install global mouse hook to dismiss on click-outside
        Loaded += (_, _) => _mouseHook = new FlyoutMouseHook(this, Close);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        ClampToMonitor();
    }

    /// <summary>
    /// Ensures the flyout doesn't overflow past the edges of the monitor it's on.
    /// </summary>
    private void ClampToMonitor()
    {
        // Find the screen that contains the flyout's current position
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)(Left * DpiScale), (int)(Top * DpiScale)));
        var workArea = screen.WorkingArea;

        // Convert physical-pixel work area to DIPs
        var dpi = DpiScale;
        var waLeft = workArea.Left / dpi;
        var waTop = workArea.Top / dpi;
        var waRight = workArea.Right / dpi;
        var waBottom = workArea.Bottom / dpi;

        // Clamp so the flyout stays fully within the monitor
        if (Left + ActualWidth > waRight)
            Left = waRight - ActualWidth;
        if (Left < waLeft)
            Left = waLeft;
        if (Top + ActualHeight > waBottom)
            Top = waBottom - ActualHeight;
        if (Top < waTop)
            Top = waTop;
    }

    private double DpiScale
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
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

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _volumeService.VolumeChanged -= OnVolumeChanged;
        _volumeService.DevicesChanged -= OnDevicesChanged;
        base.OnClosed(e);
    }
}
