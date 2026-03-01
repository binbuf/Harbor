using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
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

    /// <summary>
    /// UTC time this device was last seen as connected (set when it disconnects).
    /// Used to order the recent list across restarts.
    /// </summary>
    public DateTime LastUsedUtc { get; init; }
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
    private DeviceWatcher? _discoveryWatcher;

    private readonly object _lock = new();
    private readonly Dictionary<string, BluetoothDeviceInfo> _connectedDevices = new();
    private readonly List<BluetoothDeviceInfo> _recentAudioDevices = new();
    private readonly Dictionary<string, BluetoothDeviceInfo> _nearbyDevices = new();
    private const int MaxRecentDevices = 3;

    // Properties requested from the device watcher.
    // NOTE: Do NOT add "System.Devices.Aep.Bluetooth.ClassOfDevice" here — it is not
    // a valid AEP property for the BluetoothDevice selector and causes the watcher to
    // silently stop returning devices. Category is resolved via BluetoothDevice.FromIdAsync.
    private static readonly string[] _deviceProperties =
    [
        "System.Devices.Aep.IsConnected",
    ];

    private static readonly string _recentDevicesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Harbor", "bluetooth_recent.json");

    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private bool _disposed;
    private bool _isDiscovering;

    public bool IsAvailable { get; private set; }
    public bool IsEnabled { get; private set; }
    public int ConnectedDeviceCount { get; private set; }
    public BluetoothIconState IconState { get; private set; }

    public bool IsDiscovering
    {
        get { lock (_lock) { return _isDiscovering; } }
    }

    public IReadOnlyList<BluetoothDeviceInfo> ConnectedDevices
    {
        get { lock (_lock) { return _connectedDevices.Values.ToList(); } }
    }

    /// <summary>
    /// All paired audio devices not currently connected, most-recently-used first.
    /// Populated from ALL paired devices on startup (not just session history),
    /// so devices appear immediately on first run.
    /// </summary>
    public IReadOnlyList<BluetoothDeviceInfo> RecentAudioDevices
    {
        get { lock (_lock) { return _recentAudioDevices.ToList(); } }
    }

    /// <summary>
    /// Nearby devices discovered by the active inquiry scan (unpaired, in range).
    /// Only populated while <see cref="IsDiscovering"/> is true.
    /// </summary>
    public IReadOnlyList<BluetoothDeviceInfo> NearbyDevices
    {
        get { lock (_lock) { return _nearbyDevices.Values.ToList(); } }
    }

    public event EventHandler<BluetoothChangedEventArgs>? BluetoothChanged;
    public event EventHandler? DevicesChanged;
    public event EventHandler? NearbyDevicesChanged;

    public BluetoothService()
    {
        _ = InitializeAsync();
        Trace.WriteLine("[Harbor] BluetoothService: Initialized.");
    }

    private async Task InitializeAsync()
    {
        try
        {
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

            // Fire immediately so the icon appears in the menu bar right away.
            // LoadRecentDevicesAsync calls DeviceInformation.FindAllAsync which can
            // take several seconds — we must not block the icon on it.
            UpdateIconState();
            RaiseBluetoothChanged();

            await LoadRecentDevicesAsync();

            StartDeviceWatcher();

            // Fire again in case a connected device was found during enumeration
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

    // ─── Categorization ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves device category using <see cref="BluetoothDevice.FromIdAsync"/> to read
    /// the strongly-typed <see cref="BluetoothClassOfDevice.MajorClass"/>. This is the
    /// only reliable way to identify audio devices whose names give no hint (e.g.
    /// "WH-1000XM5", "QC45"). Falls back to name-based heuristics if the WinRT call fails.
    /// </summary>
    private static async Task<BluetoothDeviceCategory> GetCategoryAsync(string deviceId, string? deviceName)
    {
        try
        {
            using var btDevice = await BluetoothDevice.FromIdAsync(deviceId);
            if (btDevice is not null)
            {
                return btDevice.ClassOfDevice.MajorClass switch
                {
                    BluetoothMajorClass.AudioVideo => BluetoothDeviceCategory.Audio,
                    BluetoothMajorClass.Phone      => BluetoothDeviceCategory.Phone,
                    BluetoothMajorClass.Peripheral => CategorizePeripheralByName(deviceName),
                    _                              => CategorizeByName(deviceName),
                };
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: CoD lookup failed for {deviceId}: {ex.Message}");
        }

        return CategorizeByName(deviceName);
    }

    private static BluetoothDeviceCategory CategorizePeripheralByName(string? name)
    {
        var n = name?.ToLowerInvariant() ?? "";
        if (n.Contains("mouse") || n.Contains("trackpad")) return BluetoothDeviceCategory.Mouse;
        if (n.Contains("keyboard") || n.Contains("keychron")) return BluetoothDeviceCategory.Keyboard;
        return BluetoothDeviceCategory.Other;
    }

    private static BluetoothDeviceCategory CategorizeByName(string? name)
    {
        var n = name?.ToLowerInvariant() ?? "";

        if (n.Contains("headphone") || n.Contains("earphone") || n.Contains("earbud") ||
            n.Contains("speaker") || n.Contains("audio") || n.Contains("airpod") ||
            n.Contains("beats") || n.Contains("buds") || n.Contains("headset"))
            return BluetoothDeviceCategory.Audio;

        if (n.Contains("keyboard") || n.Contains("keychron"))
            return BluetoothDeviceCategory.Keyboard;

        if (n.Contains("mouse") || n.Contains("trackpad") || n.Contains("magic trackpad"))
            return BluetoothDeviceCategory.Mouse;

        if (n.Contains("phone") || n.Contains("iphone") || n.Contains("galaxy") ||
            n.Contains("pixel"))
            return BluetoothDeviceCategory.Phone;

        return BluetoothDeviceCategory.Other;
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private sealed record PersistedDevice(string Id, string Name, string Category, DateTime LastUsedUtc);

    private async Task LoadRecentDevicesAsync()
    {
        try
        {
            // 1. Load persisted timestamps (keyed by device ID) for ordering
            var timestamps = new Dictionary<string, DateTime>();
            if (File.Exists(_recentDevicesFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_recentDevicesFilePath);
                    var records = JsonSerializer.Deserialize<List<PersistedDevice>>(json);
                    if (records is not null)
                        foreach (var r in records)
                            timestamps[r.Id] = r.LastUsedUtc;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[Harbor] BluetoothService: Could not read recent file: {ex.Message}");
                }
            }

            // 2. Enumerate ALL paired BT devices — source of truth for what can appear.
            //    No extra properties: CoD is resolved per-device via BluetoothDevice.FromIdAsync.
            var pairedSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var pairedDevices = await DeviceInformation.FindAllAsync(pairedSelector);

            // 3. Categorize each paired device using the reliable WinRT Bluetooth API.
            //    Run in parallel so the total wait is bounded by the slowest single lookup.
            HashSet<string> connectedIds;
            lock (_lock) { connectedIds = _connectedDevices.Keys.ToHashSet(); }

            var categoryTasks = pairedDevices
                .Where(d => !connectedIds.Contains(d.Id))
                .Select(async d => (
                    device: d,
                    category: await GetCategoryAsync(d.Id, d.Name)))
                .ToList();

            var categorized = await Task.WhenAll(categoryTasks);

            var candidates = categorized
                .Where(r => r.category == BluetoothDeviceCategory.Audio)
                .Select(r => new BluetoothDeviceInfo
                {
                    Id = r.device.Id,
                    Name = string.IsNullOrEmpty(r.device.Name) ? "Unknown Device" : r.device.Name,
                    Category = r.category,
                    IsConnected = false,
                    LastUsedUtc = timestamps.GetValueOrDefault(r.device.Id, DateTime.MinValue),
                })
                .ToList();

            // 4. Most-recently-used first; devices with no history go last
            var sorted = candidates
                .OrderByDescending(d => d.LastUsedUtc)
                .Take(MaxRecentDevices)
                .ToList();

            lock (_lock)
            {
                _recentAudioDevices.Clear();
                _recentAudioDevices.AddRange(sorted);
            }

            Trace.WriteLine($"[Harbor] BluetoothService: Loaded {sorted.Count} recent audio device(s) " +
                            $"({pairedDevices.Count} paired total, {timestamps.Count} with history).");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Failed to load recent devices: {ex.Message}");
        }
    }

    private void SaveRecentDevices()
    {
        var snapshot = _recentAudioDevices
            .Select(d => new PersistedDevice(d.Id, d.Name, d.Category.ToString(), d.LastUsedUtc))
            .ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_recentDevicesFilePath)!);
                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
                await File.WriteAllTextAsync(_recentDevicesFilePath, json);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] BluetoothService: Failed to save recent devices: {ex.Message}");
            }
        });
    }

    // ─── Connected device watcher ─────────────────────────────────────────────

    private void StartDeviceWatcher()
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
        _deviceWatcher = DeviceInformation.CreateWatcher(selector, _deviceProperties);

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
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation device)
    {
        // Fire async categorization and device registration without blocking the watcher thread
        _ = OnDeviceAddedAsync(device);
    }

    private async Task OnDeviceAddedAsync(DeviceInformation device)
    {
        var name = string.IsNullOrEmpty(device.Name) ? "Unknown Device" : device.Name;
        var category = await GetCategoryAsync(device.Id, device.Name);

        var info = new BluetoothDeviceInfo
        {
            Id = device.Id,
            Name = name,
            Category = category,
            IsConnected = true,
            BatteryPercent = null,
            LastUsedUtc = DateTime.UtcNow,
        };

        bool recentChanged;
        lock (_lock)
        {
            _connectedDevices[device.Id] = info;
            ConnectedDeviceCount = _connectedDevices.Count;
            recentChanged = _recentAudioDevices.RemoveAll(d => d.Id == device.Id) > 0;
            _nearbyDevices.Remove(device.Id);
            if (recentChanged) SaveRecentDevices();
            UpdateIconState();
        }

        RaiseBluetoothChanged();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine($"[Harbor] BluetoothService: Device connected: {name} (category: {category})");
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        lock (_lock)
        {
            if (_connectedDevices.TryGetValue(device.Id, out var removed))
            {
                _connectedDevices.Remove(device.Id);
                AddToRecent(removed);
            }
            ConnectedDeviceCount = _connectedDevices.Count;
            UpdateIconState();
        }

        RaiseBluetoothChanged();
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        lock (_lock)
        {
            if (_connectedDevices.TryGetValue(device.Id, out var existing))
            {
                var isConnected = true;
                if (device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var val) && val is bool b)
                    isConnected = b;

                if (!isConnected)
                {
                    _connectedDevices.Remove(device.Id);
                    AddToRecent(existing);
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
        Trace.WriteLine($"[Harbor] BluetoothService: Initial enumeration complete. {ConnectedDeviceCount} connected.");
    }

    private void AddToRecent(BluetoothDeviceInfo device)
    {
        if (device.Category != BluetoothDeviceCategory.Audio) return;

        _recentAudioDevices.RemoveAll(d => d.Id == device.Id);
        _recentAudioDevices.Insert(0, new BluetoothDeviceInfo
        {
            Id = device.Id,
            Name = device.Name,
            Category = device.Category,
            IsConnected = false,
            LastUsedUtc = DateTime.UtcNow,
        });

        if (_recentAudioDevices.Count > MaxRecentDevices)
            _recentAudioDevices.RemoveAt(_recentAudioDevices.Count - 1);

        SaveRecentDevices();
    }

    // ─── Discovery watcher ────────────────────────────────────────────────────

    /// <summary>
    /// Starts a Bluetooth inquiry scan that discovers nearby unpaired devices.
    /// Should be called when the flyout opens; stop with <see cref="StopDiscovery"/>.
    /// </summary>
    public void StartDiscovery()
    {
        lock (_lock)
        {
            if (_isDiscovering || !IsAvailable || !IsEnabled) return;
            _isDiscovering = true;
            _nearbyDevices.Clear();
        }

        // Classic BT protocol GUID — watcher performs an active inquiry scan
        const string btProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
        var selector = $"System.Devices.Aep.ProtocolId:=\"{btProtocol}\" " +
                       "AND System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#False";

        _discoveryWatcher = DeviceInformation.CreateWatcher(
            selector,
            new[] { "System.Devices.Aep.IsPaired" },
            DeviceInformationKind.AssociationEndpoint);

        _discoveryWatcher.Added += OnDiscoveryAdded;
        _discoveryWatcher.Removed += OnDiscoveryRemoved;
        _discoveryWatcher.Updated += OnDiscoveryUpdated;
        _discoveryWatcher.Start();

        Trace.WriteLine("[Harbor] BluetoothService: Discovery scan started.");
    }

    /// <summary>
    /// Stops the inquiry scan and clears the nearby device list.
    /// </summary>
    public void StopDiscovery()
    {
        lock (_lock)
        {
            if (!_isDiscovering) return;
            _isDiscovering = false;
        }

        if (_discoveryWatcher is not null)
        {
            try
            {
                if (_discoveryWatcher.Status is DeviceWatcherStatus.Started
                    or DeviceWatcherStatus.EnumerationCompleted)
                    _discoveryWatcher.Stop();
            }
            catch { }

            _discoveryWatcher.Added -= OnDiscoveryAdded;
            _discoveryWatcher.Removed -= OnDiscoveryRemoved;
            _discoveryWatcher.Updated -= OnDiscoveryUpdated;
            _discoveryWatcher = null;
        }

        lock (_lock) { _nearbyDevices.Clear(); }
        NearbyDevicesChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine("[Harbor] BluetoothService: Discovery scan stopped.");
    }

    private void OnDiscoveryAdded(DeviceWatcher sender, DeviceInformation device)
    {
        if (string.IsNullOrEmpty(device.Name)) return; // skip nameless advertisements

        // Use name-based categorization for discovery — devices may not be paired yet,
        // so BluetoothDevice.FromIdAsync is unlikely to return CoD for unknown devices.
        var info = new BluetoothDeviceInfo
        {
            Id = device.Id,
            Name = device.Name,
            Category = CategorizeByName(device.Name),
            IsConnected = false,
            LastUsedUtc = DateTime.MinValue,
        };

        lock (_lock)
        {
            // Skip if already known (connected or in recent paired list)
            if (_connectedDevices.ContainsKey(device.Id)) return;
            if (_recentAudioDevices.Any(d => d.Id == device.Id)) return;

            _nearbyDevices[device.Id] = info;
        }

        NearbyDevicesChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine($"[Harbor] BluetoothService: Nearby device found: {info.Name} (category: {info.Category})");
    }

    private void OnDiscoveryRemoved(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        lock (_lock) { _nearbyDevices.Remove(device.Id); }
        NearbyDevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDiscoveryUpdated(DeviceWatcher sender, DeviceInformationUpdate device)
    {
        // If the device just became paired (e.g., user paired via Settings), drop it from nearby
        if (device.Properties.TryGetValue("System.Devices.Aep.IsPaired", out var val)
            && val is bool isPaired && isPaired)
        {
            lock (_lock) { _nearbyDevices.Remove(device.Id); }
            NearbyDevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public async Task SetEnabledAsync(bool enabled)
    {
        if (_bluetoothRadio is null) return;
        try
        {
            var result = await _bluetoothRadio.SetStateAsync(enabled ? RadioState.On : RadioState.Off);
            if (result != RadioAccessStatus.Allowed)
                Trace.WriteLine($"[Harbor] BluetoothService: Radio state change denied: {result}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Failed to set radio state: {ex.Message}");
        }
    }

    public async Task<bool> ConnectDeviceAsync(BluetoothDeviceInfo device)
    {
        var ok = await ConnectViaSetServiceStateAsync(device.Id);
        if (ok) return true;

        Trace.WriteLine($"[Harbor] BluetoothService: BluetoothSetServiceState connect failed for '{device.Name}', trying KsControl.");
        return await BluetoothAudioConnector.ConnectAsync(device.Name);
    }

    public async Task<bool> DisconnectDeviceAsync(BluetoothDeviceInfo device)
    {
        var ok = await DisconnectViaSetServiceStateAsync(device.Id);
        if (ok) return true;

        Trace.WriteLine($"[Harbor] BluetoothService: BluetoothSetServiceState disconnect failed for '{device.Name}', trying KsControl.");
        ok = await BluetoothAudioConnector.DisconnectAsync(device.Name);
        if (ok) return true;

        Trace.WriteLine($"[Harbor] BluetoothService: KsControl disconnect failed for '{device.Name}', trying IOCTL.");
        return await DisconnectViaIoctlAsync(device.Id);
    }

    /// <summary>
    /// Pairs a newly-discovered nearby device and then attempts to connect it.
    /// Uses <see cref="DevicePairingProtectionLevel.None"/> for "Just Works"
    /// pairing (standard for audio headphones). Falls back to opening Bluetooth
    /// Settings if the device requires PIN confirmation.
    /// </summary>
    public async Task<bool> PairAndConnectDeviceAsync(BluetoothDeviceInfo device)
    {
        try
        {
            var deviceInfo = await DeviceInformation.CreateFromIdAsync(device.Id);
            if (deviceInfo is null) return false;

            if (!deviceInfo.Pairing.IsPaired)
            {
                Trace.WriteLine($"[Harbor] BluetoothService: Pairing with {device.Name}...");
                var result = await deviceInfo.Pairing.PairAsync(DevicePairingProtectionLevel.None);

                Trace.WriteLine($"[Harbor] BluetoothService: Pairing result for {device.Name}: {result.Status}");

                if (result.Status is not DevicePairingResultStatus.Paired
                    and not DevicePairingResultStatus.AlreadyPaired)
                    return false;
            }

            // Small delay to let the OS register the new pairing before connecting
            await Task.Delay(500);

            return device.Category == BluetoothDeviceCategory.Audio
                ? await BluetoothAudioConnector.ConnectAsync(device.Name)
                : true; // Non-audio devices auto-connect after pairing
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: PairAndConnect failed for {device.Name}: {ex.Message}");
            return false;
        }
    }

    // ─── BluetoothSetServiceState fallback connect ────────────────────────────

    /// <summary>
    /// Reconnects a paired BT audio device using BluetoothSetServiceState, which
    /// operates at the Bluetooth stack level and works even when the audio kernel
    /// filter (btha2dp.sys) is not loaded. Enables both A2DP and HFP services so
    /// either profile can bring up the ACL link.
    /// </summary>
    private static async Task<bool> ConnectViaSetServiceStateAsync(string deviceId)
    {
        ulong address;
        try
        {
            using var btDevice = await BluetoothDevice.FromIdAsync(deviceId);
            if (btDevice is null) return false;
            address = btDevice.BluetoothAddress;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Could not resolve BT address: {ex.Message}");
            return false;
        }

        return await Task.Run(() => SendSetServiceState(address));
    }

    private static bool SendSetServiceState(ulong bluetoothAddress)
    {
        var radioParams = new BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        var radioFind = BluetoothNative.BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFind == IntPtr.Zero) return false;

        try
        {
            var deviceInfo = new BluetoothNative.BLUETOOTH_DEVICE_INFO
            {
                dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_DEVICE_INFO>(),
                Address = new BluetoothNative.BLUETOOTH_ADDRESS { ullLong = bluetoothAddress },
                szName = string.Empty,
            };

            var a2dp = BluetoothNative.A2dpSinkService;
            var a2dpResult = BluetoothNative.BluetoothSetServiceState(
                radioHandle, ref deviceInfo, ref a2dp, BluetoothNative.BLUETOOTH_SERVICE_ENABLE);
            if (a2dpResult != 0)
                Trace.WriteLine($"[Harbor] BluetoothService: BluetoothSetServiceState(A2DP) failed, error={a2dpResult}");

            var hfp = BluetoothNative.HandsFreeService;
            var hfpResult = BluetoothNative.BluetoothSetServiceState(
                radioHandle, ref deviceInfo, ref hfp, BluetoothNative.BLUETOOTH_SERVICE_ENABLE);
            if (hfpResult != 0)
                Trace.WriteLine($"[Harbor] BluetoothService: BluetoothSetServiceState(HFP) failed, error={hfpResult}");

            return a2dpResult == 0 || hfpResult == 0;
        }
        finally
        {
            BluetoothNative.BluetoothFindRadioClose(radioFind);
            BluetoothNative.CloseHandle(radioHandle);
        }
    }

    // ─── BluetoothSetServiceState disconnect ────────────────────────────────────

    private static async Task<bool> DisconnectViaSetServiceStateAsync(string deviceId)
    {
        ulong address;
        try
        {
            using var btDevice = await BluetoothDevice.FromIdAsync(deviceId);
            if (btDevice is null) return false;
            address = btDevice.BluetoothAddress;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Could not resolve BT address: {ex.Message}");
            return false;
        }

        return await Task.Run(() => SendDisableServiceState(address));
    }

    private static bool SendDisableServiceState(ulong bluetoothAddress)
    {
        var radioParams = new BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        var radioFind = BluetoothNative.BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFind == IntPtr.Zero) return false;

        try
        {
            var deviceInfo = new BluetoothNative.BLUETOOTH_DEVICE_INFO
            {
                dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_DEVICE_INFO>(),
                Address = new BluetoothNative.BLUETOOTH_ADDRESS { ullLong = bluetoothAddress },
                szName = string.Empty,
            };

            var a2dp = BluetoothNative.A2dpSinkService;
            var a2dpResult = BluetoothNative.BluetoothSetServiceState(
                radioHandle, ref deviceInfo, ref a2dp, BluetoothNative.BLUETOOTH_SERVICE_DISABLE);
            if (a2dpResult != 0)
                Trace.WriteLine($"[Harbor] BluetoothService: BluetoothSetServiceState DISABLE(A2DP) failed, error={a2dpResult}");

            var hfp = BluetoothNative.HandsFreeService;
            var hfpResult = BluetoothNative.BluetoothSetServiceState(
                radioHandle, ref deviceInfo, ref hfp, BluetoothNative.BLUETOOTH_SERVICE_DISABLE);
            if (hfpResult != 0)
                Trace.WriteLine($"[Harbor] BluetoothService: BluetoothSetServiceState DISABLE(HFP) failed, error={hfpResult}");

            return a2dpResult == 0 || hfpResult == 0;
        }
        finally
        {
            BluetoothNative.BluetoothFindRadioClose(radioFind);
            BluetoothNative.CloseHandle(radioHandle);
        }
    }

    // ─── Local discoverability ────────────────────────────────────────────────

    public void EnableLocalDiscovery() => _ = Task.Run(() => SetLocalDiscoverability(true));

    public void DisableLocalDiscovery() => _ = Task.Run(() => SetLocalDiscoverability(false));

    private static void SetLocalDiscoverability(bool enabled)
    {
        var radioParams = new BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        var radioFind = BluetoothNative.BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFind == IntPtr.Zero)
        {
            Trace.WriteLine("[Harbor] BluetoothService: BluetoothEnableDiscovery — no radio found.");
            return;
        }

        try
        {
            var result = BluetoothNative.BluetoothEnableDiscovery(radioHandle, enabled);
            Trace.WriteLine($"[Harbor] BluetoothService: BluetoothEnableDiscovery({enabled}) = {result}");
        }
        finally
        {
            BluetoothNative.BluetoothFindRadioClose(radioFind);
            BluetoothNative.CloseHandle(radioHandle);
        }
    }

    // ─── IOCTL fallback disconnect ────────────────────────────────────────────

    private static async Task<bool> DisconnectViaIoctlAsync(string deviceId)
    {
        ulong address;
        try
        {
            using var btDevice = await BluetoothDevice.FromIdAsync(deviceId);
            if (btDevice is null) return false;
            address = btDevice.BluetoothAddress;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothService: Could not resolve BT address: {ex.Message}");
            return false;
        }

        return await Task.Run(() => SendDisconnectIoctl(address));
    }

    private static bool SendDisconnectIoctl(ulong bluetoothAddress)
    {
        var radioParams = new BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS
        {
            dwSize = (uint)Marshal.SizeOf<BluetoothNative.BLUETOOTH_FIND_RADIO_PARAMS>()
        };

        var radioFind = BluetoothNative.BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFind == IntPtr.Zero) return false;

        try
        {
            var ok = BluetoothNative.DeviceIoControl(
                radioHandle,
                BluetoothNative.IOCTL_BTH_DISCONNECT_DEVICE,
                ref bluetoothAddress,
                8,
                IntPtr.Zero, 0,
                out _,
                IntPtr.Zero);

            if (!ok)
                Trace.WriteLine($"[Harbor] BluetoothService: IOCTL_BTH_DISCONNECT_DEVICE failed, error={Marshal.GetLastWin32Error()}");

            return ok;
        }
        finally
        {
            BluetoothNative.BluetoothFindRadioClose(radioFind);
            BluetoothNative.CloseHandle(radioHandle);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void UpdateIconState()
    {
        if (!IsAvailable || !IsEnabled) IconState = BluetoothIconState.Off;
        else if (ConnectedDeviceCount > 0) IconState = BluetoothIconState.Connected;
        else IconState = BluetoothIconState.On;
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

    // ─── Disposal ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopDiscovery();

        if (_bluetoothRadio is not null)
        {
            _bluetoothRadio.StateChanged -= OnRadioStateChanged;
            _bluetoothRadio = null;
        }

        if (_deviceWatcher is not null)
        {
            try
            {
                if (_deviceWatcher.Status is DeviceWatcherStatus.Started
                    or DeviceWatcherStatus.EnumerationCompleted)
                    _deviceWatcher.Stop();
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
            _nearbyDevices.Clear();
        }

        Trace.WriteLine("[Harbor] BluetoothService: Disposed.");
    }
}
