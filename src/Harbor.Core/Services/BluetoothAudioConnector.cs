using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Harbor.Core.Services;

/// <summary>
/// Connects and disconnects Bluetooth audio devices using the Windows kernel
/// streaming (KS) audio topology API, following the approach confirmed working
/// by the open-source ToothTray project (github.com/m2jean/ToothTray).
///
/// Flow: IMMDevice → IDeviceTopology → IConnector → IConnector.GetConnectedTo()
///       → QueryInterface for IKsControl → send KSPROPERTY_ONESHOT_RECONNECT
///       or KSPROPERTY_ONESHOT_DISCONNECT (KSPROPSETID_BtAudio).
///
/// Both the A2DP (stereo audio) and HFP (handsfree) BT audio kernel filters
/// support these properties. Because all BT profiles share one ACL link,
/// sending reconnect/disconnect to any matching endpoint brings the device
/// up or down for all audio profiles.
/// </summary>
internal static class BluetoothAudioConnector
{
    public static async Task<bool> ConnectAsync(string deviceName)
    {
        Trace.WriteLine($"[Harbor] BluetoothAudioConnector: Connecting '{deviceName}'...");
        var sent = await Task.Run(() => SendToDevice(deviceName, AudioComInterop.OneshotReconnect));
        Trace.WriteLine($"[Harbor] BluetoothAudioConnector: Connect sent={sent} for '{deviceName}'");
        return sent > 0;
    }

    public static async Task<bool> DisconnectAsync(string deviceName)
    {
        Trace.WriteLine($"[Harbor] BluetoothAudioConnector: Disconnecting '{deviceName}'...");
        var sent = await Task.Run(() => SendToDevice(deviceName, AudioComInterop.OneshotDisconnect));
        Trace.WriteLine($"[Harbor] BluetoothAudioConnector: Disconnect sent={sent} for '{deviceName}'");
        return sent > 0;
    }

    // ─── Core implementation ─────────────────────────────────────────────────

    /// <summary>
    /// Sends a BT audio one-shot property to all audio endpoints whose friendly
    /// name contains <paramref name="deviceName"/>. Returns the number of
    /// endpoints that accepted the property.
    /// </summary>
    private static int SendToDevice(string deviceName, uint propertyId)
    {
        var controls = FindKsControls(deviceName);
        if (controls.Count == 0)
            return 0;

        int sent = 0;
        foreach (var ksControl in controls)
        {
            if (SendKsProperty(ksControl, propertyId))
                sent++;
        }
        return sent;
    }

    /// <summary>
    /// Enumerates all audio endpoints (render + capture, active + unplugged),
    /// finds those whose friendly name contains <paramref name="deviceName"/>,
    /// and navigates each one's topology to retrieve an <see cref="IKsControl"/>.
    /// </summary>
    private static List<IKsControl> FindKsControls(string deviceName)
    {
        var results = new List<IKsControl>();

        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

            // eAll=2 covers both render and capture; active|unplugged covers BT
            // devices that are paired but not currently connected.
            var hr = enumerator.EnumAudioEndpoints(
                AudioComInterop.DataFlowAll,
                AudioComInterop.DeviceStateActiveUnplugged,
                out var collection);

            if (hr != 0)
            {
                Trace.WriteLine($"[Harbor] BluetoothAudioConnector: EnumAudioEndpoints failed hr=0x{hr:X8}");
                return results;
            }

            collection.GetCount(out var count);

            for (uint i = 0; i < count; i++)
            {
                try
                {
                    collection.Item(i, out var device);

                    var name = GetFriendlyName(device);
                    if (name is null)
                        continue;

                    // Partial match: audio endpoints for BT devices are often named
                    // "Sony WH-1000XM5 Stereo" or "Headset (Sony WH-1000XM5)".
                    if (!name.Contains(deviceName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var ksControl = GetKsControl(device);
                    if (ksControl is not null)
                    {
                        Trace.WriteLine($"[Harbor] BluetoothAudioConnector: Found KsControl for endpoint '{name}'");
                        results.Add(ksControl);
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[Harbor] BluetoothAudioConnector: Error processing endpoint {i}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothAudioConnector: Error enumerating endpoints: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Navigates the audio topology from an IMMDevice to the kernel filter's
    /// IKsControl: IMMDevice → IDeviceTopology → IConnector[0] → GetConnectedTo
    /// → QueryInterface for IKsControl.
    /// </summary>
    private static IKsControl? GetKsControl(IMMDevice device)
    {
        var iidTopology = typeof(IDeviceTopology).GUID;
        var hr = device.Activate(ref iidTopology, AudioComInterop.ClsctxAll,
            IntPtr.Zero, out var topologyObj);
        if (hr != 0)
            return null;

        var topology = (IDeviceTopology)topologyObj;

        hr = topology.GetConnector(0, out var connector);
        if (hr != 0)
            return null;

        // GetConnectedTo returns the connector on the hardware (KS filter) side.
        // For BT audio endpoints this is implemented by btha2dp.sys or bthhfenum.sys,
        // which expose IKsControl.
        hr = connector.GetConnectedTo(out var connectedTo);
        if (hr != 0)
            return null;

        return connectedTo as IKsControl;
    }

    /// <summary>
    /// Retrieves the PKEY_Device_FriendlyName from an audio endpoint's property store.
    /// </summary>
    private static string? GetFriendlyName(IMMDevice device)
    {
        try
        {
            var hr = device.OpenPropertyStore(AudioComInterop.StgmRead, out var propStore);
            if (hr != 0)
                return null;

            var key = AudioComInterop.FriendlyNameKey;
            hr = propStore.GetValue(ref key, out var val);
            if (hr != 0)
                return null;

            var name = val.GetString();
            AudioComInterop.PropVariantClear(ref val);
            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sends a KSPROPERTY from KSPROPSETID_BtAudio to the given IKsControl.
    /// One-shot properties (RECONNECT=1, DISCONNECT=2) use KSPROPERTY_TYPE_GET.
    /// Returns true if the property was accepted (S_OK or expected status codes).
    /// </summary>
    private static bool SendKsProperty(IKsControl ksControl, uint propertyId)
    {
        try
        {
            var property = new KSPROPERTY
            {
                Set = AudioComInterop.KsPropsetIdBtAudio,
                Id = propertyId,
                Flags = AudioComInterop.KsPropertyTypeGet,
            };

            var hr = ksControl.KsProperty(
                ref property,
                (uint)Marshal.SizeOf<KSPROPERTY>(),
                IntPtr.Zero,
                0,
                out _);

            // S_OK = 0 — success
            // E_NOTIMPL = 0x80004001 — filter doesn't implement this property (not a BT audio filter)
            if (hr == 0)
            {
                Trace.WriteLine($"[Harbor] BluetoothAudioConnector: KsProperty id={propertyId} accepted");
                return true;
            }

            if ((uint)hr == 0x80004001) // E_NOTIMPL — silently skip non-BT endpoints
                return false;

            Trace.WriteLine($"[Harbor] BluetoothAudioConnector: KsProperty id={propertyId} hr=0x{hr:X8}");
            return false;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BluetoothAudioConnector: KsProperty exception: {ex.Message}");
            return false;
        }
    }
}
