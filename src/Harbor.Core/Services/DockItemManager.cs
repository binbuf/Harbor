using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using ManagedShell.WindowsTasks;

namespace Harbor.Core.Services;

/// <summary>
/// Merges pinned apps and running apps into a unified collection for the Dock UI.
/// Pinned apps appear first, then a separator, then unpinned running apps.
/// Multiple windows of the same application are grouped under a single DockItem.
/// </summary>
public sealed class DockItemManager : IDisposable
{
    private readonly DockPinningService _pinningService;
    private readonly IconExtractionService _iconService;
    private SynchronizationContext? _syncContext;
    private Tasks? _tasks;
    private bool _disposed;

    public DockItemManager(DockPinningService pinningService, IconExtractionService iconService)
    {
        _pinningService = pinningService;
        _iconService = iconService;
        _pinningService.PinsChanged += OnPinsChanged;
    }

    /// <summary>
    /// Pinned dock items (left of separator).
    /// </summary>
    public ObservableCollection<DockItem> PinnedItems { get; } = [];

    /// <summary>
    /// Running but unpinned dock items (right of separator).
    /// </summary>
    public ObservableCollection<DockItem> RunningItems { get; } = [];

    /// <summary>
    /// True when both pinned and running unpinned items exist (separator should be visible).
    /// </summary>
    public bool ShowSeparator => PinnedItems.Count > 0 && RunningItems.Count > 0;

    public DockPinningService PinningService => _pinningService;

    /// <summary>
    /// Binds to ManagedShell's Tasks and starts tracking.
    /// Must be called from the UI thread.
    /// </summary>
    public void Initialize(Tasks tasks)
    {
        _syncContext = SynchronizationContext.Current;
        _tasks = tasks;

        if (_tasks.GroupedWindows is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged += OnGroupedWindowsChanged;
        }

        Rebuild();
    }

    /// <summary>
    /// Rebuilds the pinned and running collections from current state.
    /// </summary>
    public void Rebuild()
    {
        RebuildPinnedItems();
        RebuildRunningItems();
        Trace.WriteLine($"[Harbor] DockItemManager: Rebuilt — {PinnedItems.Count} pinned, {RunningItems.Count} running");
    }

    private void RebuildPinnedItems()
    {
        PinnedItems.Clear();

        foreach (var pin in _pinningService.Pins)
        {
            var runningWindows = FindRunningWindows(pin.ExecutablePath);
            PinnedItems.Add(new DockItem
            {
                ExecutablePath = pin.ExecutablePath,
                DisplayName = pin.DisplayName,
                IsPinned = true,
                IsRunning = runningWindows.Count > 0,
                Windows = runningWindows,
                Icon = _iconService.GetIcon(pin.ExecutablePath),
            });
        }
    }

    private void RebuildRunningItems()
    {
        RunningItems.Clear();

        if (_tasks?.GroupedWindows is null) return;

        // Group windows by executable path (case-insensitive)
        var windowGroups = new Dictionary<string, List<ApplicationWindow>>(StringComparer.OrdinalIgnoreCase);

        foreach (ApplicationWindow window in _tasks.GroupedWindows)
        {
            var exePath = window.WinFileName;
            if (string.IsNullOrEmpty(exePath)) continue;
            if (_pinningService.IsPinned(exePath)) continue;

            if (!windowGroups.TryGetValue(exePath, out var group))
            {
                group = [];
                windowGroups[exePath] = group;
            }
            group.Add(window);
        }

        // Create one DockItem per unique executable
        foreach (var (exePath, windows) in windowGroups)
        {
            RunningItems.Add(new DockItem
            {
                ExecutablePath = exePath,
                DisplayName = ForegroundWindowService.GetFriendlyNameFromPath(exePath),
                IsPinned = false,
                IsRunning = true,
                Windows = windows,
                Icon = _iconService.GetIcon(exePath),
            });
        }
    }

    private List<ApplicationWindow> FindRunningWindows(string executablePath)
    {
        var windows = new List<ApplicationWindow>();
        if (_tasks?.GroupedWindows is null) return windows;

        foreach (ApplicationWindow window in _tasks.GroupedWindows)
        {
            if (string.Equals(window.WinFileName, executablePath, StringComparison.OrdinalIgnoreCase))
                windows.Add(window);
        }

        return windows;
    }

    private void OnPinsChanged(object? sender, EventArgs e)
    {
        PostRebuild();
    }

    private void OnGroupedWindowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        PostRebuild();
    }

    /// <summary>
    /// Ensures Rebuild() runs on the UI thread. ManagedShell may fire
    /// GroupedWindows.CollectionChanged from a background thread; modifying
    /// ObservableCollections bound to WPF controls off the UI thread would
    /// either throw or silently corrupt state.
    /// </summary>
    private void PostRebuild()
    {
        if (_syncContext is not null)
            _syncContext.Post(_ => Rebuild(), null);
        else
            Rebuild();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pinningService.PinsChanged -= OnPinsChanged;

        if (_tasks?.GroupedWindows is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged -= OnGroupedWindowsChanged;
        }
    }
}

/// <summary>
/// Represents a single item in the dock (pinned, running, or both).
/// Multiple windows of the same application are grouped under one DockItem.
/// </summary>
public class DockItem : INotifyPropertyChanged
{
    public required string ExecutablePath { get; set; }
    public required string DisplayName { get; set; }
    public bool IsPinned { get; set; }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { if (_isRunning != value) { _isRunning = value; OnPropertyChanged(); } }
    }

    private bool _isLaunching;
    /// <summary>
    /// True while a launch bounce animation is playing.
    /// </summary>
    public bool IsLaunching
    {
        get => _isLaunching;
        set { if (_isLaunching != value) { _isLaunching = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// All windows belonging to this application, grouped under this dock icon.
    /// The first window is typically the most recently active.
    /// </summary>
    public List<ApplicationWindow> Windows { get; set; } = [];

    /// <summary>
    /// The most recently active window, or null if no windows are open.
    /// </summary>
    public ApplicationWindow? ActiveWindow => Windows.Count > 0 ? Windows[0] : null;

    public System.Windows.Media.ImageSource? Icon { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
