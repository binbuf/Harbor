using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Harbor.Core.Models;

namespace Harbor.Core.Services;

/// <summary>
/// Enumerates, caches, and watches for changes to installed applications
/// by querying the shell:AppsFolder virtual folder.
/// </summary>
public class InstalledAppService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ShellSettingsService? _shellSettings;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly DispatcherTimer _debounceTimer;
    private bool _disposed;

    /// <summary>
    /// Observable collection of discovered apps, safe for WPF binding.
    /// Updated only on the UI thread.
    /// </summary>
    public ObservableCollection<AppInfo> Apps { get; } = [];

    /// <summary>
    /// Raised when the app list changes after a rescan.
    /// </summary>
    public event Action? AppsChanged;

    public InstalledAppService(ShellSettingsService? shellSettings = null)
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _shellSettings = shellSettings;

        if (_shellSettings is not null)
            _shellSettings.SettingsChanged += OnSettingsChanged;

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _debounceTimer.Tick += OnDebounceTimerTick;
    }

    /// <summary>
    /// Starts an asynchronous background scan of installed applications.
    /// </summary>
    public async Task ScanAsync()
    {
        try
        {
            var filter = _shellSettings?.FilterAppsFolder ?? false;
            var apps = await Task.Run(() => ShellAppEnumerator.EnumerateApps(filter));

            // Dump diagnostic log for review
            var dumpPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Harbor", "apps-dump.tsv");
            Directory.CreateDirectory(Path.GetDirectoryName(dumpPath)!);
            ShellAppEnumerator.DumpAppList(apps, dumpPath);

            _dispatcher.Invoke(() =>
            {
                Apps.Clear();
                foreach (var app in apps.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
                    Apps.Add(app);

                AppsChanged?.Invoke();
                Trace.WriteLine($"[Harbor] InstalledAppService: Scan complete, found {Apps.Count} apps.");
            });

            SetupWatchers();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] InstalledAppService: Scan failed: {ex.Message}");
        }
    }

    #region File System Watching

    private void SetupWatchers()
    {
        // Watch Start Menu directories for shortcut changes (Win32 app installs/uninstalls)
        var directories = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    Filter = "*.lnk",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };

                watcher.Created += OnFileSystemChanged;
                watcher.Deleted += OnFileSystemChanged;
                watcher.Renamed += OnFileSystemChanged;

                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] InstalledAppService: Failed to watch {dir}: {ex.Message}");
            }
        }

        // Watch for UWP package changes
        try
        {
            var catalog = Windows.ApplicationModel.PackageCatalog.OpenForCurrentUser();
            catalog.PackageInstalling += OnPackageChanged;
            catalog.PackageUninstalling += OnPackageChanged;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] InstalledAppService: Failed to watch UWP packages: {ex.Message}");
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private void OnPackageChanged(object? sender, object e)
    {
        _dispatcher.Invoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private async void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        Trace.WriteLine("[Harbor] InstalledAppService: Change detected, rescanning...");
        await ScanAsync();
    }

    #endregion

    private async void OnSettingsChanged(object? sender, EventArgs e)
    {
        await ScanAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_shellSettings is not null)
            _shellSettings.SettingsChanged -= OnSettingsChanged;

        _debounceTimer.Stop();

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}
