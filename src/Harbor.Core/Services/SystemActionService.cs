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
        Process.Start(new ProcessStartInfo("ms-settings:about") { UseShellExecute = true });
    }

    public static void OpenSystemSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:") { UseShellExecute = true });
    }

    public static void OpenAppStore()
    {
        Process.Start(new ProcessStartInfo("ms-windows-store:") { UseShellExecute = true });
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

    // SetSuspendState lives in powrprof.dll which CsWin32 can't generate
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
