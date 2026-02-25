using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Harbor.Core.Services;

public enum NetworkConnectionType
{
    None,
    WiFi,
    Ethernet,
    Unknown,
}

public enum WiFiSignalStrength
{
    None,
    Weak,
    Fair,
    Good,
    Excellent,
}

public enum NetworkIconState
{
    Disconnected,
    Ethernet,
    WiFiWeak,
    WiFiFair,
    WiFiGood,
    WiFiExcellent,
}

public sealed class WiFiNetworkInfo
{
    public string Ssid { get; init; } = "";
    public WiFiSignalStrength SignalStrength { get; init; }
    public int SignalQuality { get; init; }
    public bool IsSecured { get; init; }
    public bool IsConnected { get; init; }
}

public sealed class NetworkChangedEventArgs : EventArgs
{
    public bool IsConnected { get; init; }
    public NetworkConnectionType ConnectionType { get; init; }
    public string? WiFiNetworkName { get; init; }
    public WiFiSignalStrength SignalStrength { get; init; }
    public NetworkIconState IconState { get; init; }
}

public sealed class NetworkService : IDisposable
{
    private readonly object _lock = new();
    private Timer? _signalPollTimer;
    private IntPtr _wlanHandle;
    private bool _disposed;
    private List<WiFiNetworkInfo> _availableNetworks = new();

    public bool IsConnected { get; private set; }
    public NetworkConnectionType ConnectionType { get; private set; }
    public string? WiFiNetworkName { get; private set; }
    public WiFiSignalStrength SignalStrength { get; private set; }
    public NetworkIconState IconState { get; private set; }
    public IReadOnlyList<WiFiNetworkInfo> AvailableNetworks
    {
        get
        {
            lock (_lock) { return _availableNetworks.ToList(); }
        }
    }

    public event EventHandler<NetworkChangedEventArgs>? NetworkChanged;
    public event EventHandler? AvailableNetworksChanged;

    public NetworkService()
    {
        // Open WLAN handle
        try
        {
            int negotiatedVersion;
            var result = WlanOpenHandle(2, IntPtr.Zero, out negotiatedVersion, out _wlanHandle);
            if (result != 0)
            {
                _wlanHandle = IntPtr.Zero;
                Trace.WriteLine($"[Harbor] NetworkService: WlanOpenHandle failed with error {result}.");
            }
        }
        catch (Exception ex)
        {
            _wlanHandle = IntPtr.Zero;
            Trace.WriteLine($"[Harbor] NetworkService: WLAN API unavailable: {ex.Message}");
        }

        // Subscribe to network change events (push)
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        // Initial state
        UpdateNetworkState();

        // Poll Wi-Fi signal strength every 10 seconds
        _signalPollTimer = new Timer(_ => UpdateNetworkState(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        Trace.WriteLine("[Harbor] NetworkService: Initialized.");
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        UpdateNetworkState();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        UpdateNetworkState();
    }

    private void UpdateNetworkState()
    {
        if (_disposed) return;

        bool isConnected;
        var connectionType = NetworkConnectionType.None;
        string? wifiName = null;
        int signalQuality = 0;

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Find the best active interface
            NetworkInterface? activeWifi = null;
            NetworkInterface? activeEthernet = null;

            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                // Skip virtual/VPN adapters
                var desc = ni.Description.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vpn") || desc.Contains("pseudo"))
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    activeWifi ??= ni;
                else if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet)
                    activeEthernet ??= ni;
            }

            if (activeWifi is not null)
            {
                connectionType = NetworkConnectionType.WiFi;
                isConnected = true;

                // Get Wi-Fi details from WLAN API
                if (_wlanHandle != IntPtr.Zero)
                {
                    (wifiName, signalQuality) = GetCurrentWiFiInfo();
                }
            }
            else if (activeEthernet is not null)
            {
                connectionType = NetworkConnectionType.Ethernet;
                isConnected = true;
            }
            else
            {
                isConnected = NetworkInterface.GetIsNetworkAvailable();
                connectionType = isConnected ? NetworkConnectionType.Unknown : NetworkConnectionType.None;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] NetworkService: Failed to query network state: {ex.Message}");
            isConnected = false;
            connectionType = NetworkConnectionType.None;
        }

        var strength = ClassifySignalStrength(signalQuality);
        var iconState = DetermineIconState(connectionType, strength);

        bool stateChanged;
        lock (_lock)
        {
            stateChanged = IsConnected != isConnected ||
                           ConnectionType != connectionType ||
                           WiFiNetworkName != wifiName ||
                           SignalStrength != strength;

            IsConnected = isConnected;
            ConnectionType = connectionType;
            WiFiNetworkName = wifiName;
            SignalStrength = strength;
            IconState = iconState;
        }

        if (stateChanged)
        {
            RaiseNetworkChanged();
        }

        // Update available networks (only when connected to Wi-Fi)
        if (connectionType == NetworkConnectionType.WiFi && _wlanHandle != IntPtr.Zero)
        {
            UpdateAvailableNetworks();
        }
    }

    private (string? ssid, int signalQuality) GetCurrentWiFiInfo()
    {
        IntPtr interfaceList = IntPtr.Zero;
        try
        {
            var result = WlanEnumInterfaces(_wlanHandle, IntPtr.Zero, out interfaceList);
            if (result != 0 || interfaceList == IntPtr.Zero)
                return (null, 0);

            // Read interface count
            int count = Marshal.ReadInt32(interfaceList);
            if (count == 0)
                return (null, 0);

            // First WLAN_INTERFACE_INFO starts at offset 8 (4 bytes count + 4 bytes padding)
            var interfaceInfoPtr = interfaceList + 8;
            // WLAN_INTERFACE_INFO: 16 bytes GUID + 512 bytes description (256 chars) + 4 bytes state
            var interfaceGuid = Marshal.PtrToStructure<Guid>(interfaceInfoPtr);

            // Query current connection
            IntPtr dataPtr;
            int dataSize;
            var opcode = 0; // unused
            result = WlanQueryInterface(
                _wlanHandle,
                ref interfaceGuid,
                7, // wlan_intf_opcode_current_connection
                IntPtr.Zero,
                out dataSize,
                out dataPtr,
                out opcode);

            if (result != 0 || dataPtr == IntPtr.Zero)
                return (null, 0);

            try
            {
                // WLAN_CONNECTION_ATTRIBUTES structure:
                // offset 0: WLAN_INTERFACE_STATE (4 bytes)
                // offset 4: WLAN_CONNECTION_MODE (4 bytes)
                // offset 8: profile name (512 bytes = 256 wchars)
                // offset 520: WLAN_ASSOCIATION_ATTRIBUTES
                //   offset 520: DOT11_SSID
                //     offset 520: ULONG length (4 bytes)
                //     offset 524: UCHAR[32] ssid
                //   offset 556: DOT11_BSS_TYPE (4 bytes)
                //   offset 560: DOT11_MAC_ADDRESS (6 bytes)
                //   offset 566: padding (2 bytes)
                //   offset 568: DOT11_PHY_TYPE (4 bytes)
                //   offset 572: ULONG phyIndex (4 bytes)
                //   offset 576: WLAN_SIGNAL_QUALITY (4 bytes) <-- signal quality 0-100

                var ssidLength = Marshal.ReadInt32(dataPtr, 520);
                var ssidBytes = new byte[ssidLength];
                Marshal.Copy(dataPtr + 524, ssidBytes, 0, ssidLength);
                var ssid = System.Text.Encoding.UTF8.GetString(ssidBytes);

                var quality = Marshal.ReadInt32(dataPtr, 576);

                return (ssid, quality);
            }
            finally
            {
                WlanFreeMemory(dataPtr);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] NetworkService: Failed to get Wi-Fi info: {ex.Message}");
            return (null, 0);
        }
        finally
        {
            if (interfaceList != IntPtr.Zero)
                WlanFreeMemory(interfaceList);
        }
    }

    private void UpdateAvailableNetworks()
    {
        IntPtr interfaceList = IntPtr.Zero;
        try
        {
            var result = WlanEnumInterfaces(_wlanHandle, IntPtr.Zero, out interfaceList);
            if (result != 0 || interfaceList == IntPtr.Zero) return;

            int count = Marshal.ReadInt32(interfaceList);
            if (count == 0) return;

            var interfaceInfoPtr = interfaceList + 8;
            var interfaceGuid = Marshal.PtrToStructure<Guid>(interfaceInfoPtr);

            IntPtr networkListPtr;
            result = WlanGetAvailableNetworkList(
                _wlanHandle,
                ref interfaceGuid,
                0, // flags
                IntPtr.Zero,
                out networkListPtr);

            if (result != 0 || networkListPtr == IntPtr.Zero) return;

            try
            {
                // WLAN_AVAILABLE_NETWORK_LIST:
                // offset 0: DWORD NumberOfItems
                // offset 4: DWORD Index (always 0)
                // offset 8: WLAN_AVAILABLE_NETWORK[NumberOfItems]
                var networkCount = Marshal.ReadInt32(networkListPtr);
                var networks = new List<WiFiNetworkInfo>();

                // Each WLAN_AVAILABLE_NETWORK is 628 bytes
                const int networkSize = 628;
                var baseOffset = 8;

                for (int i = 0; i < networkCount && i < 20; i++) // Cap at 20
                {
                    var netPtr = networkListPtr + baseOffset + (i * networkSize);

                    // DOT11_SSID at offset 0:
                    //   ULONG length (4 bytes)
                    //   UCHAR[32] ssid
                    var ssidLen = Marshal.ReadInt32(netPtr);
                    string ssid;
                    if (ssidLen > 0 && ssidLen <= 32)
                    {
                        var ssidBytes = new byte[ssidLen];
                        Marshal.Copy(netPtr + 4, ssidBytes, 0, ssidLen);
                        ssid = System.Text.Encoding.UTF8.GetString(ssidBytes);
                    }
                    else
                    {
                        continue; // Skip hidden networks
                    }

                    // Skip duplicate SSIDs (keep strongest)
                    if (networks.Any(n => n.Ssid == ssid))
                        continue;

                    // Flags at offset 36 (DWORD)
                    // Signal quality at offset 284 (WLAN_SIGNAL_QUALITY, ULONG, 0-100)
                    // Security enabled at offset 296 (BOOL)
                    var quality = Marshal.ReadInt32(netPtr, 284);
                    var securityEnabled = Marshal.ReadInt32(netPtr, 296) != 0;
                    var flags = Marshal.ReadInt32(netPtr, 36);
                    var isCurrentlyConnected = (flags & 1) != 0; // WLAN_AVAILABLE_NETWORK_CONNECTED

                    networks.Add(new WiFiNetworkInfo
                    {
                        Ssid = ssid,
                        SignalQuality = quality,
                        SignalStrength = ClassifySignalStrength(quality),
                        IsSecured = securityEnabled,
                        IsConnected = isCurrentlyConnected,
                    });
                }

                // Sort: connected first, then by signal strength descending
                networks.Sort((a, b) =>
                {
                    if (a.IsConnected != b.IsConnected)
                        return a.IsConnected ? -1 : 1;
                    return b.SignalQuality.CompareTo(a.SignalQuality);
                });

                bool changed;
                lock (_lock)
                {
                    changed = !NetworkListsEqual(_availableNetworks, networks);
                    if (changed)
                        _availableNetworks = networks;
                }

                if (changed)
                    AvailableNetworksChanged?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                WlanFreeMemory(networkListPtr);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] NetworkService: Failed to scan available networks: {ex.Message}");
        }
        finally
        {
            if (interfaceList != IntPtr.Zero)
                WlanFreeMemory(interfaceList);
        }
    }

    private static bool NetworkListsEqual(List<WiFiNetworkInfo> a, List<WiFiNetworkInfo> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Ssid != b[i].Ssid || a[i].SignalStrength != b[i].SignalStrength ||
                a[i].IsConnected != b[i].IsConnected)
                return false;
        }
        return true;
    }

    private static WiFiSignalStrength ClassifySignalStrength(int quality)
    {
        return quality switch
        {
            0 => WiFiSignalStrength.None,
            <= 25 => WiFiSignalStrength.Weak,
            <= 50 => WiFiSignalStrength.Fair,
            <= 75 => WiFiSignalStrength.Good,
            _ => WiFiSignalStrength.Excellent,
        };
    }

    private static NetworkIconState DetermineIconState(NetworkConnectionType type, WiFiSignalStrength strength)
    {
        return type switch
        {
            NetworkConnectionType.Ethernet => NetworkIconState.Ethernet,
            NetworkConnectionType.WiFi => strength switch
            {
                WiFiSignalStrength.Weak => NetworkIconState.WiFiWeak,
                WiFiSignalStrength.Fair => NetworkIconState.WiFiFair,
                WiFiSignalStrength.Good => NetworkIconState.WiFiGood,
                WiFiSignalStrength.Excellent => NetworkIconState.WiFiExcellent,
                _ => NetworkIconState.Disconnected,
            },
            NetworkConnectionType.None => NetworkIconState.Disconnected,
            _ => NetworkIconState.Disconnected,
        };
    }

    private void RaiseNetworkChanged()
    {
        NetworkChanged?.Invoke(this, new NetworkChangedEventArgs
        {
            IsConnected = IsConnected,
            ConnectionType = ConnectionType,
            WiFiNetworkName = WiFiNetworkName,
            SignalStrength = SignalStrength,
            IconState = IconState,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

        _signalPollTimer?.Dispose();
        _signalPollTimer = null;

        if (_wlanHandle != IntPtr.Zero)
        {
            WlanCloseHandle(_wlanHandle, IntPtr.Zero);
            _wlanHandle = IntPtr.Zero;
        }

        Trace.WriteLine("[Harbor] NetworkService: Disposed.");
    }

    #region Native WiFi API P/Invoke

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanOpenHandle(
        int clientVersion,
        IntPtr reserved,
        out int negotiatedVersion,
        out IntPtr clientHandle);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanCloseHandle(
        IntPtr clientHandle,
        IntPtr reserved);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanEnumInterfaces(
        IntPtr clientHandle,
        IntPtr reserved,
        out IntPtr interfaceList);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanQueryInterface(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        int opCode,
        IntPtr reserved,
        out int dataSize,
        out IntPtr data,
        out int opcodeValueType);

    [DllImport("wlanapi.dll", SetLastError = true)]
    private static extern int WlanGetAvailableNetworkList(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        int flags,
        IntPtr reserved,
        out IntPtr availableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);

    #endregion
}
