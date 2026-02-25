using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Harbor.Core.Services;

/// <summary>
/// Controls visibility of desktop icons (e.g. Recycle Bin) via registry
/// and notifies the shell to refresh.
/// </summary>
public class DesktopIconService : IDisposable
{
    private const string RegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    /// <summary>CLSID for the Recycle Bin desktop icon.</summary>
    private const string RecycleBinClsid = "{645FF040-5081-101B-9F08-00AA002F954E}";

    private bool _disposed;
    private bool _modified;

    /// <summary>
    /// Sets the Recycle Bin desktop icon visibility.
    /// </summary>
    /// <param name="hidden">true to hide, false to show.</param>
    public void SetRecycleBinHidden(bool hidden)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            if (key is null) return;

            key.SetValue(RecycleBinClsid, hidden ? 1 : 0, RegistryValueKind.DWord);
            _modified = true;
            RefreshDesktop();

            Trace.WriteLine($"[Harbor] DesktopIconService: Recycle Bin hidden={hidden}.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] DesktopIconService: Failed to set Recycle Bin visibility: {ex.Message}");
        }
    }

    /// <summary>
    /// Shows the Recycle Bin desktop icon unconditionally.
    /// Always sets the registry to "visible" rather than restoring a previous value,
    /// which could be stale from a prior crash.
    /// Uses synchronous notification (SHCNF_FLUSH) to ensure explorer processes
    /// the change before the calling process exits.
    /// </summary>
    public void Restore()
    {
        if (!_modified) return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            if (key is null) return;

            // Always delete the entry so the Recycle Bin is visible.
            // This avoids the stale-value problem where a previous crash
            // left the registry set to hidden (1), causing Restore to
            // perpetually "restore" to hidden.
            key.DeleteValue(RecycleBinClsid, throwOnMissingValue: false);

            RefreshDesktopSync();
            Trace.WriteLine("[Harbor] DesktopIconService: Restored Recycle Bin to visible.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] DesktopIconService: Failed to restore Recycle Bin: {ex.Message}");
        }
    }

    /// <summary>
    /// Notifies the shell that an association has changed, forcing it to
    /// re-read desktop icon visibility from the registry.
    /// Uses async delivery — suitable for runtime changes.
    /// </summary>
    private static void RefreshDesktop()
    {
        // SHCNE_ASSOCCHANGED (0x08000000) with SHCNF_IDLIST (0x0000)
        // forces Explorer to re-enumerate desktop icons.
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Synchronous variant of <see cref="RefreshDesktop"/> that blocks until
    /// explorer has processed the notification. Used during shutdown so the
    /// process does not exit before the change is picked up.
    /// </summary>
    private static void RefreshDesktopSync()
    {
        // SHCNE_ASSOCCHANGED (0x08000000) with SHCNF_FLUSH (0x1000)
        // blocks until all listeners have processed the notification.
        SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Restore();
        GC.SuppressFinalize(this);
    }
}
