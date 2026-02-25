using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace Harbor.Core.Services;

public enum BluetoothIconState
{
    Off,
    On,
    Connected,
}

public sealed class BluetoothDeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public BluetoothDeviceCategory Category { get; init; }
    public bool IsConnected { get; init; }
    public int? BatteryPercent { get; init; }
}

public enum BluetoothDeviceCategory
{
    Audio,
    Keyboard,
    Mouse,
    Phone,
    Other,
}

public sealed class BluetoothChangedEventArgs : EventArgs
{
    public bool IsAvailable { get; init; }
    public bool IsEnabled { get; init; }
    public BluetoothIconState IconState { get; init; }
    public int ConnectedDeviceCount { get; init; }
}

public sealed class BluetoothService : IDisposable
{
    private Radio? _bluetoothRadio;
    private DeviceWatcher? _deviceWatcher;
    private readonly object _lock = new();
    private readonly Dictionary<string, BluetoothDeviceInfo> _connectedDevices = new();
    private bool _disposed;

    public bool IsAvailable { get; private set; }
    public bool IsEnabled { get; private set; }
    public int ConnectedDeviceCount { get; private set; }
    public BluetoothIconState IconState { get; private set; }
    public IReadOnlyList<BluetoothDeviceInfo> ConnectedDevices
    {
        get
        {
            lock (_lock)
            {
                return _connectedDevices.Values.ToList();
            }
        }
    }

    public event EventHandler<BluetoothChangedEventArgs>? BluetoothChanged;
    public event EventHandler? DevicesChanged;

    public BluetoothService()
    {
        _ = InitializeAsync();
        Trace.WriteLine("[Harbor] BluetoothService: Initialized.");
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Find the Bluetooth radio
            var radios = await Radio.GetRadiosAsync();
            _bluetoothRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

            if (_bluetoothRadio is null)
            {
                IsAvailable = false;
                IconState = BluetoothIconState.Off;
                RaiseBluetoothChanged();
                Trace.WriteLine("[Harbor] BluetoothService: No Bluetooth adapter found.");
                return;
            }

            IsAvailable = true;
            IsEnabled = _bluetoothRadio.State == RadioState.On;
            _bluetoothRadio.StateChanged += OnRadioStateChanged;

            // Start watching connected Bluetooth devices
            StartDeviceWatcher();

            UpdateIconState();
            RaiseBluetoothChanged();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Failed to initialize: {ex.Message}");
            IsAvailable = false;
            IconState = BluetoothIconState.Off;
            RaiseBluetoothChanged();
        }
    }

    private void StartDeviceWatcher()
    {
        // Watch for connected Bluetooth devices using the Bluetooth LE and Classic selectors
        var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);

        _deviceWatcher = DeviceInformation.CreateWatcher(
            selector,
            new[] { "System.Devices.Aep.IsConnected" });

        _deviceWatcher.Added += OnDeviceAdded;
        _deviceWatcher.Removed += OnDeviceRemoved;
        _deviceWatcher.Updated += OnDeviceUpdated;
        _deviceWatcher.EnumerationCompleted += OnEnumerationCompleted;

        _deviceWatcher.Start();
        Trace.WriteLine("[Harbor] BluetoothService: Device watcher started.");
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        lock (_lock)
        {
            IsEnabled = sender.State == RadioState.On;
            UpdateIconState();
        }

        RaiseBluetoothChanged();
        Trace.WriteLine($"[Harbor] BluetoothService: Radio state changed to {sender.State}.");
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device)
    {
        var info = CreateDeviceInfo(device, isConnected: true);

        lock (_lock)
        {
            _connectedDevices[device.Id] = info;
            ConnectedDeviceCount = _connectedDevices.Count;
            UpdateIconState();
        }

        RaiseBluetoothChanged();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine($"[Harbor] BluetoothService: Device connected: {info.Name}");
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        lock (_lock)
        {
            _connectedDevices.Remove(device.Id);
            ConnectedDeviceCount = _connectedDevices.Count;
            UpdateIconState();
        }

        RaiseBluetoothChanged();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine($"[Harbor] BluetoothService: Device disconnected: {device.Id}");
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        lock (_lock)
        {
            if (_connectedDevices.TryGetValue(device.Id, out var existing))
            {
                // Re-create with updated connection status
                var isConnected = true;
                if (device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var val) && val is bool connected)
                    isConnected = connected;

                if (!isConnected)
                {
                    _connectedDevices.Remove(device.Id);
                }

                ConnectedDeviceCount = _connectedDevices.Count;
                UpdateIconState();
            }
        }

        RaiseBluetoothChanged();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        Trace.WriteLine($"[Harbor] BluetoothService: Initial device enumeration complete. {ConnectedDeviceCount} connected.");
    }

    private static BluetoothDeviceInfo CreateDeviceInfo(DeviceInformation device, bool isConnected)
    {
        return new BluetoothDeviceInfo
        {
            Id = device.Id,
            Name = string.IsNullOrEmpty(device.Name) ? "Unknown Device" : device.Name,
            Category = CategorizeDevice(device),
            IsConnected = isConnected,
            BatteryPercent = null, // Battery level requires per-device GATT query, not implemented
        };
    }

    private static BluetoothDeviceCategory CategorizeDevice(DeviceInformation device)
    {
        var name = device.Name?.ToLowerInvariant() ?? "";

        if (name.Contains("headphone") || name.Contains("earphone") || name.Contains("earbud") ||
            name.Contains("speaker") || name.Contains("audio") || name.Contains("airpod") ||
            name.Contains("beats") || name.Contains("buds") || name.Contains("headset"))
            return BluetoothDeviceCategory.Audio;

        if (name.Contains("keyboard") || name.Contains("keychron"))
            return BluetoothDeviceCategory.Keyboard;

        if (name.Contains("mouse") || name.Contains("trackpad") || name.Contains("magic trackpad"))
            return BluetoothDeviceCategory.Mouse;

        if (name.Contains("phone") || name.Contains("iphone") || name.Contains("galaxy") ||
            name.Contains("pixel"))
            return BluetoothDeviceCategory.Phone;

        return BluetoothDeviceCategory.Other;
    }

    private void UpdateIconState()
    {
        if (!IsAvailable || !IsEnabled)
        {
            IconState = BluetoothIconState.Off;
        }
        else if (ConnectedDeviceCount > 0)
        {
            IconState = BluetoothIconState.Connected;
        }
        else
        {
            IconState = BluetoothIconState.On;
        }
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_bluetoothRadio is null) return;

        try
        {
            var targetState = enabled ? RadioState.On : RadioState.Off;
            var result = await _bluetoothRadio.SetStateAsync(targetState);

            if (result != RadioAccessStatus.Allowed)
            {
                Trace.WriteLine($"[Harbor] BluetoothService: Radio state change denied: {result}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Failed to set radio state: {ex.Message}");
        }
    }

    private void RaiseBluetoothChanged()
    {
        BluetoothChanged?.Invoke(this, new BluetoothChangedEventArgs
        {
            IsAvailable = IsAvailable,
            IsEnabled = IsEnabled,
            IconState = IconState,
            ConnectedDeviceCount = ConnectedDeviceCount,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_bluetoothRadio is not null)
        {
            _bluetoothRadio.StateChanged -= OnRadioStateChanged;
            _bluetoothRadio = null;
        }

        if (_deviceWatcher is not null)
        {
            try
            {
                if (_deviceWatcher.Status == DeviceWatcherStatus.Started ||
                    _deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _deviceWatcher.Stop();
                }
            }
            catch { }

            _deviceWatcher.Added -= OnDeviceAdded;
            _deviceWatcher.Removed -= OnDeviceRemoved;
            _deviceWatcher.Updated -= OnDeviceUpdated;
            _deviceWatcher.EnumerationCompleted -= OnEnumerationCompleted;
            _deviceWatcher = null;
        }

        lock (_lock)
        {
            _connectedDevices.Clear();
        }

        Trace.WriteLine("[Harbor] BluetoothService: Disposed.");
    }
}
