using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Harbor.Core.Services;

/// <summary>
/// Manages per-application title bar color overrides.
/// Persists overrides to %LOCALAPPDATA%\Harbor\color-overrides.json.
/// </summary>
public sealed class ColorOverrideService : IDisposable
{
    private readonly string _configPath;
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ColorOverrideService()
        : this(GetDefaultConfigPath())
    {
    }

    public ColorOverrideService(string configPath)
    {
        _configPath = configPath;
        Load();
    }

    /// <summary>
    /// Gets the override color for a process name, or null if none is set.
    /// </summary>
    public Color? GetOverride(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return null;

        lock (_lock)
        {
            if (_overrides.TryGetValue(processName, out var hex))
            {
                return ParseHexColor(hex);
            }
        }

        return null;
    }

    /// <summary>
    /// Sets an override color for a process name.
    /// </summary>
    public void SetOverride(string processName, Color color)
    {
        if (string.IsNullOrEmpty(processName)) return;

        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        lock (_lock)
            _overrides[processName] = hex;

        Save();
        Trace.WriteLine($"[Harbor] ColorOverrideService: Set override for {processName} = {hex}");
    }

    /// <summary>
    /// Removes an override for a process name.
    /// </summary>
    public void RemoveOverride(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return;

        bool removed;
        lock (_lock)
            removed = _overrides.Remove(processName);

        if (removed)
        {
            Save();
            Trace.WriteLine($"[Harbor] ColorOverrideService: Removed override for {processName}");
        }
    }

    /// <summary>
    /// Returns all current overrides as a read-only dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (_lock)
            return new Dictionary<string, string>(_overrides, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the number of overrides.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _overrides.Count;
        }
    }

    internal static Color? ParseHexColor(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;

        try
        {
            var converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color color)
                return color;
        }
        catch
        {
            // Invalid hex
        }

        return null;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Trace.WriteLine("[Harbor] ColorOverrideService: No config file, starting with empty overrides.");
                return;
            }

            var json = File.ReadAllText(_configPath);
            var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(json, s_jsonOptions);
            if (overrides is not null)
            {
                lock (_lock)
                {
                    _overrides.Clear();
                    foreach (var kvp in overrides)
                        _overrides[kvp.Key] = kvp.Value;
                }
            }

            Trace.WriteLine($"[Harbor] ColorOverrideService: Loaded {_overrides.Count} overrides.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ColorOverrideService: Failed to load config: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Dictionary<string, string> snapshot;
            lock (_lock)
                snapshot = new Dictionary<string, string>(_overrides, StringComparer.OrdinalIgnoreCase);

            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            File.WriteAllText(_configPath, json);

            Trace.WriteLine($"[Harbor] ColorOverrideService: Saved {snapshot.Count} overrides to {_configPath}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ColorOverrideService: Failed to save config: {ex.Message}");
        }
    }

    private static string GetDefaultConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Harbor", "color-overrides.json");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
