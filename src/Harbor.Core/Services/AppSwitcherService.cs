using System.Diagnostics;
using System.Windows.Input;
using Harbor.Core.Interop;
using ManagedShell.WindowsTasks;
using Windows.Win32.Foundation;

namespace Harbor.Core.Services;

/// <summary>
/// Manages the ALT+TAB app switching logic. Tracks ALT key state and TAB presses
/// to cycle through running applications. Delegates UI to an external overlay.
/// </summary>
public sealed class AppSwitcherService : IDisposable
{
    private const int VK_TAB = 0x09;
    private const int VK_LMENU = 0xA4; // Left Alt
    private const int VK_RMENU = 0xA5; // Right Alt

    private readonly LowLevelKeyboardHookService _keyboard;
    private readonly Tasks _tasks;
    private readonly IconExtractionService _iconService;
    private bool _isActive;
    private List<AppEntry> _apps = [];
    private int _selectedIndex;
    private bool _disposed;

    public event Action<List<AppEntry>, int>? ShowRequested;
    public event Action<int>? SelectionChanged;
    public event Action? HideRequested;

    public AppSwitcherService(LowLevelKeyboardHookService keyboard, Tasks tasks, IconExtractionService iconService)
    {
        _keyboard = keyboard;
        _tasks = tasks;
        _iconService = iconService;

        _keyboard.Register(VK_TAB, ModifierKeys.Alt, OnAltTab);
        _keyboard.Register(VK_TAB, ModifierKeys.Alt | ModifierKeys.Shift, OnAltShiftTab);
        _keyboard.RawKeyEvent += OnRawKeyEvent;

        Trace.WriteLine("[Harbor] AppSwitcherService: Registered ALT+TAB handler.");
    }

    private bool OnAltTab(bool isKeyDown)
    {
        if (!isKeyDown) return true;
        AdvanceSelection(forward: true);
        return true; // suppress default Windows ALT+TAB
    }

    private bool OnAltShiftTab(bool isKeyDown)
    {
        if (!isKeyDown) return true;
        AdvanceSelection(forward: false);
        return true;
    }

    private void OnRawKeyEvent(int vkCode, bool isDown)
    {
        // Detect ALT release to commit selection
        if (!isDown && (vkCode == VK_LMENU || vkCode == VK_RMENU) && _isActive)
        {
            CommitSelection();
        }
    }

    private void AdvanceSelection(bool forward)
    {
        if (!_isActive)
        {
            // Build app list
            _apps = BuildAppList();
            if (_apps.Count == 0) return;

            _isActive = true;
            _selectedIndex = forward ? 1 : _apps.Count - 1;
            if (_selectedIndex >= _apps.Count) _selectedIndex = 0;

            ShowRequested?.Invoke(_apps, _selectedIndex);
        }
        else
        {
            if (_apps.Count == 0) return;

            if (forward)
                _selectedIndex = (_selectedIndex + 1) % _apps.Count;
            else
                _selectedIndex = (_selectedIndex - 1 + _apps.Count) % _apps.Count;

            SelectionChanged?.Invoke(_selectedIndex);
        }
    }

    private void CommitSelection()
    {
        _isActive = false;

        if (_selectedIndex >= 0 && _selectedIndex < _apps.Count)
        {
            var app = _apps[_selectedIndex];
            // Activate the topmost window of the selected app
            if (app.Windows.Count > 0)
            {
                // Sort by Z-order, activate the topmost
                var sorted = app.Windows
                    .OrderBy(w => WindowInterop.GetZOrder(new HWND(w.Handle)))
                    .ToList();
                sorted[0].BringToFront();
            }
        }

        HideRequested?.Invoke();
        _apps.Clear();
    }

    private List<AppEntry> BuildAppList()
    {
        var groups = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (ApplicationWindow window in _tasks.GroupedWindows)
        {
            var exePath = window.WinFileName;
            if (string.IsNullOrEmpty(exePath)) continue;

            if (!groups.TryGetValue(exePath, out var entry))
            {
                entry = new AppEntry
                {
                    ExecutablePath = exePath,
                    DisplayName = ForegroundWindowService.GetFriendlyNameFromPath(exePath),
                    Icon = _iconService.GetIcon(exePath),
                    Windows = [],
                };
                groups[exePath] = entry;
            }

            entry.Windows.Add(window);
        }

        // Sort by Z-order of the topmost window in each group (most recent first)
        var result = groups.Values.ToList();
        result.Sort((a, b) =>
        {
            var zA = a.Windows.Min(w => WindowInterop.GetZOrder(new HWND(w.Handle)));
            var zB = b.Windows.Min(w => WindowInterop.GetZOrder(new HWND(w.Handle)));
            return zA.CompareTo(zB);
        });

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _keyboard.Unregister(VK_TAB, ModifierKeys.Alt);
        _keyboard.Unregister(VK_TAB, ModifierKeys.Alt | ModifierKeys.Shift);
        _keyboard.RawKeyEvent -= OnRawKeyEvent;
    }

    public class AppEntry
    {
        public required string ExecutablePath { get; set; }
        public required string DisplayName { get; set; }
        public System.Windows.Media.ImageSource? Icon { get; set; }
        public List<ApplicationWindow> Windows { get; set; } = [];
    }
}
