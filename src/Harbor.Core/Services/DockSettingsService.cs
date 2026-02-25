using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harbor.Core.Services;

/// <summary>
/// Dock auto-hide behavior mode.
/// </summary>
public enum DockAutoHideMode
{
    /// <summary>Dock is always visible.</summary>
    Never,
    /// <summary>Dock hides when a window overlaps its area.</summary>
    WhenOverlapped,
    /// <summary>Dock is always hidden until the mouse approaches the bottom edge.</summary>
    Always,
}

/// <summary>
/// Persists dock display settings to %LOCALAPPDATA%\Harbor\dock-settings.json.
/// </summary>
public class DockSettings
{
    public int IconSize { get; set; } = 102;
    public bool FullWidthDock { get; set; } = false;
    public DockAutoHideMode AutoHideMode { get; set; } = DockAutoHideMode.Never;
    public bool MagnificationEnabled { get; set; } = false;
}

/// <summary>
/// Manages dock display preferences. Persists settings to a JSON file
/// at %LOCALAPPDATA%\Harbor\dock-settings.json.
/// </summary>
public class DockSettingsService : IDisposable
{
    private readonly string _configPath;
    private DockSettings _settings = new();
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public DockSettingsService()
        : this(GetDefaultConfigPath())
    {
    }

    public DockSettingsService(string configPath)
    {
        _configPath = configPath;
        Load();
    }

    /// <summary>
    /// Current settings snapshot.
    /// </summary>
    public DockSettings Settings
    {
        get { lock (_lock) return _settings; }
    }

    /// <summary>
    /// Icon size in pixels (32–128, default 48).
    /// </summary>
    public int IconSize
    {
        get { lock (_lock) return _settings.IconSize; }
        set
        {
            var clamped = Math.Clamp(value, 32, 128);
            lock (_lock)
            {
                if (_settings.IconSize == clamped) return;
                _settings.IconSize = clamped;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether the dock spans the full width of the screen.
    /// </summary>
    public bool FullWidthDock
    {
        get { lock (_lock) return _settings.FullWidthDock; }
        set
        {
            lock (_lock)
            {
                if (_settings.FullWidthDock == value) return;
                _settings.FullWidthDock = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Dock auto-hide mode (Never, WhenOverlapped, Always).
    /// </summary>
    public DockAutoHideMode AutoHideMode
    {
        get { lock (_lock) return _settings.AutoHideMode; }
        set
        {
            lock (_lock)
            {
                if (_settings.AutoHideMode == value) return;
                _settings.AutoHideMode = value;
            }
            Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether dock magnification (fishbowl) effect is enabled.
    /// </summary>
    public bool MagnificationEnabled
    {
        get { lock (_lock) return _settings.MagnificationEnabled; }
        set
        {
            lock (_lock)
            {
                if (_settings.MagnificationEnabled == value) return;
                _settings.MagnificationEnabled = value;
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
                Trace.WriteLine("[Harbor] DockSettingsService: No config file, using defaults.");
                return;
            }

            var json = File.ReadAllText(_configPath);
            var settings = JsonSerializer.Deserialize<DockSettings>(json, s_jsonOptions);
            if (settings is not null)
            {
                lock (_lock)
                {
                    _settings = settings;
                    _settings.IconSize = Math.Clamp(_settings.IconSize, 32, 128);
                }
            }

            Trace.WriteLine($"[Harbor] DockSettingsService: Loaded settings (IconSize={_settings.IconSize}, FullWidthDock={_settings.FullWidthDock}, AutoHideMode={_settings.AutoHideMode}, MagnificationEnabled={_settings.MagnificationEnabled}).");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] DockSettingsService: Failed to load: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            DockSettings snapshot;
            lock (_lock)
                snapshot = new DockSettings
                {
                    IconSize = _settings.IconSize,
                    FullWidthDock = _settings.FullWidthDock,
                    AutoHideMode = _settings.AutoHideMode,
                    MagnificationEnabled = _settings.MagnificationEnabled,
                };

            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            File.WriteAllText(_configPath, json);

            Trace.WriteLine($"[Harbor] DockSettingsService: Saved settings to {_configPath}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] DockSettingsService: Failed to save: {ex.Message}");
        }
    }

    private static string GetDefaultConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Harbor", "dock-settings.json");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
