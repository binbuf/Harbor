using System.Diagnostics;
using System.Windows.Input;
using Harbor.Core.Interop;
using ManagedShell.WindowsTasks;
using Windows.Win32.Foundation;

namespace Harbor.Core.Services;

/// <summary>
/// Implements ALT+` (backtick) same-application window cycling.
/// Cycles through all windows of the foreground application.
/// </summary>
public sealed class WindowCycleService : IDisposable
{
    private const int VK_OEM_3 = 0xC0; // backtick/tilde key
    private readonly LowLevelKeyboardHookService _keyboard;
    private readonly Tasks _tasks;
    private bool _disposed;

    public WindowCycleService(LowLevelKeyboardHookService keyboard, Tasks tasks)
    {
        _keyboard = keyboard;
        _tasks = tasks;

        _keyboard.Register(VK_OEM_3, ModifierKeys.Alt, OnAltBacktick);
        Trace.WriteLine("[Harbor] WindowCycleService: Registered ALT+` handler.");
    }

    private bool OnAltBacktick(bool isKeyDown)
    {
        if (!isKeyDown) return true; // suppress key-up too

        try
        {
            var fgHwnd = WindowInterop.GetForegroundWindow();
            if (fgHwnd == HWND.Null) return true;

            WindowInterop.GetWindowThreadProcessId(fgHwnd, out var processId);
            if (processId == 0) return true;

            // Find all windows belonging to the same process
            var sameProcessWindows = new List<ApplicationWindow>();
            foreach (ApplicationWindow window in _tasks.GroupedWindows)
            {
                WindowInterop.GetWindowThreadProcessId(new HWND(window.Handle), out var wPid);
                if (wPid == processId)
                    sameProcessWindows.Add(window);
            }

            if (sameProcessWindows.Count <= 1) return true; // nothing to cycle

            // Sort by Z-order (lowest number = topmost)
            sameProcessWindows.Sort((a, b) =>
                WindowInterop.GetZOrder(new HWND(a.Handle))
                    .CompareTo(WindowInterop.GetZOrder(new HWND(b.Handle))));

            // Find current foreground window in the list
            int currentIndex = -1;
            for (int i = 0; i < sameProcessWindows.Count; i++)
            {
                if (new HWND(sameProcessWindows[i].Handle) == fgHwnd)
                {
                    currentIndex = i;
                    break;
                }
            }

            // Activate the next window (wrap around)
            int nextIndex = (currentIndex + 1) % sameProcessWindows.Count;
            sameProcessWindows[nextIndex].BringToFront();

            Trace.WriteLine($"[Harbor] WindowCycleService: Cycled to window {nextIndex + 1}/{sameProcessWindows.Count}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] WindowCycleService: Error cycling: {ex.Message}");
        }

        return true; // suppress the key
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _keyboard.Unregister(VK_OEM_3, ModifierKeys.Alt);
    }
}
