using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Harbor.Core.Services;

/// <summary>
/// Installs a WH_KEYBOARD_LL hook and dispatches registered hotkeys.
/// Must be created on a thread with a message pump (WPF dispatcher thread).
/// </summary>
public sealed class LowLevelKeyboardHookService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    private nint _hookId;
    private readonly LowLevelKeyboardProc _proc;
    private bool _disposed;

    // Hotkey handlers: key = (vkCode, modifiers), value = handler(isKeyDown) returning true to suppress
    private readonly Dictionary<(int vkCode, ModifierKeys modifiers), Func<bool, bool>> _handlers = [];

    // Raw key event for services that need to track key state
    public event Action<int, bool>? RawKeyEvent; // (vkCode, isDown)

    public LowLevelKeyboardHookService()
    {
        _proc = HookCallback;
        _hookId = SetHook(_proc);
        Trace.WriteLine($"[Harbor] LowLevelKeyboardHookService: Hook installed (handle={_hookId})");
    }

    /// <summary>
    /// Registers a hotkey handler. Handler receives isKeyDown and returns true to suppress the key.
    /// </summary>
    public void Register(int vkCode, ModifierKeys modifiers, Func<bool, bool> handler)
    {
        _handlers[(vkCode, modifiers)] = handler;
    }

    public void Unregister(int vkCode, ModifierKeys modifiers)
    {
        _handlers.Remove((vkCode, modifiers));
    }

    private static nint SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var msg = (int)wParam;
            var isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
            var isKeyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                // Fire raw event
                RawKeyEvent?.Invoke((int)hookStruct.vkCode, isKeyDown);

                // Check for registered handlers
                var modifiers = Keyboard.Modifiers;
                var key = ((int)hookStruct.vkCode, modifiers);

                if (_handlers.TryGetValue(key, out var handler))
                {
                    if (handler(isKeyDown))
                        return 1; // Suppress the key
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookId != 0)
        {
            UnhookWindowsHookEx(_hookId);
            Trace.WriteLine("[Harbor] LowLevelKeyboardHookService: Hook removed.");
            _hookId = 0;
        }

        _handlers.Clear();
    }
}
