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
/// </summary>
public sealed class DockItemManager : IDisposable
{
    private readonly DockPinningService _pinningService;
    private readonly IconExtractionService _iconService;
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
    /// </summary>
    public void Initialize(Tasks tasks)
    {
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
            var runningWindow = FindRunningWindow(pin.ExecutablePath);
            PinnedItems.Add(new DockItem
            {
                ExecutablePath = pin.ExecutablePath,
                DisplayName = pin.DisplayName,
                IsPinned = true,
                IsRunning = runningWindow is not null,
                Window = runningWindow,
                Icon = _iconService.GetIcon(pin.ExecutablePath),
            });
        }
    }

    private void RebuildRunningItems()
    {
        RunningItems.Clear();

        if (_tasks?.GroupedWindows is null) return;

        foreach (ApplicationWindow window in _tasks.GroupedWindows)
        {
            var exePath = window.WinFileName;
            if (string.IsNullOrEmpty(exePath)) continue;
            if (_pinningService.IsPinned(exePath)) continue;

            RunningItems.Add(new DockItem
            {
                ExecutablePath = exePath,
                DisplayName = window.Title ?? Path.GetFileNameWithoutExtension(exePath),
                IsPinned = false,
                IsRunning = true,
                Window = window,
                Icon = _iconService.GetIcon(exePath),
            });
        }
    }

    private ApplicationWindow? FindRunningWindow(string executablePath)
    {
        if (_tasks?.GroupedWindows is null) return null;

        foreach (ApplicationWindow window in _tasks.GroupedWindows)
        {
            if (string.Equals(window.WinFileName, executablePath, StringComparison.OrdinalIgnoreCase))
                return window;
        }

        return null;
    }

    private void OnPinsChanged(object? sender, EventArgs e)
    {
        Rebuild();
    }

    private void OnGroupedWindowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
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
/// </summary>
public class DockItem : INotifyPropertyChanged
{
    public required string ExecutablePath { get; set; }
    public required string DisplayName { get; set; }
    public bool IsPinned { get; set; }
    public bool IsRunning { get; set; }
    public ApplicationWindow? Window { get; set; }
    public System.Windows.Media.ImageSource? Icon { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
