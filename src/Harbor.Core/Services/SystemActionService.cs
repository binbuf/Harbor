using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;

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

    /// <summary>
    /// Launches a protocol URI. Uses explorer.exe as the launcher so it works
    /// even when Explorer's shell (Shell_TrayWnd) has been killed by Harbor.
    /// UseShellExecute fails with 0x80040900 when no shell is running.
    /// </summary>
    private static void LaunchUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = uri,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] SystemActionService: Failed to launch '{uri}': {ex.Message}");
        }
    }

    // SetSuspendState lives in powrprof.dll which CsWin32 can't generate
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
