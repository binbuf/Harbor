using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Harbor.Core.Services;

/// <summary>
/// Samples the wallpaper image to determine whether the area behind the menu bar
/// is predominantly light or dark. Used by the "Dynamic Color" setting to
/// switch menu bar text between white and black for readability.
/// </summary>
public class WallpaperBrightnessService : IDisposable
{
    private readonly WallpaperService _wallpaperService;
    private bool _disposed;

    /// <summary>
    /// True if the wallpaper region behind the menu bar is light (text should be black).
    /// False if dark (text should be white).
    /// </summary>
    public bool IsLightBackground { get; private set; }

    /// <summary>
    /// Average color of the top ~5% of the wallpaper (the region behind the menu bar).
    /// Used for dynamic acrylic tinting.
    /// </summary>
    public Color DominantColor { get; private set; } = Color.FromRgb(0x1E, 0x1E, 0x1E);

    /// <summary>
    /// Raised when the brightness determination changes (e.g. wallpaper changed).
    /// </summary>
    public event Action<bool>? BrightnessChanged;

    public WallpaperBrightnessService(WallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService;
        _wallpaperService.WallpaperChanged += OnWallpaperChanged;
        Refresh();
    }

    private void OnWallpaperChanged(WallpaperInfo _)
    {
        Refresh();
    }

    /// <summary>
    /// Re-samples the wallpaper and updates IsLightBackground.
    /// </summary>
    public void Refresh()
    {
        var info = _wallpaperService.GetCurrentWallpaper();

        double brightness;
        Color dominantColor;

        if (!string.IsNullOrEmpty(info.ImagePath) && File.Exists(info.ImagePath))
        {
            (brightness, dominantColor) = SampleTopRegion(info.ImagePath);
        }
        else
        {
            // No wallpaper image — use the solid background color
            brightness = GetPerceivedBrightness(info.BackgroundR, info.BackgroundG, info.BackgroundB);
            dominantColor = Color.FromRgb(info.BackgroundR, info.BackgroundG, info.BackgroundB);
        }

        var wasLight = IsLightBackground;
        var prevColor = DominantColor;
        IsLightBackground = brightness > 0.5;
        DominantColor = dominantColor;

        Trace.WriteLine($"[Harbor] WallpaperBrightness: brightness={brightness:F2}, isLight={IsLightBackground}, color=#{dominantColor.R:X2}{dominantColor.G:X2}{dominantColor.B:X2}");

        if (IsLightBackground != wasLight || DominantColor != prevColor)
        {
            BrightnessChanged?.Invoke(IsLightBackground);
        }
    }

    /// <summary>
    /// Samples the top ~5% of the wallpaper image (the region behind the menu bar)
    /// and returns the average perceived brightness (0=black, 1=white) and dominant color.
    /// </summary>
    private static (double Brightness, Color DominantColor) SampleTopRegion(string imagePath)
    {
        try
        {
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            // Convert to Bgra32 for easy pixel access
            var converted = new FormatConvertedBitmap(bitmapImage, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            converted.Freeze();

            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var sampleHeight = Math.Max(1, height / 20); // top 5%

            // Read just the top strip of pixels
            var stride = width * 4; // 4 bytes per pixel (BGRA)
            var pixelData = new byte[stride * sampleHeight];
            converted.CopyPixels(new System.Windows.Int32Rect(0, 0, width, sampleHeight), pixelData, stride, 0);

            // Sample ~128 points across the strip
            var stepX = Math.Max(1, width / 32);
            var stepY = Math.Max(1, sampleHeight / 4);

            double totalBrightness = 0;
            long totalR = 0, totalG = 0, totalB = 0;
            int sampleCount = 0;

            for (int y = 0; y < sampleHeight; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    var offset = y * stride + x * 4;
                    var b = pixelData[offset];
                    var g = pixelData[offset + 1];
                    var r = pixelData[offset + 2];
                    totalBrightness += GetPerceivedBrightness(r, g, b);
                    totalR += r;
                    totalG += g;
                    totalB += b;
                    sampleCount++;
                }
            }

            if (sampleCount == 0)
                return (0.5, Color.FromRgb(0x1E, 0x1E, 0x1E));

            var avgBrightness = totalBrightness / sampleCount;
            var dominantColor = Color.FromRgb(
                (byte)(totalR / sampleCount),
                (byte)(totalG / sampleCount),
                (byte)(totalB / sampleCount));

            return (avgBrightness, dominantColor);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] WallpaperBrightness: Failed to sample image: {ex.Message}");
            return (0.5, Color.FromRgb(0x1E, 0x1E, 0x1E)); // assume neutral
        }
    }

    /// <summary>
    /// Perceived brightness using the ITU-R BT.709 luminance formula.
    /// Returns 0.0 (black) to 1.0 (white).
    /// </summary>
    private static double GetPerceivedBrightness(byte r, byte g, byte b)
    {
        return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wallpaperService.WallpaperChanged -= OnWallpaperChanged;
    }
}
