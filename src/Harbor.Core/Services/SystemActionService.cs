using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Harbor.Core.Interop;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Services;

/// <summary>
/// Provides system-level actions for the Windows logo menu (About, Settings, Sleep, Restart, etc.).
/// </summary>
public static class SystemActionService
{
    public static void OpenAboutThisPC()
    {
        LaunchUri("ms-settings:about");
    }

    public static void OpenSystemSettings()
    {
        LaunchUri("ms-settings:");
    }

    public static void OpenAppStore()
    {
        LaunchUri("ms-windows-store:");
    }

    public static void Sleep()
    {
        SetSuspendState(false, true, false);
    }

    public static void Restart()
    {
        Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true });
    }

    public static void ShutDown()
    {
        Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true });
    }

    public static void LockScreen()
    {
        PInvoke.LockWorkStation();
    }

    public static void LogOut()
    {
        // EWX_LOGOFF = 0x00
        PInvoke.ExitWindowsEx(0, 0);
    }

    public static string GetCurrentUserName() => Environment.UserName;

    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Harbor", "launch-diag.log");

    private static void DiagLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* don't let logging failures break anything */ }
    }

    /// <summary>
    /// Launches a protocol URI by briefly restarting explorer.exe to restore the
    /// shell infrastructure, using ShellExecuteEx to activate the URI, then killing
    /// explorer again. All standard URI activation APIs (ShellExecuteEx, COM
    /// IApplicationActivationManager, WinRT Launcher) hang or fail with 0x80040900
    /// when the shell is dead — the only way to activate protocol URIs is to have
    /// a live shell, so we temporarily provide one.
    /// Runs entirely on a background thread to avoid blocking the UI.
    /// </summary>
    private static void LaunchUri(string uri)
    {
        DiagLog($"========== LaunchUri: {uri} ==========");

        Task.Run(() =>
        {
            const uint WM_QUIT = 0x0012;
            try
            {
                // 1. Start explorer.exe — it will become the shell
                DiagLog("[Launch] Starting explorer.exe to restore shell...");
                Process.Start("explorer.exe");

                // 2. Wait for Shell_TrayWnd to appear (= shell is ready)
                DiagLog("[Launch] Waiting for Shell_TrayWnd...");
                Windows.Win32.Foundation.HWND trayWnd = default;
                for (int i = 0; i < 150; i++) // 15s timeout
                {
                    trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
                    if (trayWnd != default) break;
                    Thread.Sleep(100);
                }

                if (trayWnd == default)
                {
                    DiagLog("[Launch] FAILED: Shell_TrayWnd did not appear within 15s");
                    return;
                }
                DiagLog("[Launch] Shell_TrayWnd found — shell is ready");

                // 3. Small extra delay to let shell fully initialize
                Thread.Sleep(500);

                // 4. Activate the URI via ShellExecuteEx (works now that shell is alive)
                DiagLog($"[Launch] ShellExecute({uri})...");
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                DiagLog("[Launch] ShellExecute succeeded");

                // 5. Wait for activation to be processed before killing explorer
                Thread.Sleep(3000);

                // 6. Kill explorer again (same method as Harbor startup)
                DiagLog("[Launch] Killing explorer...");
                trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
                if (trayWnd != default)
                {
                    WindowInterop.PostMessage(trayWnd, WM_QUIT, 0, 0);
                    foreach (var proc in Process.GetProcessesByName("explorer"))
                    {
                        proc.WaitForExit(5000);
                        proc.Dispose();
                    }
                }
                DiagLog("[Launch] Explorer killed — done");
            }
            catch (Exception ex)
            {
                DiagLog($"[Launch] EXCEPTION: {ex.GetType().Name}: {ex.Message} (0x{ex.HResult:X8})");
                DiagLog($"[Launch] Stack: {ex.StackTrace}");

                // If something went wrong, still try to clean up explorer
                try
                {
                    var trayWnd = PInvoke.FindWindow("Shell_TrayWnd", null);
                    if (trayWnd != default)
                        WindowInterop.PostMessage(trayWnd, WM_QUIT, 0, 0);
                }
                catch { /* best effort cleanup */ }
            }
        });
    }

    // SetSuspendState lives in powrprof.dll which CsWin32 can't generate
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
