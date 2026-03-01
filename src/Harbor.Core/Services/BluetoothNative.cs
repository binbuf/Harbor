using System.Runtime.InteropServices;

namespace Harbor.Core.Services;

/// <summary>
/// Manual P/Invoke declarations for Win32 Bluetooth APIs (BluetoothAPIs.dll).
/// Used to connect and disconnect Bluetooth devices programmatically via
/// BluetoothSetServiceState, which the WinRT API does not expose.
/// </summary>
internal static class BluetoothNative
{
    // Service state flags for BluetoothSetServiceState
    internal const uint BLUETOOTH_SERVICE_ENABLE = 0x00000001;
    internal const uint BLUETOOTH_SERVICE_DISABLE = 0x00000002;

    // A2DP Sink — audio streaming to headphones/speakers (Classic BT)
    internal static readonly Guid A2dpSinkService = new("0000110b-0000-1000-8000-00805f9b34fb");

    // HFP — Hands-Free Profile (used by headsets for call audio)
    internal static readonly Guid HandsFreeService = new("0000111e-0000-1000-8000-00805f9b34fb");

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        internal uint dwSize;
    }

    /// <summary>
    /// BLUETOOTH_ADDRESS union. Only ullLong is used here; the rgBytes[6] overlay
    /// is omitted because C# structs cannot represent partial-size union members
    /// directly. ullLong matches the WinRT BluetoothDevice.BluetoothAddress value.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_ADDRESS
    {
        internal ulong ullLong;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME
    {
        internal short wYear, wMonth, wDayOfWeek, wDay;
        internal short wHour, wMinute, wSecond, wMilliseconds;
    }

    /// <summary>
    /// BLUETOOTH_DEVICE_INFO. Must have dwSize set to Marshal.SizeOf before use.
    /// The 4-byte padding after dwSize is implicit: the CLR aligns BLUETOOTH_ADDRESS
    /// (ulong = 8 bytes) to an 8-byte boundary, matching the C struct layout on x64.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BLUETOOTH_DEVICE_INFO
    {
        internal uint dwSize;
        internal BLUETOOTH_ADDRESS Address;
        internal uint ulClassofDevice;
        internal int fConnected;        // BOOL
        internal int fRemembered;       // BOOL
        internal int fAuthenticated;    // BOOL
        internal SYSTEMTIME stLastSeen;
        internal SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        internal string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        internal uint dwSize;
        internal int fReturnAuthenticated;  // BOOL
        internal int fReturnRemembered;     // BOOL
        internal int fReturnUnknown;        // BOOL
        internal int fReturnConnected;      // BOOL
        internal int fIssueInquiry;         // BOOL
        internal byte cTimeoutMultiplier;
        // 7 bytes padding (CLR aligns IntPtr to 8 bytes on x64)
        internal IntPtr hRadio;             // HANDLE
    }

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    internal static extern IntPtr BluetoothFindFirstRadio(
        ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp,
        out IntPtr phRadio);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    internal static extern IntPtr BluetoothFindFirstDevice(
        ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp,
        ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindNextDevice(
        IntPtr hFind,
        ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    internal static extern uint BluetoothSetServiceState(
        IntPtr hRadio,
        ref BLUETOOTH_DEVICE_INFO pbtdi,
        ref Guid pGuidService,
        uint dwServiceFlags);

    [DllImport("BluetoothAPIs.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BluetoothEnableDiscovery(
        IntPtr hRadio,
        [MarshalAs(UnmanagedType.Bool)] bool fEnabled);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    // ─── IOCTL disconnect (HCI-level, used as fallback) ──────────────────────

    // CTL_CODE(FILE_DEVICE_BLUETOOTH=0x41, 0x03, METHOD_BUFFERED=0, FILE_ANY_ACCESS=0) = 0x0041000C
    internal const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x0041000C;

    /// <summary>
    /// Sends IOCTL_BTH_DISCONNECT_DEVICE to the Bluetooth radio. Input buffer
    /// is the 8-byte BTH_ADDR (ULONGLONG) of the device to disconnect. This
    /// operates at the HCI link layer and disconnects all active profiles.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        ref ulong lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
