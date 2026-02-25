using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Harbor.Core.Services;

namespace Harbor.Shell.Flyouts;

public partial class BluetoothFlyout : Window
{
    private readonly BluetoothService _bluetoothService;
    private FlyoutMouseHook? _mouseHook;
    private bool _suppressToggleEvent;

    public BluetoothFlyout(BluetoothService bluetoothService)
    {
        InitializeComponent();

        _bluetoothService = bluetoothService;

        // Set initial state
        _suppressToggleEvent = true;
        BluetoothToggle.IsChecked = _bluetoothService.IsEnabled;
        _suppressToggleEvent = false;

        UpdateDeviceList();
        UpdateDevicesVisibility();

        // Subscribe to live changes
        _bluetoothService.BluetoothChanged += OnBluetoothChanged;
        _bluetoothService.DevicesChanged += OnDevicesChanged;

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

    private void ClampToMonitor()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)(Left * DpiScale), (int)(Top * DpiScale)));
        var workArea = screen.WorkingArea;

        var dpi = DpiScale;
        var waLeft = workArea.Left / dpi;
        var waTop = workArea.Top / dpi;
        var waRight = workArea.Right / dpi;
        var waBottom = workArea.Bottom / dpi;

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

    private void OnBluetoothChanged(object? sender, BluetoothChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _suppressToggleEvent = true;
            BluetoothToggle.IsChecked = e.IsEnabled;
            _suppressToggleEvent = false;

            UpdateDevicesVisibility();
        });
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateDeviceList();
            UpdateDevicesVisibility();
        });
    }

    private void UpdateDeviceList()
    {
        DeviceList.ItemsSource = _bluetoothService.ConnectedDevices;
    }

    private void UpdateDevicesVisibility()
    {
        var isEnabled = _bluetoothService.IsEnabled;
        var hasDevices = _bluetoothService.ConnectedDeviceCount > 0;

        DevicesLabel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        DeviceList.Visibility = isEnabled && hasDevices ? Visibility.Visible : Visibility.Collapsed;
        NoDevicesText.Visibility = isEnabled && !hasDevices ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BluetoothToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;

        var enabled = BluetoothToggle.IsChecked == true;
        _ = _bluetoothService.SetEnabledAsync(enabled);
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

    private void BluetoothSettings_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothFlyout: Failed to open Bluetooth Settings: {ex.Message}");
        }
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _bluetoothService.BluetoothChanged -= OnBluetoothChanged;
        _bluetoothService.DevicesChanged -= OnDevicesChanged;
        base.OnClosed(e);
    }
}
