using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Harbor.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;

namespace Harbor.Core.Services;

/// <summary>
/// Describes how the title bar color was detected.
/// </summary>
public enum ColorDetectionMethod
{
    /// <summary>DWM per-window caption color (DWMWA_CAPTION_COLOR).</summary>
    DwmCaptionColor,

    /// <summary>System-wide DWM colorization accent color.</summary>
    DwmColorization,

    /// <summary>Mica or Acrylic system backdrop detected.</summary>
    SystemBackdrop,

    /// <summary>Pixel sampling from the title bar region.</summary>
    PixelSampling,

    /// <summary>User-specified override from config file.</summary>
    UserOverride,

    /// <summary>Default fallback color.</summary>
    Fallback,
}

/// <summary>
/// Result of title bar color detection.
/// </summary>
public sealed class TitleBarColor
{
    public required System.Windows.Media.Color Color { get; init; }
    public required ColorDetectionMethod Method { get; init; }
}

/// <summary>
/// Detects the title bar background color for application windows using a cascading strategy:
/// 1. User override (per-app config)
/// 2. DWM per-window caption color (DWMWA_CAPTION_COLOR)
/// 3. System-wide DWM colorization
/// 4. Mica/Acrylic detection
/// 5. Pixel sampling
/// Caches results per process name. Invalidates on theme change.
/// </summary>
public sealed class TitleBarColorService : IDisposable
{
    // DWMWA_CAPTION_COLOR was added in Windows 11 build 22000
    private const uint DWMWA_CAPTION_COLOR = 35;

    // System backdrop types
    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2; // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4; // Tabbed Mica

    // Pixel sampling region size
    internal const int SampleSize = 8;

    private readonly ConcurrentDictionary<string, TitleBarColor> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ColorOverrideService? _overrideService;
    private bool _disposed;

    public TitleBarColorService(ColorOverrideService? overrideService = null)
    {
        _overrideService = overrideService;
        Trace.WriteLine("[Harbor] TitleBarColorService: Initialized.");
    }

    /// <summary>
    /// Returns the number of cached color entries.
    /// </summary>
    public int CacheCount => _cache.Count;

    /// <summary>
    /// Detects the title bar color for the given window.
    /// Uses the cascading priority strategy defined in Design.md Section 9A.
    /// </summary>
    public TitleBarColor Detect(HWND hwnd, RECT titleBarRect)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (hwnd == HWND.Null)
            return MakeFallback();

        // Check user override first (highest priority per task spec Priority 4,
        // but logically should be checked first since it's an explicit user choice)
        var processName = GetProcessName(hwnd);

        if (processName is not null && _overrideService is not null)
        {
            var overrideColor = _overrideService.GetOverride(processName);
            if (overrideColor is not null)
            {
                var result = new TitleBarColor
                {
                    Color = overrideColor.Value,
                    Method = ColorDetectionMethod.UserOverride,
                };
                if (processName is not null) _cache[processName] = result;
                return result;
            }
        }

        // Check cache
        if (processName is not null && _cache.TryGetValue(processName, out var cached))
            return cached;

        // Priority 1: DWM per-window caption color
        var detected = TryDwmCaptionColor(hwnd);

        // Priority 1 (system-wide): DWM colorization
        detected ??= TryDwmColorizationColor();

        // Priority 2: Mica/Acrylic detection
        detected ??= TrySystemBackdropDetection(hwnd);

        // Priority 3: Pixel sampling
        detected ??= TryPixelSampling(hwnd, titleBarRect);

        // Fallback
        detected ??= MakeFallback();

        // Cache by process name
        if (processName is not null)
            _cache[processName] = detected;

        return detected;
    }

    /// <summary>
    /// Clears all cached colors. Called on theme change.
    /// </summary>
    public void InvalidateAll()
    {
        _cache.Clear();
        Trace.WriteLine("[Harbor] TitleBarColorService: Cache invalidated.");
    }

    /// <summary>
    /// Invalidates the cached color for a specific process name.
    /// </summary>
    public void Invalidate(string processName)
    {
        _cache.TryRemove(processName, out _);
    }

    /// <summary>
    /// Priority 1: Query DWM per-window caption color (Windows 11 22H2+).
    /// This returns the actual rendered caption color for the specific window.
    /// </summary>
    internal static TitleBarColor? TryDwmCaptionColor(HWND hwnd)
    {
        var hr = DwmInterop.GetWindowAttribute(
            hwnd,
            (DWMWINDOWATTRIBUTE)DWMWA_CAPTION_COLOR,
            out uint colorRef);

        if (hr.Failed)
            return null;

        // COLORREF is 0x00BBGGRR
        // Value of 0xFFFFFFFF means "use default" — fall through
        if (colorRef == 0xFFFFFFFF)
            return null;

        var r = (byte)(colorRef & 0xFF);
        var g = (byte)((colorRef >> 8) & 0xFF);
        var b = (byte)((colorRef >> 16) & 0xFF);

        return new TitleBarColor
        {
            Color = System.Windows.Media.Color.FromRgb(r, g, b),
            Method = ColorDetectionMethod.DwmCaptionColor,
        };
    }

    /// <summary>
    /// Priority 1 (system-wide): Query DWM colorization color.
    /// Works for apps using the default Windows title bar.
    /// </summary>
    internal static TitleBarColor? TryDwmColorizationColor()
    {
        var hr = DwmInterop.GetColorizationColor(out var argb, out _);
        if (hr.Failed)
            return null;

        // ARGB packed: 0xAARRGGBB
        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);

        return new TitleBarColor
        {
            Color = System.Windows.Media.Color.FromArgb(a, r, g, b),
            Method = ColorDetectionMethod.DwmColorization,
        };
    }

    /// <summary>
    /// Priority 2: Detect Mica/Acrylic backdrop and return an appropriate color.
    /// </summary>
    internal static TitleBarColor? TrySystemBackdropDetection(HWND hwnd)
    {
        var hr = DwmInterop.GetWindowAttribute(
            hwnd,
            (DWMWINDOWATTRIBUTE)DwmInterop.DWMWA_SYSTEMBACKDROP_TYPE,
            out int backdropType);

        if (hr.Failed)
            return null;

        // Only handle Mica and Acrylic — these have translucent title bars
        if (backdropType is DWMSBT_MAINWINDOW or DWMSBT_TABBEDWINDOW)
        {
            // Mica: the title bar takes on the desktop wallpaper color, tinted.
            // Use a semi-transparent color that approximates the Mica appearance.
            // Dark mode: dark gray with alpha, Light mode: light gray with alpha.
            // We'll use the DWM colorization as a base and make it translucent.
            var colorizationResult = TryDwmColorizationColor();
            if (colorizationResult is not null)
            {
                var c = colorizationResult.Color;
                return new TitleBarColor
                {
                    Color = System.Windows.Media.Color.FromArgb(200, c.R, c.G, c.B),
                    Method = ColorDetectionMethod.SystemBackdrop,
                };
            }

            // Fallback Mica approximation
            return new TitleBarColor
            {
                Color = System.Windows.Media.Color.FromArgb(200, 32, 32, 32),
                Method = ColorDetectionMethod.SystemBackdrop,
            };
        }

        if (backdropType == DWMSBT_TRANSIENTWINDOW)
        {
            // Acrylic: translucent blur
            return new TitleBarColor
            {
                Color = System.Windows.Media.Color.FromArgb(180, 32, 32, 32),
                Method = ColorDetectionMethod.SystemBackdrop,
            };
        }

        return null;
    }

    /// <summary>
    /// Priority 3: Capture pixels from the title bar and compute the median color.
    /// Samples an 8x8 region at the right edge of the title bar (just left of where
    /// the native buttons would be).
    /// </summary>
    internal static TitleBarColor? TryPixelSampling(HWND hwnd, RECT titleBarRect)
    {
        var titleWidth = titleBarRect.right - titleBarRect.left;
        var titleHeight = titleBarRect.bottom - titleBarRect.top;

        if (titleWidth <= 0 || titleHeight <= 0)
            return null;

        try
        {
            // Sample region: 8x8 pixels at the right side of the title bar,
            // offset inward to avoid the native buttons themselves.
            // Native buttons are typically ~135px wide, so sample just left of that.
            var sampleX = Math.Max(0, titleWidth - 150);
            var sampleY = Math.Max(0, (titleHeight - SampleSize) / 2);
            var sampleW = Math.Min(SampleSize, titleWidth - sampleX);
            var sampleH = Math.Min(SampleSize, titleHeight - sampleY);

            if (sampleW <= 0 || sampleH <= 0)
                return null;

            // Use PrintWindow to capture the window content (works even if partially occluded)
            using var windowBitmap = new Bitmap(titleWidth, titleHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(windowBitmap))
            {
                var hdc = graphics.GetHdc();
                try
                {
                    // PrintWindow with PW_RENDERFULLCONTENT flag (2) for better DWM capture
                    var success = PrintWindow(hwnd, hdc, 2);
                    if (!success)
                    {
                        // Fallback: try without flag
                        success = PrintWindow(hwnd, hdc, 0);
                    }

                    if (!success)
                        return null;
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            // The bitmap contains the full window — we need to extract just the title bar region
            // PrintWindow captures from the window's top-left, so the title bar is at (0, 0)
            // relative to the window. We need to adjust for the fact that titleBarRect is in
            // screen coords but the bitmap is in window-relative coords.
            if (!WindowInterop.GetWindowRect(hwnd, out var windowRect))
                return null;

            var titleBarRelativeY = titleBarRect.top - windowRect.top;
            sampleY = Math.Max(0, titleBarRelativeY + (titleHeight - SampleSize) / 2);

            // Extract sample pixels
            var colors = new List<(byte R, byte G, byte B)>();
            for (var py = sampleY; py < sampleY + sampleH && py < windowBitmap.Height; py++)
            {
                for (var px = sampleX; px < sampleX + sampleW && px < windowBitmap.Width; px++)
                {
                    var pixel = windowBitmap.GetPixel(px, py);
                    colors.Add((pixel.R, pixel.G, pixel.B));
                }
            }

            if (colors.Count == 0)
                return null;

            var median = ComputeMedianColor(colors);
            return new TitleBarColor
            {
                Color = System.Windows.Media.Color.FromRgb(median.R, median.G, median.B),
                Method = ColorDetectionMethod.PixelSampling,
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] TitleBarColorService: Pixel sampling failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Computes the median color from a list of RGB tuples.
    /// Uses the median of each channel independently.
    /// </summary>
    internal static (byte R, byte G, byte B) ComputeMedianColor(List<(byte R, byte G, byte B)> colors)
    {
        if (colors.Count == 0)
            return (0, 0, 0);

        var rs = colors.Select(c => c.R).OrderBy(v => v).ToList();
        var gs = colors.Select(c => c.G).OrderBy(v => v).ToList();
        var bs = colors.Select(c => c.B).OrderBy(v => v).ToList();

        var mid = colors.Count / 2;

        return (rs[mid], gs[mid], bs[mid]);
    }

    private static TitleBarColor MakeFallback()
    {
        // Default: dark title bar color matching Windows 11 dark mode
        return new TitleBarColor
        {
            Color = System.Windows.Media.Color.FromRgb(32, 32, 32),
            Method = ColorDetectionMethod.Fallback,
        };
    }

    private static string? GetProcessName(HWND hwnd)
    {
        try
        {
            WindowInterop.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0) return null;
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(HWND hwnd, nint hdc, uint flags);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
        Trace.WriteLine("[Harbor] TitleBarColorService: Disposed.");
    }
}
