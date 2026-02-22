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
    public bool ReplaceExplorer { get; set; } = false;
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
                snapshot = new ShellSettings { ReplaceExplorer = _settings.ReplaceExplorer };

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
