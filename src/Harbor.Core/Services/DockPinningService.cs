using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Harbor.Core.Services;

/// <summary>
/// Manages pinned dock applications. Persists pin list to a JSON file
/// at %LOCALAPPDATA%\Harbor\dock-pins.json.
/// </summary>
public class DockPinningService : IDisposable
{
    private readonly string _configPath;
    private readonly List<DockPin> _pins = [];
    private readonly object _lock = new();
    private bool _disposed;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DockPinningService()
        : this(GetDefaultConfigPath())
    {
    }

    public DockPinningService(string configPath)
    {
        _configPath = configPath;
        Load();
    }

    /// <summary>
    /// Current pinned applications, in display order.
    /// </summary>
    public IReadOnlyList<DockPin> Pins
    {
        get
        {
            lock (_lock)
                return _pins.ToList();
        }
    }

    /// <summary>
    /// Returns true if the given executable path is pinned.
    /// </summary>
    public bool IsPinned(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return false;
        lock (_lock)
            return _pins.Any(p => string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Pins an application to the dock. No-op if already pinned.
    /// </summary>
    public void Pin(string executablePath, string? displayName = null)
    {
        if (string.IsNullOrEmpty(executablePath)) return;

        lock (_lock)
        {
            if (_pins.Any(p => string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)))
                return;

            _pins.Add(new DockPin
            {
                ExecutablePath = executablePath,
                DisplayName = displayName ?? ForegroundWindowService.GetFriendlyNameFromPath(executablePath),
            });
        }

        Save();
        PinsChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine($"[Harbor] DockPinningService: Pinned {executablePath}");
    }

    /// <summary>
    /// Pins an application at a specific index. No-op if already pinned.
    /// </summary>
    public void PinAt(int index, string executablePath, string? displayName = null)
    {
        if (string.IsNullOrEmpty(executablePath)) return;

        lock (_lock)
        {
            if (_pins.Any(p => string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)))
                return;

            var pin = new DockPin
            {
                ExecutablePath = executablePath,
                DisplayName = displayName ?? ForegroundWindowService.GetFriendlyNameFromPath(executablePath),
            };
            index = Math.Clamp(index, 0, _pins.Count);
            _pins.Insert(index, pin);
        }

        Save();
        PinsChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine($"[Harbor] DockPinningService: Pinned {executablePath} at index {index}");
    }

    /// <summary>
    /// Moves a pinned item from one position to another.
    /// </summary>
    public void Reorder(int fromIndex, int toIndex)
    {
        lock (_lock)
        {
            if (fromIndex < 0 || fromIndex >= _pins.Count) return;
            toIndex = Math.Clamp(toIndex, 0, _pins.Count - 1);
            if (fromIndex == toIndex) return;

            var item = _pins[fromIndex];
            _pins.RemoveAt(fromIndex);
            _pins.Insert(toIndex, item);
        }

        Save();
        PinsChanged?.Invoke(this, EventArgs.Empty);
        Trace.WriteLine($"[Harbor] DockPinningService: Reordered from {fromIndex} to {toIndex}");
    }

    /// <summary>
    /// Unpins an application from the dock. No-op if not pinned.
    /// </summary>
    public void Unpin(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return;

        bool removed;
        lock (_lock)
        {
            removed = _pins.RemoveAll(p =>
                string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            Save();
            PinsChanged?.Invoke(this, EventArgs.Empty);
            Trace.WriteLine($"[Harbor] DockPinningService: Unpinned {executablePath}");
        }
    }

    /// <summary>
    /// Raised when the pin list changes (pin or unpin).
    /// </summary>
    public event EventHandler? PinsChanged;

    private void Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                Trace.WriteLine("[Harbor] DockPinningService: No config file, starting with empty pins.");
                return;
            }

            var json = File.ReadAllText(_configPath);
            var pins = JsonSerializer.Deserialize<List<DockPin>>(json, s_jsonOptions);
            if (pins is not null)
            {
                lock (_lock)
                {
                    _pins.Clear();
                    _pins.AddRange(pins);
                }
            }

            Trace.WriteLine($"[Harbor] DockPinningService: Loaded {_pins.Count} pinned apps.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] DockPinningService: Failed to load config: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            List<DockPin> snapshot;
            lock (_lock)
                snapshot = _pins.ToList();

            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            File.WriteAllText(_configPath, json);

            Trace.WriteLine($"[Harbor] DockPinningService: Saved {snapshot.Count} pins to {_configPath}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] DockPinningService: Failed to save config: {ex.Message}");
        }
    }

    private static string GetDefaultConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Harbor", "dock-pins.json");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a pinned application in the dock.
/// </summary>
public class DockPin
{
    public required string ExecutablePath { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
