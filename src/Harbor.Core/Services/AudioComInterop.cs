using System.Runtime.InteropServices;

namespace Harbor.Core.Services;

// ─── Structs ──────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal struct KSPROPERTY
{
    public Guid Set;    // Property set GUID (e.g. KSPROPSETID_BtAudio)
    public uint Id;     // Property ID within the set
    public uint Flags;  // KSPROPERTY_TYPE_GET = 1, KSPROPERTY_TYPE_SET = 2
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

/// <summary>
/// Simplified PROPVARIANT that covers the VT_LPWSTR case used for device
/// friendly names. Always call <see cref="AudioComInterop.PropVariantClear"/>
/// after use to free the native string buffer.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(8)] public IntPtr pwszVal;

    internal string? GetString()
    {
        const ushort VT_LPWSTR = 31;
        return vt == VT_LPWSTR && pwszVal != IntPtr.Zero
            ? Marshal.PtrToStringUni(pwszVal)
            : null;
    }
}

// ─── Constants ────────────────────────────────────────────────────────────────

internal static class AudioComInterop
{
    /// <summary>KSPROPSETID_BtAudio — Bluetooth audio one-shot operations.</summary>
    internal static readonly Guid KsPropsetIdBtAudio = new("A7AA577D-9A2C-4F96-B6A7-C40E22F55D98");

    /// <summary>KSPROPERTY_ONESHOT_RECONNECT — triggers BT audio connection.</summary>
    internal const uint OneshotReconnect = 1;

    /// <summary>KSPROPERTY_ONESHOT_DISCONNECT — triggers BT audio disconnection.</summary>
    internal const uint OneshotDisconnect = 2;

    internal const uint KsPropertyTypeGet = 1;

    // EDataFlow: eAll = 2 (enumerates render + capture endpoints)
    internal const int DataFlowAll = 2;

    // DEVICE_STATE_ACTIVE (0x1) | DEVICE_STATE_NOTPRESENT (0x4) | DEVICE_STATE_UNPLUGGED (0x8).
    // Note: 0x2 = DISABLED (not UNPLUGGED). Paired-but-disconnected BT devices appear as UNPLUGGED
    // or NOTPRESENT depending on the driver, so both flags are required for reconnect to work.
    internal const uint DeviceStateActiveUnplugged = 0x0000000D;

    internal const uint StgmRead = 0;
    internal const uint ClsctxAll = 0x17;

    // PKEY_Device_FriendlyName: {A45C254E-DF1C-4EFD-8020-67D146A850E0}, pid=14
    internal static readonly PROPERTYKEY FriendlyNameKey = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14,
    };

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PROPVARIANT pvar);
}

// ─── COM class ────────────────────────────────────────────────────────────────

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject { }

// ─── COM interfaces ───────────────────────────────────────────────────────────

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint pcDevices);
    [PreserveSig] int Item(uint nDevice, out IMMDevice ppDevice);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    [PreserveSig] int GetState(out uint pdwState);
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint cProps);
    [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
    [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);
    [PreserveSig] int Commit();
}

/// <summary>
/// Represents the audio topology of a device endpoint. Used to navigate to the
/// kernel streaming filter that implements IKsControl for BT audio operations.
/// </summary>
[ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig] int GetConnectorCount(out uint pCount);
    [PreserveSig] int GetConnector(uint nIndex, out IConnector ppConnector);
    [PreserveSig] int GetSubunitCount(out uint pCount);
    [PreserveSig] int GetSubunit(uint nIndex, out IntPtr ppSubunit);
    [PreserveSig] int GetPartById(uint nId, out IntPtr ppPart);
    [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string ppwstrDeviceId);
    [PreserveSig] int GetSignalPath(IntPtr pIPartFrom, IntPtr pIPartTo,
        bool bRejectMixedPaths, out IntPtr ppParts);
}

[ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig] int GetType(out int pType);
    [PreserveSig] int GetDataFlow(out int pFlow);
    [PreserveSig] int ConnectTo(IConnector pConnectTo);
    [PreserveSig] int Disconnect();
    [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool pbConnected);
    [PreserveSig] int GetConnectedTo(out IConnector ppConTo);
    [PreserveSig] int GetConnectorIdConnectedTo(
        [MarshalAs(UnmanagedType.LPWStr)] out string ppwstrDeviceId, out uint pnId);
    [PreserveSig] int GetDeviceIdConnectedTo(
        [MarshalAs(UnmanagedType.LPWStr)] out string ppwstrDeviceId);
}

/// <summary>
/// Kernel streaming control interface. Implemented by BT audio filters
/// (btha2dp.sys, bthhfenum.sys) and used to send KSPROPERTY_ONESHOT_RECONNECT
/// and KSPROPERTY_ONESHOT_DISCONNECT.
/// </summary>
[ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsControl
{
    [PreserveSig]
    int KsProperty(ref KSPROPERTY Property, uint PropertyLength,
        IntPtr PropertyData, uint DataLength, out uint BytesReturned);

    [PreserveSig]
    int KsMethod(ref KSPROPERTY Method, uint MethodLength,
        IntPtr MethodData, uint DataLength, out uint BytesReturned);

    [PreserveSig]
    int KsEvent(ref KSPROPERTY Event, uint EventLength,
        IntPtr EventData, uint DataLength, out uint BytesReturned);
}
