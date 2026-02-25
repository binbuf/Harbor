using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Harbor.Core.Services;

/// <summary>
/// Enumerates ejectable USB devices and provides eject functionality
/// using WMI and Configuration Manager APIs.
/// </summary>
public sealed class SafeRemoveService : IDisposable
{
    private readonly DispatcherTimer _pollTimer;
    private List<EjectableDevice> _devices = [];
    private bool _disposed;

    public IReadOnlyList<EjectableDevice> EjectableDevices => _devices;
    public bool HasDevices => _devices.Count > 0;

    public event EventHandler? DevicesChanged;

    public SafeRemoveService()
    {
        // Initial scan
        RefreshDevices();

        // Poll every 5 seconds for device changes
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += (_, _) => RefreshDevices();
        _pollTimer.Start();

        Trace.WriteLine("[Harbor] SafeRemoveService: Initialized.");
    }

    private void RefreshDevices()
    {
        try
        {
            var newDevices = EnumerateEjectableDevices();
            var changed = newDevices.Count != _devices.Count ||
                          !newDevices.SequenceEqual(_devices, EjectableDeviceComparer.Instance);

            if (changed)
            {
                _devices = newDevices;
                DevicesChanged?.Invoke(this, EventArgs.Empty);
                Trace.WriteLine($"[Harbor] SafeRemoveService: Device list updated ({_devices.Count} devices).");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] SafeRemoveService: Error refreshing devices: {ex.Message}");
        }
    }

    private static List<EjectableDevice> EnumerateEjectableDevices()
    {
        var devices = new List<EjectableDevice>();

        try
        {
            // Query USB disk drives via WMI
            using var driveSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Model, PNPDeviceID FROM Win32_DiskDrive WHERE InterfaceType='USB'");

            foreach (ManagementObject drive in driveSearcher.Get())
            {
                var model = drive["Model"]?.ToString() ?? "USB Device";
                var pnpDeviceId = drive["PNPDeviceID"]?.ToString();
                var driveDeviceId = drive["DeviceID"]?.ToString();

                if (string.IsNullOrEmpty(pnpDeviceId) || string.IsNullOrEmpty(driveDeviceId))
                    continue;

                // Find drive letters associated with this disk drive
                var driveLetters = GetDriveLettersForDisk(driveDeviceId);
                var driveLetter = driveLetters.Count > 0 ? string.Join(", ", driveLetters) : null;

                var friendlyName = driveLetter is not null
                    ? $"{model} ({driveLetter})"
                    : model;

                devices.Add(new EjectableDevice(friendlyName, driveLetter, pnpDeviceId));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] SafeRemoveService: WMI enumeration error: {ex.Message}");
        }

        return devices;
    }

    private static List<string> GetDriveLettersForDisk(string diskDeviceId)
    {
        var letters = new List<string>();

        try
        {
            // Disk → Partitions
            using var partitionSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{diskDeviceId.Replace("\\", "\\\\")}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

            foreach (ManagementObject partition in partitionSearcher.Get())
            {
                var partitionId = partition["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(partitionId)) continue;

                // Partition → Logical Disks
                using var logicalSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementObject logical in logicalSearcher.Get())
                {
                    var letter = logical["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(letter))
                        letters.Add(letter);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] SafeRemoveService: Error getting drive letters: {ex.Message}");
        }

        return letters;
    }

    /// <summary>
    /// Ejects the specified device. Returns true on success.
    /// </summary>
    public bool EjectDevice(EjectableDevice device)
    {
        try
        {
            Trace.WriteLine($"[Harbor] SafeRemoveService: Ejecting '{device.Name}' (ID: {device.DeviceInstanceId})...");

            var result = CM_Locate_DevNode(out uint devInst, device.DeviceInstanceId, 0);
            if (result != 0)
            {
                Trace.WriteLine($"[Harbor] SafeRemoveService: CM_Locate_DevNode failed: {result}");
                return false;
            }

            // Get the parent device (USB hub port) — ejecting the parent safely removes the whole device
            result = CM_Get_Parent(out uint parentInst, devInst, 0);
            if (result != 0)
            {
                Trace.WriteLine($"[Harbor] SafeRemoveService: CM_Get_Parent failed: {result}");
                return false;
            }

            result = CM_Request_Device_Eject(parentInst, out uint vetoType, IntPtr.Zero, 0, 0);
            if (result != 0 || vetoType != 0)
            {
                Trace.WriteLine($"[Harbor] SafeRemoveService: Eject failed (result={result}, vetoType={vetoType}).");
                return false;
            }

            Trace.WriteLine($"[Harbor] SafeRemoveService: Successfully ejected '{device.Name}'.");

            // Refresh device list immediately
            RefreshDevices();
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] SafeRemoveService: Eject exception: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pollTimer.Stop();
        Trace.WriteLine("[Harbor] SafeRemoveService: Disposed.");
    }

    // Configuration Manager P/Invoke declarations
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNode(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern uint CM_Request_Device_Eject(uint dnDevInst, out uint pVetoType, IntPtr pszVetoName, uint ulNameLength, uint ulFlags);

    private sealed class EjectableDeviceComparer : IEqualityComparer<EjectableDevice>
    {
        public static readonly EjectableDeviceComparer Instance = new();

        public bool Equals(EjectableDevice? x, EjectableDevice? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.DeviceInstanceId == y.DeviceInstanceId && x.DriveLetter == y.DriveLetter;
        }

        public int GetHashCode(EjectableDevice obj) => obj.DeviceInstanceId.GetHashCode();
    }
}

public sealed record EjectableDevice(string Name, string? DriveLetter, string DeviceInstanceId);
