using System.Diagnostics;
using System.Runtime.InteropServices;
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

    // In-memory list of up to 3 recently-disconnected audio devices, shown in the
    // flyout to enable quick reconnect — matches macOS Bluetooth menu behavior.
    private readonly List<BluetoothDeviceInfo> _recentAudioDevices = new();
    private const int MaxRecentDevices = 3;

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

    /// <summary>
    /// Audio devices that were recently connected but are currently disconnected.
    /// Populated when a connected audio device disconnects; cleared when it reconnects.
    /// Resets when the app restarts (in-memory only).
    /// </summary>
    public IReadOnlyList<BluetoothDeviceInfo> RecentAudioDevices
    {
        get
        {
            lock (_lock)
            {
                return _recentAudioDevices.ToList();
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

            // If this device was in the recent list, remove it — it's connected now
            _recentAudioDevices.RemoveAll(d => d.Id == device.Id);

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
            if (_connectedDevices.TryGetValue(device.Id, out var removedDevice))
            {
                _connectedDevices.Remove(device.Id);

                // Track audio devices in the recent list for quick-reconnect
                if (removedDevice.Category == BluetoothDeviceCategory.Audio)
                {
                    // Remove any existing entry for this device (avoid duplicates)
                    _recentAudioDevices.RemoveAll(d => d.Id == removedDevice.Id);

                    // Add to front of the list (most recent first)
                    _recentAudioDevices.Insert(0, new BluetoothDeviceInfo
                    {
                        Id = removedDevice.Id,
                        Name = removedDevice.Name,
                        Category = removedDevice.Category,
                        IsConnected = false,
                        BatteryPercent = null,
                    });

                    // Trim to max
                    if (_recentAudioDevices.Count > MaxRecentDevices)
                        _recentAudioDevices.RemoveAt(_recentAudioDevices.Count - 1);
                }
            }

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

                    // Track audio devices in the recent list
                    if (existing.Category == BluetoothDeviceCategory.Audio)
                    {
                        _recentAudioDevices.RemoveAll(d => d.Id == existing.Id);
                        _recentAudioDevices.Insert(0, new BluetoothDeviceInfo
                        {
                            Id = existing.Id,
                            Name = existing.Name,
                            Category = existing.Category,
                            IsConnected = false,
                            BatteryPercent = null,
                        });

                        if (_recentAudioDevices.Count > MaxRecentDevices)
                            _recentAudioDevices.RemoveAt(_recentAudioDevices.Count - 1);
                    }
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

    /// <summary>
    /// Attempts to connect a previously-paired Bluetooth audio device by enabling
    /// its A2DP Sink service via Win32 BluetoothSetServiceState.
    /// </summary>
    public async Task<bool> ConnectDeviceAsync(BluetoothDeviceInfo device)
    {
        Trace.WriteLine($"[Harbor] BluetoothService: Attempting to connect {device.Name}...");
        return await SetDeviceServiceStateAsync(device.Id, enable: true);
    }

    /// <summary>
    /// Disconnects a currently-connected Bluetooth audio device by disabling
    /// its A2DP Sink service via Win32 BluetoothSetServiceState.
    /// </summary>
    public async Task<bool> DisconnectDeviceAsync(BluetoothDeviceInfo device)
    {
        Trace.WriteLine($"[Harbor] BluetoothService: Attempting to disconnect {device.Name}...");
        return await SetDeviceServiceStateAsync(device.Id, enable: false);
    }

    private static async Task<bool> SetDeviceServiceStateAsync(string deviceId, bool enable)
    {
        // Resolve Bluetooth address via WinRT (DeviceInformation ID → BluetoothDevice)
        ulong bluetoothAddress;
        try
        {
            using var device = await BluetoothDevice.FromIdAsync(deviceId);
            if (device is null)
            {
                Trace.WriteLine($"[Harbor] BluetoothService: Could not resolve device {deviceId}");
                return false;
            }
            bluetoothAddress = device.BluetoothAddress;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Failed to resolve device {deviceId}: {ex.Message}");
            return false;
        }

        // Run the Win32 Bluetooth API calls on a thread-pool thread
        return await Task.Run(() => SetServiceStateWin32(bluetoothAddress, enable));
    }

    private static bool SetServiceStateWin32(ulong bluetoothAddress, bool enable)
    {
        var serviceGuid = BluetoothNative.A2dpSinkService;
        uint flags = enable ? BluetoothNative.BLUETOOTH_SERVICE_ENABLE
                            : BluetoothNative.BLUETOOTH_SERVICE_DISABLE;

        // Open the first available Bluetooth radio
        var radioParams = new BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        var radioFind = BluetoothNative.BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFind == IntPtr.Zero)
        {
            Trace.WriteLine("[Harbor] BluetoothService: BluetoothFindFirstRadio failed.");
            return false;
        }

        try
        {
            var searchParams = new BluetoothNative.BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                fReturnAuthenticated = 1,
                fReturnRemembered = 1,
                fReturnUnknown = 0,
                fReturnConnected = 1,
                fIssueInquiry = 0,
                cTimeoutMultiplier = 0,
                hRadio = radioHandle,
            };

            var deviceInfo = new BluetoothNative.BLUETOOTH_DEVICE_INFO
            {
                dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_DEVICE_INFO>(),
                szName = string.Empty,
            };

            var deviceFind = BluetoothNative.BluetoothFindFirstDevice(ref searchParams, ref deviceInfo);
            if (deviceFind == IntPtr.Zero)
            {
                Trace.WriteLine("[Harbor] BluetoothService: BluetoothFindFirstDevice found no devices.");
                return false;
            }

            try
            {
                do
                {
                    if (deviceInfo.Address.ullLong == bluetoothAddress)
                    {
                        var result = BluetoothNative.BluetoothSetServiceState(
                            radioHandle, ref deviceInfo, ref serviceGuid, flags);

                        if (result == 0)
                        {
                            Trace.WriteLine($"[Harbor] BluetoothService: Service state set ({(enable ? "connect" : "disconnect")}) for {deviceInfo.szName}");
                            return true;
                        }

                        Trace.WriteLine($"[Harbor] BluetoothService: BluetoothSetServiceState failed with error {result} for {deviceInfo.szName}");
                        return false;
                    }

                    // Reset szName before next call to avoid stale data
                    deviceInfo.szName = string.Empty;
                }
                while (BluetoothNative.BluetoothFindNextDevice(deviceFind, ref deviceInfo));
            }
            finally
            {
                BluetoothNative.BluetoothFindDeviceClose(deviceFind);
            }
        }
        finally
        {
            BluetoothNative.BluetoothFindRadioClose(radioFind);
            BluetoothNative.CloseHandle(radioHandle);
        }

        Trace.WriteLine($"[Harbor] BluetoothService: Device with address {bluetoothAddress:X12} not found in radio device list.");
        return false;
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
            _recentAudioDevices.Clear();
        }

        Trace.WriteLine("[Harbor] BluetoothService: Disposed.");
    }
}
