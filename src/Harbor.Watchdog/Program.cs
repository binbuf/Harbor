using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Watchdog;

/// <summary>
/// Lightweight watchdog process that monitors Harbor's heartbeat and executes
/// crash recovery if the main process becomes unresponsive.
///
/// Deliberately standalone — no reference to Harbor.Core — so it remains
/// functional even if the main .NET runtime has faulted.
/// </summary>
internal static class Program
{
    // Must match constants in Harbor.Core.Services.HiddenWindowRegistry
    private const string HeartbeatMmfName = "Harbor_Heartbeat";
    private const string HiddenWindowMmfName = "Harbor_HiddenWindowRegistry";
    private const int HiddenWindowHeaderSize = 4;
    private const int HiddenWindowEntrySize = 8;
    private const int HiddenWindowMaxEntries = 512;
    private const int HiddenWindowTotalSize = HiddenWindowHeaderSize + (HiddenWindowMaxEntries * HiddenWindowEntrySize);

    // Monitoring parameters
    private const int HeartbeatCheckIntervalMs = 500;
    private const int MissedHeartbeatThreshold = 3; // 1.5 seconds at 500ms intervals
    private const double HeartbeatTimeoutMs = 1500.0;

    private static int Main(string[] args)
    {
        Console.WriteLine("[harbor-watchdog] Starting watchdog process...");

        // Parse optional parent PID from arguments
        int parentPid = 0;
        if (args.Length > 0 && int.TryParse(args[0], out var pid))
        {
            parentPid = pid;
            Console.WriteLine($"[harbor-watchdog] Monitoring parent process PID {parentPid}.");
        }

        try
        {
            MonitorHeartbeat(parentPid);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harbor-watchdog] Fatal error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static void MonitorHeartbeat(int parentPid)
    {
        int consecutiveMisses = 0;

        // Wait for the heartbeat MMF to become available
        MemoryMappedFile? heartbeatMmf = null;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                heartbeatMmf = MemoryMappedFile.OpenExisting(HeartbeatMmfName);
                break;
            }
            catch (FileNotFoundException)
            {
                Thread.Sleep(500);
            }
        }

        if (heartbeatMmf is null)
        {
            Console.Error.WriteLine("[harbor-watchdog] Could not open heartbeat MMF after 15 seconds. Exiting.");
            return;
        }

        using (heartbeatMmf)
        using (var accessor = heartbeatMmf.CreateViewAccessor(0, 8, MemoryMappedFileAccess.Read))
        {
            Console.WriteLine("[harbor-watchdog] Heartbeat connection established. Monitoring...");

            while (true)
            {
                Thread.Sleep(HeartbeatCheckIntervalMs);

                // If parent process exited cleanly, stop monitoring
                if (parentPid > 0 && HasProcessExited(parentPid))
                {
                    Console.WriteLine("[harbor-watchdog] Parent process exited. Checking for orphaned hidden windows...");
                    // Check if there are hidden windows that need recovery
                    int hiddenCount = GetHiddenWindowCount();
                    if (hiddenCount > 0)
                    {
                        Console.WriteLine($"[harbor-watchdog] Found {hiddenCount} orphaned hidden window(s). Executing recovery.");
                        ExecuteRecovery();
                    }
                    else
                    {
                        Console.WriteLine("[harbor-watchdog] No orphaned hidden windows. Clean exit.");
                    }
                    return;
                }

                long lastHeartbeat = accessor.ReadInt64(0);
                double elapsedMs = GetElapsedMs(lastHeartbeat);

                if (elapsedMs > HeartbeatTimeoutMs)
                {
                    consecutiveMisses++;
                    Console.WriteLine($"[harbor-watchdog] Missed heartbeat #{consecutiveMisses} (elapsed: {elapsedMs:F0}ms).");

                    if (consecutiveMisses >= MissedHeartbeatThreshold)
                    {
                        Console.WriteLine("[harbor-watchdog] Heartbeat timeout! Executing crash recovery...");
                        ExecuteRecovery();
                        return;
                    }
                }
                else
                {
                    if (consecutiveMisses > 0)
                    {
                        Console.WriteLine("[harbor-watchdog] Heartbeat resumed.");
                    }
                    consecutiveMisses = 0;
                }
            }
        }
    }

    private static void ExecuteRecovery()
    {
        Console.WriteLine("[harbor-watchdog] === RECOVERY SEQUENCE START ===");

        // Step 1: Re-show hidden windows
        try
        {
            ReShowHiddenWindows();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harbor-watchdog] Failed to re-show windows: {ex.Message}");
        }

        // Step 2: Restore native animations
        try
        {
            RestoreNativeAnimations();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harbor-watchdog] Failed to restore animations: {ex.Message}");
        }

        // Step 3: Launch explorer.exe
        try
        {
            LaunchExplorer();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harbor-watchdog] Failed to launch explorer: {ex.Message}");
        }

        // Step 4: Write crash dump
        try
        {
            WriteCrashDump();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[harbor-watchdog] Failed to write crash dump: {ex.Message}");
        }

        Console.WriteLine("[harbor-watchdog] === RECOVERY SEQUENCE COMPLETE ===");
    }

    private static void ReShowHiddenWindows()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(HiddenWindowMmfName);
            using var accessor = mmf.CreateViewAccessor(0, HiddenWindowTotalSize, MemoryMappedFileAccess.ReadWrite);

            int count = accessor.ReadInt32(0);
            Console.WriteLine($"[harbor-watchdog] Found {count} hidden window(s) in registry.");

            int restored = 0;
            for (int i = 0; i < count; i++)
            {
                long offset = HiddenWindowHeaderSize + (i * HiddenWindowEntrySize);
                nint hwndValue = (nint)accessor.ReadInt64(offset);
                var hwnd = new HWND(hwndValue);

                if (PInvoke.IsWindow(hwnd))
                {
                    PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOW);
                    restored++;
                    Console.WriteLine($"[harbor-watchdog] Restored HWND {hwndValue}.");
                }
                else
                {
                    Console.WriteLine($"[harbor-watchdog] Skipped stale HWND {hwndValue}.");
                }
            }

            // Clear the registry
            accessor.Write(0, 0);
            accessor.Flush();

            Console.WriteLine($"[harbor-watchdog] Restored {restored}/{count} window(s).");
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine("[harbor-watchdog] No hidden window registry found.");
        }
    }

    private static unsafe void RestoreNativeAnimations()
    {
        BOOL enabled = true;
        PInvoke.SystemParametersInfo(
            SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETCLIENTAREAANIMATION,
            0,
            &enabled,
            SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
        Console.WriteLine("[harbor-watchdog] Native animations restored.");
    }

    private static void LaunchExplorer()
    {
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        if (File.Exists(explorerPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = explorerPath,
                UseShellExecute = false,
            });
            Console.WriteLine("[harbor-watchdog] Launched explorer.exe as fallback shell.");
        }
        else
        {
            Console.Error.WriteLine("[harbor-watchdog] explorer.exe not found!");
        }
    }

    private static void WriteCrashDump()
    {
        var dumpDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Harbor", "CrashDumps");
        Directory.CreateDirectory(dumpDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var dumpPath = Path.Combine(dumpDir, $"crash_{timestamp}_watchdog.txt");

        var content = $"""
            Harbor Crash Report (Watchdog)
            ===============================
            Time (UTC): {DateTime.UtcNow:O}
            Machine: {Environment.MachineName}
            OS: {Environment.OSVersion}
            Cause: Heartbeat timeout detected by watchdog process.
            """;

        File.WriteAllText(dumpPath, content);
        Console.WriteLine($"[harbor-watchdog] Crash dump written to {dumpPath}.");
    }

    private static int GetHiddenWindowCount()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(HiddenWindowMmfName);
            using var accessor = mmf.CreateViewAccessor(0, HiddenWindowHeaderSize, MemoryMappedFileAccess.Read);
            return accessor.ReadInt32(0);
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
    }

    private static bool HasProcessExited(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process no longer exists
            return true;
        }
    }

    private static double GetElapsedMs(long timestamp)
    {
        long now = Stopwatch.GetTimestamp();
        long elapsed = now - timestamp;
        return (double)elapsed / Stopwatch.Frequency * 1000.0;
    }
}
