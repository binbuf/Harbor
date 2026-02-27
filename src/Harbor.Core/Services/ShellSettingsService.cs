using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Harbor.Core.Services;

/// <summary>
/// Persists shell-level settings to %LOCALAPPDATA%\Harbor\shell-settings.json.
/// </summary>
public class ShellSettings
{
    /// <summary>
    /// When true, Harbor kills explorer.exe on startup and restarts it on exit.
    /// </summary>
    public bool ReplaceExplorer { get; set; } = true;

    /// <summary>
    /// Show/hide the global menu items from the foreground app in the top menu bar.
    /// </summary>
    public bool ShowAppMenuItems { get; set; } = true;

    /// <summary>
    /// Menu bar opacity (0.0 = fully transparent acrylic, 1.0 = fully opaque solid).
    /// Default 0.8 enables acrylic blur at 80% opacity.
    /// </summary>
    public double MenuBarOpacity { get; set; } = 0.8;

    /// <summary>
    /// Show day of week in the menu bar clock.
    /// </summary>
    public bool ShowDayOfWeek { get; set; } = true;

    /// <summary>
    /// Use 24-hour clock format.
    /// </summary>
    public bool Use24HourClock { get; set; }

    /// <summary>
    /// Show seconds in the menu bar clock.
    /// </summary>
    public bool ShowSeconds { get; set; }

    /// <summary>
    /// Theme override: "auto", "light", or "dark".
    /// </summary>
    public string ThemeOverride { get; set; } = "auto";

    /// <summary>
    /// Show recent applications in the dock.
    /// </summary>
    public bool ShowRecentApps { get; set; } = true;

    /// <summary>
    /// Animate opening applications (bounce).
    /// </summary>
    public bool AnimateOpeningApps { get; set; } = true;

    /// <summary>
    /// Show desktop icons.
    /// </summary>
    public bool ShowDesktopIcons { get; set; } = true;

    /// <summary>
    /// Auto-hide the menu bar until the cursor hits the top screen edge.
    /// </summary>
    public bool AutoHideMenuBar { get; set; }

    /// <summary>
    /// Force all tray icons to monochrome (grayscale) for visual consistency.
    /// </summary>
    public bool MonochromeTrayIcons { get; set; } = true;

    /// <summary>
    /// Menu bar text/icon color mode: "white", "black", or "auto" (wallpaper-brightness-based).
    /// </summary>
    public string MenuBarTextColor { get; set; } = "white";

    /// <summary>
    /// Filter out utilities, documentation, and uninstallers from the Apps launcher.
    /// </summary>
    public bool FilterAppsFolder { get; set; } = true;

    /// <summary>
    /// Hide the Recycle Bin desktop icon.
    /// </summary>
    public bool HideRecycleBin { get; set; } = true;

    /// <summary>
    /// Use Harbor's custom app switcher instead of the default Windows ALT+TAB.
    /// </summary>
    public bool UseCustomAppSwitcher { get; set; } = true;
}

/// <summary>
/// Manages shell-level preferences. Persists settings to a JSON file
/// at %LOCALAPPDATA%\Harbor\shell-settings.json.
/// </summary>
public class ShellSettingsService : IDisposable
{
    private readonly string _configPath;
    private ShellSettings _settings = new();
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ShellSettingsService()
        : this(GetDefaultConfigPath())
    {
    }

    public ShellSettingsService(string configPath)
    {
        _configPath = configPath;
        Load();
    }

    /// <summary>
    /// When true, Harbor kills explorer.exe on startup and restarts it on exit.
    /// </summary>
    public bool ReplaceExplorer
    {
        get { lock (_lock) return _settings.ReplaceExplorer; }
        set
        {
            lock (_lock)
            {
                if (_settings.ReplaceExplorer == value) return;
                _settings.ReplaceExplorer = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowAppMenuItems
    {
        get { lock (_lock) return _settings.ShowAppMenuItems; }
        set
        {
            lock (_lock)
            {
                if (_settings.ShowAppMenuItems == value) return;
                _settings.ShowAppMenuItems = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double MenuBarOpacity
    {
        get { lock (_lock) return _settings.MenuBarOpacity; }
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            lock (_lock)
            {
                if (Math.Abs(_settings.MenuBarOpacity - clamped) < 0.001) return;
                _settings.MenuBarOpacity = clamped;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowDayOfWeek
    {
        get { lock (_lock) return _settings.ShowDayOfWeek; }
        set
        {
            lock (_lock)
            {
                if (_settings.ShowDayOfWeek == value) return;
                _settings.ShowDayOfWeek = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool Use24HourClock
    {
        get { lock (_lock) return _settings.Use24HourClock; }
        set
        {
            lock (_lock)
            {
                if (_settings.Use24HourClock == value) return;
                _settings.Use24HourClock = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowSeconds
    {
        get { lock (_lock) return _settings.ShowSeconds; }
        set
        {
            lock (_lock)
            {
                if (_settings.ShowSeconds == value) return;
                _settings.ShowSeconds = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string ThemeOverride
    {
        get { lock (_lock) return _settings.ThemeOverride; }
        set
        {
            lock (_lock)
            {
                if (_settings.ThemeOverride == value) return;
                _settings.ThemeOverride = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowRecentApps
    {
        get { lock (_lock) return _settings.ShowRecentApps; }
        set
        {
            lock (_lock)
            {
                if (_settings.ShowRecentApps == value) return;
                _settings.ShowRecentApps = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool AnimateOpeningApps
    {
        get { lock (_lock) return _settings.AnimateOpeningApps; }
        set
        {
            lock (_lock)
            {
                if (_settings.AnimateOpeningApps == value) return;
                _settings.AnimateOpeningApps = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowDesktopIcons
    {
        get { lock (_lock) return _settings.ShowDesktopIcons; }
        set
        {
            lock (_lock)
            {
                if (_settings.ShowDesktopIcons == value) return;
                _settings.ShowDesktopIcons = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool AutoHideMenuBar
    {
        get { lock (_lock) return _settings.AutoHideMenuBar; }
        set
        {
            lock (_lock)
            {
                if (_settings.AutoHideMenuBar == value) return;
                _settings.AutoHideMenuBar = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool MonochromeTrayIcons
    {
        get { lock (_lock) return _settings.MonochromeTrayIcons; }
        set
        {
            lock (_lock)
            {
                if (_settings.MonochromeTrayIcons == value) return;
                _settings.MonochromeTrayIcons = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string MenuBarTextColor
    {
        get { lock (_lock) return _settings.MenuBarTextColor; }
        set
        {
            lock (_lock)
            {
                if (_settings.MenuBarTextColor == value) return;
                _settings.MenuBarTextColor = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool FilterAppsFolder
    {
        get { lock (_lock) return _settings.FilterAppsFolder; }
        set
        {
            lock (_lock)
            {
                if (_settings.FilterAppsFolder == value) return;
                _settings.FilterAppsFolder = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HideRecycleBin
    {
        get { lock (_lock) return _settings.HideRecycleBin; }
        set
        {
            lock (_lock)
            {
                if (_settings.HideRecycleBin == value) return;
                _settings.HideRecycleBin = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool UseCustomAppSwitcher
    {
        get { lock (_lock) return _settings.UseCustomAppSwitcher; }
        set
        {
            lock (_lock)
            {
                if (_settings.UseCustomAppSwitcher == value) return;
                _settings.UseCustomAppSwitcher = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised when any setting changes.
    /// </summary>
    public event EventHandler? SettingsChanged;

    private void Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Trace.WriteLine("[Harbor] ShellSettingsService: No config file, using defaults.");
                return;
            }

            var json = File.ReadAllText(_configPath);
            var settings = JsonSerializer.Deserialize<ShellSettings>(json, s_jsonOptions);
            if (settings is not null)
            {
                lock (_lock)
                    _settings = settings;
            }

            Trace.WriteLine($"[Harbor] ShellSettingsService: Loaded settings (ReplaceExplorer={_settings.ReplaceExplorer}).");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ShellSettingsService: Failed to load: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            ShellSettings snapshot;
            lock (_lock)
            {
                snapshot = new ShellSettings
                {
                    ReplaceExplorer = _settings.ReplaceExplorer,
                    ShowAppMenuItems = _settings.ShowAppMenuItems,
                    MenuBarOpacity = _settings.MenuBarOpacity,
                    ShowDayOfWeek = _settings.ShowDayOfWeek,
                    Use24HourClock = _settings.Use24HourClock,
                    ShowSeconds = _settings.ShowSeconds,
                    ThemeOverride = _settings.ThemeOverride,
                    ShowRecentApps = _settings.ShowRecentApps,
                    AnimateOpeningApps = _settings.AnimateOpeningApps,
                    ShowDesktopIcons = _settings.ShowDesktopIcons,
                    AutoHideMenuBar = _settings.AutoHideMenuBar,
                    MonochromeTrayIcons = _settings.MonochromeTrayIcons,
                    MenuBarTextColor = _settings.MenuBarTextColor,
                    FilterAppsFolder = _settings.FilterAppsFolder,
                    HideRecycleBin = _settings.HideRecycleBin,
                    UseCustomAppSwitcher = _settings.UseCustomAppSwitcher,
                };
            }

            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            File.WriteAllText(_configPath, json);

            Trace.WriteLine($"[Harbor] ShellSettingsService: Saved settings to {_configPath}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ShellSettingsService: Failed to save: {ex.Message}");
        }
    }

    private static string GetDefaultConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Harbor", "shell-settings.json");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
