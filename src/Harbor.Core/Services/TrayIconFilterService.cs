using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using ManagedShell.WindowsTray;

namespace Harbor.Core.Services;

/// <summary>
/// Filters native Windows system indicator icons out of the ManagedShell tray collection
/// so they don't appear alongside Harbor's custom indicator controls.
/// </summary>
public sealed class TrayIconFilterService : IDisposable
{
    private readonly NotificationArea _notificationArea;
    private readonly ICollectionView _filteredView;
    private bool _disposed;

    // Executable paths that host system indicator icons
    private static readonly HashSet<string> FilteredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sndvol.exe",
        "sndvol64.exe",
    };

    // Title substrings that identify volume-related tray icons
    private static readonly string[] VolumeSubstrings =
    {
        "Speakers",
        "Volume",
        "Headphones",
    };

    public ICollectionView FilteredTrayIcons => _filteredView;

    public TrayIconFilterService(NotificationArea notificationArea)
    {
        _notificationArea = notificationArea;

        _filteredView = CollectionViewSource.GetDefaultView(_notificationArea.TrayIcons);
        _filteredView.Filter = FilterIcon;

        // Refresh filter when the tray icons collection changes
        if (_notificationArea.TrayIcons is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnTrayIconsCollectionChanged;
        }

        Trace.WriteLine("[Harbor] TrayIconFilterService: Initialized.");
    }

    private bool FilterIcon(object obj)
    {
        if (obj is not NotifyIcon icon) return true;

        // Filter by process name
        if (!string.IsNullOrEmpty(icon.Path))
        {
            var fileName = System.IO.Path.GetFileName(icon.Path);
            if (FilteredProcessNames.Contains(fileName))
                return false;
        }

        // Filter by title substring (volume icons)
        if (!string.IsNullOrEmpty(icon.Title))
        {
            foreach (var substring in VolumeSubstrings)
            {
                if (icon.Title.Contains(substring, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private void OnTrayIconsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _filteredView.Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notificationArea.TrayIcons is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged -= OnTrayIconsCollectionChanged;
        }

        Trace.WriteLine("[Harbor] TrayIconFilterService: Disposed.");
    }
}
