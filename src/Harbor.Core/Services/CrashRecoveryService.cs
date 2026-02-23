using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Orchestrates the crash recovery sequence: re-shows hidden windows,
/// restores native animations, and launches explorer.exe as fallback.
/// Used both by the main process (on unhandled exceptions) and by the watchdog.
/// </summary>
public static class CrashRecoveryService
{
    private static readonly string CrashDumpDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Harbor", "CrashDumps");

    /// <summary>
    /// Executes the full recovery sequence. Safe to call from any context.
    /// </summary>
    public static void ExecuteRecovery(Exception? exception = null)
    {
        Trace.WriteLine("[Harbor] CrashRecoveryService: Executing recovery sequence...");

        try { ReShowHiddenWindows(); }
        catch (Exception ex) { Trace.WriteLine($"[Harbor] CrashRecoveryService: Failed to re-show windows: {ex.Message}"); }

        try { RestoreNativeAnimations(); }
        catch (Exception ex) { Trace.WriteLine($"[Harbor] CrashRecoveryService: Failed to restore animations: {ex.Message}"); }

        try { RestoreExplorer(); }
        catch (Exception ex) { Trace.WriteLine($"[Harbor] CrashRecoveryService: Failed to restore explorer: {ex.Message}"); }

        try { WriteCrashDump(exception); }
        catch (Exception ex) { Trace.WriteLine($"[Harbor] CrashRecoveryService: Failed to write crash dump: {ex.Message}"); }

        Trace.WriteLine("[Harbor] CrashRecoveryService: Recovery sequence complete.");
    }

    /// <summary>
    /// Re-shows all windows recorded in the Hidden Window Registry.
    /// Skips invalid (stale) HWNDs via IsWindow() check.
    /// </summary>
    public static int ReShowHiddenWindows()
    {
        int restored = 0;

        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(HiddenWindowRegistry.MmfName);
            using var accessor = mmf.CreateViewAccessor(0, HiddenWindowRegistry.TotalSize, MemoryMappedFileAccess.ReadWrite);

            int count = accessor.ReadInt32(0);
            Trace.WriteLine($"[Harbor] CrashRecoveryService: Found {count} hidden window(s) in registry.");

            for (int i = 0; i < count; i++)
            {
                long offset = HiddenWindowRegistry.HeaderSize + (i * HiddenWindowRegistry.EntrySize);
                nint hwndValue = (nint)accessor.ReadInt64(offset);
                var hwnd = new HWND(hwndValue);

                if (WindowInterop.IsWindow(hwnd))
                {
                    WindowInterop.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOW);
                    restored++;
                    Trace.WriteLine($"[Harbor] CrashRecoveryService: Restored HWND {hwndValue}.");
                }
                else
                {
                    Trace.WriteLine($"[Harbor] CrashRecoveryService: Skipped stale HWND {hwndValue}.");
                }
            }

            // Clear the registry after recovery
            accessor.Write(0, 0);
            accessor.Flush();
        }
        catch (FileNotFoundException)
        {
            Trace.WriteLine("[Harbor] CrashRecoveryService: No hidden window registry found.");
        }

        return restored;
    }

    /// <summary>
    /// Restores native Windows client area animations.
    /// </summary>
    public static void RestoreNativeAnimations()
    {
        SystemInterop.SetClientAreaAnimation(true);
        Trace.WriteLine("[Harbor] CrashRecoveryService: Native animations restored.");
    }

    /// <summary>
    /// Restores explorer's hidden UI elements. Falls back to launching explorer.exe
    /// if Shell_TrayWnd is not found (explorer somehow died on its own).
    /// </summary>
    public static void RestoreExplorer()
    {
        var trayWnd = Windows.Win32.PInvoke.FindWindow("Shell_TrayWnd", null);
        if (trayWnd != default)
        {
            ExplorerSuppressionService.RestoreExplorer();
            Trace.WriteLine("[Harbor] CrashRecoveryService: Restored explorer UI.");
        }
        else
        {
            // Explorer somehow died — launch it as fallback
            var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            if (File.Exists(explorerPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    UseShellExecute = true,
                });
                Trace.WriteLine("[Harbor] CrashRecoveryService: Explorer not found, launched explorer.exe as fallback.");
            }
            else
            {
                Trace.WriteLine("[Harbor] CrashRecoveryService: explorer.exe not found!");
            }
        }
    }

    /// <summary>
    /// Writes crash information to %LOCALAPPDATA%\Harbor\CrashDumps\.
    /// </summary>
    public static void WriteCrashDump(Exception? exception)
    {
        Directory.CreateDirectory(CrashDumpDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var dumpPath = Path.Combine(CrashDumpDir, $"crash_{timestamp}.txt");

        var content = $"""
            Harbor Crash Report
            ====================
            Time (UTC): {DateTime.UtcNow:O}
            Machine: {Environment.MachineName}
            OS: {Environment.OSVersion}
            CLR: {Environment.Version}
            Process ID: {Environment.ProcessId}

            Exception:
            {exception?.ToString() ?? "No exception information available."}
            """;

        File.WriteAllText(dumpPath, content);
        Trace.WriteLine($"[Harbor] CrashRecoveryService: Crash dump written to {dumpPath}.");
    }

    /// <summary>
    /// Clears stale Hidden Window Registry entries from a previous session.
    /// Called during safe startup.
    /// </summary>
    public static void ClearStaleRegistry()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(HiddenWindowRegistry.MmfName);
            using var accessor = mmf.CreateViewAccessor(0, HiddenWindowRegistry.TotalSize, MemoryMappedFileAccess.ReadWrite);

            int count = accessor.ReadInt32(0);
            if (count > 0)
            {
                Trace.WriteLine($"[Harbor] CrashRecoveryService: Clearing {count} stale entries from previous session.");

                // Re-show any windows that are still valid before clearing
                for (int i = 0; i < count; i++)
                {
                    long offset = HiddenWindowRegistry.HeaderSize + (i * HiddenWindowRegistry.EntrySize);
                    nint hwndValue = (nint)accessor.ReadInt64(offset);
                    var hwnd = new HWND(hwndValue);

                    if (WindowInterop.IsWindow(hwnd))
                    {
                        WindowInterop.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOW);
                        Trace.WriteLine($"[Harbor] CrashRecoveryService: Restored stale HWND {hwndValue}.");
                    }
                }

                accessor.Write(0, 0);
                accessor.Flush();
            }
        }
        catch (FileNotFoundException)
        {
            // No stale registry — clean startup
        }
    }
}
