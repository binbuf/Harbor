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

    // Tracks device IDs that have a connect/disconnect operation in flight.
    // The list is rebuilt whenever this changes so the "Connecting..." label appears.
    private readonly HashSet<string> _pendingDeviceIds = new();

    public BluetoothFlyout(BluetoothService bluetoothService)
    {
        InitializeComponent();

        _bluetoothService = bluetoothService;

        // Set initial state
        _suppressToggleEvent = true;
        BluetoothToggle.IsChecked = _bluetoothService.IsEnabled;
        _suppressToggleEvent = false;

        UpdateDeviceLists();
        UpdateDevicesVisibility();

        // Subscribe to live changes
        _bluetoothService.BluetoothChanged += OnBluetoothChanged;
        _bluetoothService.DevicesChanged += OnDevicesChanged;

        // Clamp position to monitor bounds once layout is known
        ContentRendered += OnContentRendered;

        // Install global mouse hook to dismiss on click-outside
        Loaded += (_, _) => _mouseHook = new FlyoutMouseHook(this, Close);
    }

    // ─── View model ─────────────────────────────────────────────────────────

    /// <summary>
    /// Thin wrapper around <see cref="BluetoothDeviceInfo"/> that adds UI state
    /// (pending status, status text) without requiring a full MVVM framework.
    /// </summary>
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

    // ─── List updates ────────────────────────────────────────────────────────

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

        DevicesLabel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        ConnectedDeviceList.Visibility = isEnabled && hasConnected ? Visibility.Visible : Visibility.Collapsed;
        RecentSection.Visibility = isEnabled && hasRecent ? Visibility.Visible : Visibility.Collapsed;
        NoDevicesText.Visibility = isEnabled && !hasConnected && !hasRecent ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── Event handlers (service events) ────────────────────────────────────

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
            // Clear pending state for any device that has finished connecting/disconnecting
            // (it will now appear in the correct list, so the pending label is no longer needed)
            var connectedIds = _bluetoothService.ConnectedDevices.Select(d => d.Id).ToHashSet();
            var recentIds = _bluetoothService.RecentAudioDevices.Select(d => d.Id).ToHashSet();
            _pendingDeviceIds.RemoveWhere(id => connectedIds.Contains(id) || recentIds.Contains(id));

            UpdateDeviceLists();
            UpdateDevicesVisibility();
        });
    }

    // ─── Event handlers (UI interaction) ────────────────────────────────────

    private void BluetoothToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;

        var enabled = BluetoothToggle.IsChecked == true;
        _ = _bluetoothService.SetEnabledAsync(enabled);
    }

    private void ConnectedDevice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DeviceViewModel vm) return;
        if (vm.IsPending) return; // already in flight

        var device = vm.Device;
        _pendingDeviceIds.Add(device.Id);
        UpdateDeviceLists();

        _ = DisconnectAndClearAsync(device);
    }

    private void RecentDevice_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not DeviceViewModel vm) return;
        if (vm.IsPending) return; // already in flight

        var device = vm.Device;
        _pendingDeviceIds.Add(device.Id);
        UpdateDeviceLists();

        _ = ConnectAndClearAsync(device);
    }

    private async Task DisconnectAndClearAsync(BluetoothDeviceInfo device)
    {
        await _bluetoothService.DisconnectDeviceAsync(device);

        // The DevicesChanged event will clear pending state once the watcher sees
        // the disconnect. As a safety net, also clear after a timeout.
        await ClearPendingAfterTimeoutAsync(device.Id);
    }

    private async Task ConnectAndClearAsync(BluetoothDeviceInfo device)
    {
        await _bluetoothService.ConnectDeviceAsync(device);

        // Same safety-net timeout as above
        await ClearPendingAfterTimeoutAsync(device.Id);
    }

    /// <summary>
    /// Removes the pending state for a device after a timeout in case the
    /// DevicesChanged event never fires (e.g., device is out of range).
    /// </summary>
    private async Task ClearPendingAfterTimeoutAsync(string deviceId, int timeoutMs = 10_000)
    {
        await Task.Delay(timeoutMs);

        Dispatcher.Invoke(() =>
        {
            if (_pendingDeviceIds.Remove(deviceId))
                UpdateDeviceLists();
        });
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

    // ─── Positioning ─────────────────────────────────────────────────────────

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

    // ─── Cleanup ──────────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _bluetoothService.BluetoothChanged -= OnBluetoothChanged;
        _bluetoothService.DevicesChanged -= OnDevicesChanged;
        base.OnClosed(e);
    }
}
