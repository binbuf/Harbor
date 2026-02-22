using System.Diagnostics;
using Microsoft.Win32;

namespace Harbor.Core.Services;

/// <summary>
/// How the wallpaper should be displayed.
/// </summary>
public enum WallpaperStyle
{
    Center,
    Tile,
    Stretch,
    Fit,
    Fill,
    Span,
}

/// <summary>
/// Current wallpaper configuration read from the registry.
/// </summary>
public sealed record WallpaperInfo(
    string? ImagePath,
    WallpaperStyle Style,
    byte BackgroundR,
    byte BackgroundG,
    byte BackgroundB);

/// <summary>
/// Reads the current desktop wallpaper path and style from the Windows registry
/// and notifies subscribers when the wallpaper changes.
/// </summary>
public sealed class WallpaperService : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Raised when the desktop wallpaper or background color changes.
    /// </summary>
    public event Action<WallpaperInfo>? WallpaperChanged;

    public WallpaperService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Reads the current wallpaper configuration from the registry.
    /// </summary>
    public WallpaperInfo GetCurrentWallpaper()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var imagePath = key?.GetValue("Wallpaper") as string;
            var styleStr = key?.GetValue("WallpaperStyle") as string ?? "0";
            var tileStr = key?.GetValue("TileWallpaper") as string ?? "0";

            var style = MapStyle(styleStr, tileStr);

            // Read solid background color fallback
            var (r, g, b) = ReadBackgroundColor();

            return new WallpaperInfo(imagePath, style, r, g, b);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] WallpaperService: Failed to read wallpaper: {ex.Message}");
            return new WallpaperInfo(null, WallpaperStyle.Fill, 0, 0, 0);
        }
    }

    private static WallpaperStyle MapStyle(string style, string tile)
    {
        return style switch
        {
            "22" => WallpaperStyle.Span,
            "10" => WallpaperStyle.Fill,
            "6" => WallpaperStyle.Fit,
            "2" => WallpaperStyle.Stretch,
            "0" when tile == "1" => WallpaperStyle.Tile,
            _ => WallpaperStyle.Center,
        };
    }

    private static (byte R, byte G, byte B) ReadBackgroundColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
            var colorStr = key?.GetValue("Background") as string;
            if (colorStr is not null)
            {
                var parts = colorStr.Split(' ');
                if (parts.Length >= 3 &&
                    byte.TryParse(parts[0], out var r) &&
                    byte.TryParse(parts[1], out var g) &&
                    byte.TryParse(parts[2], out var b))
                {
                    return (r, g, b);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] WallpaperService: Failed to read background color: {ex.Message}");
        }
        return (0, 0, 0);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.Category == UserPreferenceCategory.Desktop)
        {
            Trace.WriteLine("[Harbor] WallpaperService: Desktop preference changed, reloading wallpaper.");
            WallpaperChanged?.Invoke(GetCurrentWallpaper());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
