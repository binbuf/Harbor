using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Harbor.Core.Services;

namespace Harbor.Shell.Flyouts;

public partial class BluetoothFlyout : Window
{
    private readonly BluetoothService _bluetoothService;
    private FlyoutMouseHook? _mouseHook;
    private bool _suppressToggleEvent;
    private readonly HashSet<string> _pendingDeviceIds = new();

    public BluetoothFlyout(BluetoothService bluetoothService)
    {
        InitializeComponent();

        _bluetoothService = bluetoothService;

        _suppressToggleEvent = true;
        BluetoothToggle.IsChecked = _bluetoothService.IsEnabled;
        _suppressToggleEvent = false;

        UpdateDeviceLists();
        UpdateDevicesVisibility();

        _bluetoothService.BluetoothChanged += OnBluetoothChanged;
        _bluetoothService.DevicesChanged += OnDevicesChanged;
        _bluetoothService.NearbyDevicesChanged += OnNearbyDevicesChanged;

        ContentRendered += OnContentRendered;

        Loaded += (_, _) =>
        {
            _mouseHook = new FlyoutMouseHook(this, Close);
            // Start BT inquiry scan so nearby devices appear as the flyout opens
            _bluetoothService.StartDiscovery();
            UpdateNearbySection();
        };
    }

    // ─── View model ───────────────────────────────────────────────────────────

    private sealed class DeviceViewModel
    {
        public BluetoothDeviceInfo Device { get; }
        public string Name => Device.Name;
        public bool IsPending { get; }
        public bool ShowStatus { get; }
        public string StatusText { get; }

        public DeviceViewModel(BluetoothDeviceInfo device, bool isPending)
        {
            Device = device;
            IsPending = isPending;

            if (isPending)
            {
                StatusText = device.IsConnected ? "Disconnecting..." : "Connecting...";
                ShowStatus = true;
            }
            else if (device.IsConnected)
            {
                StatusText = "Connected";
                ShowStatus = true;
            }
            else
            {
                StatusText = string.Empty;
                ShowStatus = false;
            }
        }
    }

    // ─── List updates ─────────────────────────────────────────────────────────

    private void UpdateDeviceLists()
    {
        ConnectedDeviceList.ItemsSource = _bluetoothService.ConnectedDevices
            .Select(d => new DeviceViewModel(d, _pendingDeviceIds.Contains(d.Id)))
            .ToList();

        var recent = _bluetoothService.RecentAudioDevices
            .Select(d => new DeviceViewModel(d, _pendingDeviceIds.Contains(d.Id)))
            .ToList();

        RecentDeviceList.ItemsSource = recent;
        RecentSection.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDevicesVisibility()
    {
        var isEnabled = _bluetoothService.IsEnabled;
        var hasConnected = _bluetoothService.ConnectedDeviceCount > 0;
        var hasRecent = _bluetoothService.RecentAudioDevices.Count > 0;
        var hasNearby = _bluetoothService.NearbyDevices.Count > 0;
        var isDiscovering = _bluetoothService.IsDiscovering;

        DevicesLabel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        ConnectedDeviceList.Visibility = isEnabled && hasConnected ? Visibility.Visible : Visibility.Collapsed;
        RecentSection.Visibility = isEnabled && hasRecent ? Visibility.Visible : Visibility.Collapsed;
        NoDevicesText.Visibility = isEnabled && !hasConnected && !hasRecent && !hasNearby && !isDiscovering
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateNearbySection()
    {
        if (!_bluetoothService.IsEnabled)
        {
            NearbySection.Visibility = Visibility.Collapsed;
            return;
        }

        var nearby = _bluetoothService.NearbyDevices
            .Select(d => new DeviceViewModel(d, _pendingDeviceIds.Contains(d.Id)))
            .ToList();

        NearbyDeviceList.ItemsSource = nearby;

        var isDiscovering = _bluetoothService.IsDiscovering;
        NearbySection.Visibility = isDiscovering || nearby.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ScanningPlaceholder.Visibility = isDiscovering && nearby.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Animate the scan dot while discovering
        if (isDiscovering)
        {
            var storyboard = (Storyboard)FindResource("SpinAnimation");
            storyboard.Begin();
        }
        else
        {
            var storyboard = (Storyboard)FindResource("SpinAnimation");
            storyboard.Stop();
        }
    }

    // ─── Service event handlers ───────────────────────────────────────────────

    private void OnBluetoothChanged(object? sender, BluetoothChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _suppressToggleEvent = true;
            BluetoothToggle.IsChecked = e.IsEnabled;
            _suppressToggleEvent = false;

            UpdateDevicesVisibility();
            UpdateNearbySection();
        });
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Clear pending state for devices that have finished transitioning
            var connectedIds = _bluetoothService.ConnectedDevices.Select(d => d.Id).ToHashSet();
            var recentIds = _bluetoothService.RecentAudioDevices.Select(d => d.Id).ToHashSet();
            _pendingDeviceIds.RemoveWhere(id => connectedIds.Contains(id) || recentIds.Contains(id));

            UpdateDeviceLists();
            UpdateDevicesVisibility();
        });
    }

    private void OnNearbyDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Clear pending for nearby devices that became paired
            var nearbyIds = _bluetoothService.NearbyDevices.Select(d => d.Id).ToHashSet();
            _pendingDeviceIds.RemoveWhere(id => !nearbyIds.Contains(id));

            UpdateNearbySection();
            UpdateDevicesVisibility();
        });
    }

    // ─── UI interaction ───────────────────────────────────────────────────────

    private void BluetoothToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        _ = _bluetoothService.SetEnabledAsync(BluetoothToggle.IsChecked == true);
    }

    private void ConnectedDevice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DeviceViewModel vm) return;
        if (vm.IsPending) return;

        var device = vm.Device;
        _pendingDeviceIds.Add(device.Id);
        UpdateDeviceLists();

        _ = RunWithTimeout(
            _bluetoothService.DisconnectDeviceAsync(device),
            device.Id,
            timeoutMs: 10_000);
    }

    private void RecentDevice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DeviceViewModel vm) return;
        if (vm.IsPending) return;

        var device = vm.Device;
        _pendingDeviceIds.Add(device.Id);
        UpdateDeviceLists();

        _ = RunWithTimeout(
            _bluetoothService.ConnectDeviceAsync(device),
            device.Id,
            timeoutMs: 10_000);
    }

    private void NearbyDevice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DeviceViewModel vm) return;
        if (vm.IsPending) return;

        var device = vm.Device;
        _pendingDeviceIds.Add(device.Id);
        UpdateNearbySection();

        // Longer timeout for pairing (may involve user confirmation on the device)
        _ = RunWithTimeout(
            _bluetoothService.PairAndConnectDeviceAsync(device),
            device.Id,
            timeoutMs: 30_000);
    }

    /// <summary>
    /// Awaits the given task and then clears the device's pending state after
    /// it completes or after a timeout, whichever comes first.
    /// </summary>
    private async Task RunWithTimeout(Task<bool> operation, string deviceId, int timeoutMs)
    {
        await Task.WhenAny(operation, Task.Delay(timeoutMs));

        Dispatcher.Invoke(() =>
        {
            if (_pendingDeviceIds.Remove(deviceId))
            {
                UpdateDeviceLists();
                UpdateNearbySection();
            }
        });
    }

    private void DeviceRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = (Brush)FindResource("FlyoutItemHoverBackground");
    }

    private void DeviceRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = Brushes.Transparent;
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

    // ─── Positioning ──────────────────────────────────────────────────────────

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

        if (Left + ActualWidth > workArea.Right / dpi)   Left = workArea.Right / dpi - ActualWidth;
        if (Left < workArea.Left / dpi)                   Left = workArea.Left / dpi;
        if (Top + ActualHeight > workArea.Bottom / dpi)   Top = workArea.Bottom / dpi - ActualHeight;
        if (Top < workArea.Top / dpi)                     Top = workArea.Top / dpi;
    }

    private double DpiScale
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _bluetoothService.StopDiscovery();
        _mouseHook?.Dispose();
        _mouseHook = null;
        _bluetoothService.BluetoothChanged -= OnBluetoothChanged;
        _bluetoothService.DevicesChanged -= OnDevicesChanged;
        _bluetoothService.NearbyDevicesChanged -= OnNearbyDevicesChanged;
        base.OnClosed(e);
    }
}
