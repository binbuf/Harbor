using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Harbor.Core.Interop;
using Harbor.Core.Models;

namespace Harbor.Core.Services;

/// <summary>
/// Enumerates, caches, and watches for changes to installed Win32 and UWP applications.
/// </summary>
public class InstalledAppService : IDisposable
{
    private readonly IconExtractionService _iconService;
    private readonly Dispatcher _dispatcher;
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

    /// <summary>
    /// Patterns to filter out non-app shortcuts (uninstallers, help files, etc).
    /// </summary>
    private static readonly string[] ExcludedPatterns =
    [
        "uninstall", "setup", "readme", "help", "documentation",
        "remove", "repair", "support", "license", "changelog",
        "release notes", "website", "web site", "update", "configuration",
        "migrate", "diagnostic", "command prompt", "powershell"
    ];

    public InstalledAppService(IconExtractionService iconService)
    {
        _iconService = iconService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

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
            var apps = await Task.Run(EnumerateAllApps);
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

    private List<AppInfo> EnumerateAllApps()
    {
        var win32Apps = ScanStartMenuShortcuts();
        var uwpApps = ScanUwpApps();

        // Merge: deduplicate by executable path (case-insensitive)
        var merged = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in win32Apps)
        {
            if (!merged.ContainsKey(app.ExecutablePath))
                merged[app.ExecutablePath] = app;
        }

        foreach (var app in uwpApps)
        {
            var key = app.ExecutablePath;
            if (!merged.ContainsKey(key))
                merged[key] = app;
            else if (app.Icon is not null && merged[key].Icon is null)
                merged[key] = app; // Prefer UWP entry if it has a better icon
        }

        return merged.Values.ToList();
    }

    #region Pass 1: Start Menu Shortcuts

    private List<AppInfo> ScanStartMenuShortcuts()
    {
        var results = new List<AppInfo>();

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
                foreach (var lnkPath in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    var app = ProcessShortcut(lnkPath, dir);
                    if (app is not null)
                        results.Add(app);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] InstalledAppService: Error scanning {dir}: {ex.Message}");
            }
        }

        return results;
    }

    private AppInfo? ProcessShortcut(string lnkPath, string baseDir)
    {
        var fileName = Path.GetFileNameWithoutExtension(lnkPath);

        // Filter out non-app shortcuts
        if (ExcludedPatterns.Any(p => fileName.Contains(p, StringComparison.OrdinalIgnoreCase)))
            return null;

        var linkInfo = ShellLinkInterop.ReadShortcut(lnkPath);
        if (linkInfo is null)
            return null;

        var targetPath = linkInfo.TargetPath;

        // Filter out shortcuts that don't point to an executable
        if (string.IsNullOrWhiteSpace(targetPath))
            return null;

        // Filter out directories and non-exe targets
        if (!targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(targetPath))
            return null;

        // Extract category from subfolder
        var relativePath = Path.GetRelativePath(baseDir, lnkPath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        string? category = parts.Length > 1 ? parts[0] : null;

        // Get icon
        ImageSource? icon = null;
        try
        {
            icon = _iconService.GetIcon(targetPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] InstalledAppService: Icon extraction failed for {targetPath}: {ex.Message}");
        }

        return new AppInfo
        {
            DisplayName = fileName,
            ExecutablePath = targetPath,
            LaunchArguments = string.IsNullOrEmpty(linkInfo.Arguments) ? null : linkInfo.Arguments,
            IsStoreApp = false,
            Category = category,
            Icon = icon,
        };
    }

    #endregion

    #region Pass 2: UWP/Store Apps

    private List<AppInfo> ScanUwpApps()
    {
        var results = new List<AppInfo>();

        try
        {
            var packageManager = new Windows.Management.Deployment.PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty);

            foreach (var package in packages)
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
                        continue;

                    var installPath = package.InstalledLocation?.Path;
                    if (string.IsNullOrEmpty(installPath))
                        continue;

                    var manifestPath = Path.Combine(installPath, "AppxManifest.xml");
                    if (!File.Exists(manifestPath))
                        continue;

                    var apps = ParseAppxManifest(manifestPath, installPath, package.Id.FamilyName);
                    results.AddRange(apps);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[Harbor] InstalledAppService: Error processing UWP package {package.Id?.Name}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] InstalledAppService: UWP enumeration failed: {ex.Message}");
        }

        return results;
    }

    private List<AppInfo> ParseAppxManifest(string manifestPath, string installPath, string familyName)
    {
        var results = new List<AppInfo>();
        var doc = XDocument.Load(manifestPath);
        var ns = doc.Root?.GetDefaultNamespace();
        if (ns is null) return results;

        // Find the uap namespace for VisualElements
        var uapNs = doc.Root?.Attributes()
            .Where(a => a.IsNamespaceDeclaration && a.Value.Contains("windows.universalApiContract", StringComparison.OrdinalIgnoreCase))
            .Select(a => XNamespace.Get(a.Value))
            .FirstOrDefault();

        // Also check for standard uap namespace
        uapNs ??= doc.Root?.Attributes()
            .Where(a => a.IsNamespaceDeclaration && a.Value.EndsWith("/uap", StringComparison.OrdinalIgnoreCase))
            .Select(a => XNamespace.Get(a.Value))
            .FirstOrDefault();

        var applications = doc.Descendants(ns + "Application");

        foreach (var appElement in applications)
        {
            var appId = appElement.Attribute("Id")?.Value;
            if (string.IsNullOrEmpty(appId))
                continue;

            // Check VisualElements — skip entries with AppListEntry="none"
            var visualElements = appElement.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "VisualElements");

            if (visualElements is not null)
            {
                var appListEntry = visualElements.Attribute("AppListEntry")?.Value;
                if (string.Equals(appListEntry, "none", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Get display name
            var displayName = visualElements?.Attribute("DisplayName")?.Value
                ?? appElement.Attribute("Id")?.Value
                ?? "Unknown";

            // Resolve ms-resource:// references
            var aumid = $"{familyName}!{appId}";
            if (displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith("@{", StringComparison.Ordinal))
            {
                // Try to resolve using SHLoadIndirectString with the package resource
                var resolved = ShellLinkInterop.ResolveIndirectString(
                    $"@{{{aumid}?ms-resource://{Path.GetFileNameWithoutExtension(familyName)}/Resources/{Path.GetFileName(displayName.Replace("ms-resource:", ""))}}}");
                if (resolved != displayName && !string.IsNullOrEmpty(resolved))
                    displayName = resolved;
                else
                {
                    // Fallback: try direct resolution
                    resolved = ShellLinkInterop.ResolveIndirectString(displayName);
                    if (resolved != displayName && !string.IsNullOrEmpty(resolved))
                        displayName = resolved;
                }
            }

            // Skip if display name still looks like a resource reference
            if (displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ||
                displayName.StartsWith("@{", StringComparison.Ordinal))
                continue;

            // Extract icon
            var icon = ExtractUwpIcon(visualElements, installPath);

            results.Add(new AppInfo
            {
                DisplayName = displayName,
                ExecutablePath = $"shell:appsFolder\\{aumid}",
                IsStoreApp = true,
                PackageFamilyName = familyName,
                Icon = icon,
            });
        }

        return results;
    }

    private static ImageSource? ExtractUwpIcon(XElement? visualElements, string installPath)
    {
        if (visualElements is null)
            return null;

        var logoAttr = visualElements.Attribute("Square44x44Logo")?.Value;
        if (string.IsNullOrEmpty(logoAttr))
            return null;

        var logoDir = Path.GetDirectoryName(Path.Combine(installPath, logoAttr));
        var logoBaseName = Path.GetFileNameWithoutExtension(logoAttr);
        var logoExt = Path.GetExtension(logoAttr);

        if (logoDir is null)
            return null;

        // Search for scale variants in preference order
        string[] preferredSuffixes =
        [
            ".targetsize-48", ".targetsize-64", ".targetsize-48_altform-unplated",
            ".scale-200", ".scale-150", ".scale-100", ""
        ];

        try
        {
            if (!Directory.Exists(logoDir))
                return null;

            var candidates = Directory.GetFiles(logoDir, $"{logoBaseName}*{logoExt}");

            foreach (var suffix in preferredSuffixes)
            {
                var match = candidates.FirstOrDefault(c =>
                    Path.GetFileNameWithoutExtension(c).Equals(logoBaseName + suffix, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return LoadFrozenBitmap(match);
            }

            // Fallback: use first available candidate
            if (candidates.Length > 0)
                return LoadFrozenBitmap(candidates[0]);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] InstalledAppService: UWP icon extraction failed: {ex.Message}");
        }

        return null;
    }

    private static ImageSource? LoadFrozenBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region File System Watching

    private void SetupWatchers()
    {
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _debounceTimer.Stop();

        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}
