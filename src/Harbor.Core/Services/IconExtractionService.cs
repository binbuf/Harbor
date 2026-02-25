using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Harbor.Core.Services;

/// <summary>
/// Extracts high-resolution application icons using multiple fallback strategies.
/// Thread-safe: all public methods can be called from any thread.
/// </summary>
public class IconExtractionService
{
    private readonly record struct CacheEntry(ImageSource Icon, DateTime LastModified);

    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();
    private readonly ImageSource _defaultIcon;
    private ImageSource? _appsLauncherIcon;

    public IconExtractionService()
    {
        _defaultIcon = CreateDefaultIcon();
        _defaultIcon.Freeze();
    }

    /// <summary>
    /// Gets the icon for an executable path with caching and fallback chain.
    /// Returns a frozen ImageSource safe for use on any thread.
    /// </summary>
    /// <summary>
    /// Sentinel path used by the dock for the Apps launcher icon.
    /// </summary>
    public const string AppsLauncherSentinel = "harbor:apps-launcher";

    public ImageSource GetIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return _defaultIcon;

        // Special-case: return built-in grid icon for the Apps launcher
        if (string.Equals(exePath, AppsLauncherSentinel, StringComparison.OrdinalIgnoreCase))
            return _appsLauncherIcon ??= CreateAppsLauncherIcon();

        // Check cache (with timestamp invalidation)
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(exePath, out var cached))
            {
                var currentModified = GetLastModified(exePath);
                if (currentModified == cached.LastModified)
                    return cached.Icon;

                _cache.Remove(exePath);
            }
        }

        var icon = ExtractIcon(exePath);
        var lastModified = GetLastModified(exePath);

        lock (_cacheLock)
        {
            _cache[exePath] = new CacheEntry(icon, lastModified);
        }

        return icon;
    }

    /// <summary>
    /// Clears the icon cache.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    private ImageSource ExtractIcon(string exePath)
    {
        // Strategy 1: SHGetFileInfo
        var icon = TryExtractViaSHGetFileInfo(exePath);
        if (icon is not null && !IsGenericIcon(icon))
            return icon;

        // Strategy 2: ExtractIconEx (resource table)
        icon = TryExtractViaExtractIconEx(exePath);
        if (icon is not null)
            return icon;

        // Strategy 3: UWP/Store app manifest
        icon = TryExtractUwpIcon(exePath);
        if (icon is not null)
            return icon;

        // Strategy 4: SHGetFileInfo result (even if generic) before ultimate fallback
        icon = TryExtractViaSHGetFileInfo(exePath);

        return icon ?? _defaultIcon;
    }

    private static ImageSource? TryExtractViaSHGetFileInfo(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
                return null;

            var info = new NativeIcon.SHFILEINFO();
            var result = NativeIcon.SHGetFileInfo(
                exePath,
                0,
                ref info,
                (uint)Marshal.SizeOf<NativeIcon.SHFILEINFO>(),
                NativeIcon.SHGFI_ICON | NativeIcon.SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeIcon.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] IconExtraction: SHGetFileInfo failed for {exePath}: {ex.Message}");
            return null;
        }
    }

    private static ImageSource? TryExtractViaExtractIconEx(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
                return null;

            // Get the count of icons
            var count = NativeIcon.ExtractIconEx(exePath, -1, null, null, 0);
            if (count == 0)
                return null;

            var largeIcons = new IntPtr[1];
            var extracted = NativeIcon.ExtractIconEx(exePath, 0, largeIcons, null, 1);

            if (extracted == 0 || largeIcons[0] == IntPtr.Zero)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    largeIcons[0],
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeIcon.DestroyIcon(largeIcons[0]);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] IconExtraction: ExtractIconEx failed for {exePath}: {ex.Message}");
            return null;
        }
    }

    private static ImageSource? TryExtractUwpIcon(string exePath)
    {
        try
        {
            var appDir = Path.GetDirectoryName(exePath);
            if (appDir is null)
                return null;

            var manifestPath = Path.Combine(appDir, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
                return null;

            var doc = XDocument.Load(manifestPath);
            var ns = doc.Root?.GetDefaultNamespace();
            if (ns is null)
                return null;

            // Look for VisualElements in default or uap namespace
            var visualElements = doc.Descendants(ns + "VisualElements").FirstOrDefault()
                ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "VisualElements");

            if (visualElements is null)
                return null;

            // Prefer higher resolution
            var logoPath = visualElements.Attribute("Square150x150Logo")?.Value
                ?? visualElements.Attribute("Square44x44Logo")?.Value;

            if (string.IsNullOrEmpty(logoPath))
                return null;

            var fullLogoPath = Path.Combine(appDir, logoPath);

            // UWP often has scaled variants (e.g., Logo.scale-200.png)
            if (!File.Exists(fullLogoPath))
            {
                var dir = Path.GetDirectoryName(fullLogoPath);
                var baseName = Path.GetFileNameWithoutExtension(fullLogoPath);
                var ext = Path.GetExtension(fullLogoPath);

                if (dir is not null)
                {
                    var candidates = Directory.GetFiles(dir, $"{baseName}*{ext}")
                        .OrderByDescending(f => f)
                        .ToArray();

                    if (candidates.Length > 0)
                        fullLogoPath = candidates[0];
                }
            }

            if (!File.Exists(fullLogoPath))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(fullLogoPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] IconExtraction: UWP icon extraction failed for {exePath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Heuristic: icons 16x16 or smaller are likely generic/small shell icons.
    /// </summary>
    private static bool IsGenericIcon(ImageSource icon)
    {
        if (icon is BitmapSource bitmap)
            return bitmap.PixelWidth <= 16 && bitmap.PixelHeight <= 16;

        return false;
    }

    private static DateTime GetLastModified(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static ImageSource CreateDefaultIcon()
    {
        var visual = new DrawingVisual();
        const double size = 48;

        using (var ctx = visual.RenderOpen())
        {
            var bgBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
            bgBrush.Freeze();
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)), 1);
            pen.Freeze();
            ctx.DrawRoundedRectangle(bgBrush, pen,
                new Rect(2, 2, size - 4, size - 4), 8, 8);

            var whiteBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
            whiteBrush.Freeze();
            ctx.DrawRoundedRectangle(null, new Pen(whiteBrush, 2),
                new Rect(14, 14, 20, 18), 2, 2);

            ctx.DrawLine(new Pen(whiteBrush, 1.5), new Point(14, 20), new Point(34, 20));
        }

        var renderTarget = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
        renderTarget.Render(visual);
        renderTarget.Freeze();
        return renderTarget;
    }

    /// <summary>
    /// Loads the Apps launcher icon from the embedded resource PNG.
    /// Falls back to a simple default icon if the resource is unavailable.
    /// </summary>
    private static ImageSource CreateAppsLauncherIcon()
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri("pack://application:,,,/Harbor.Shell;component/Assets/apps-drawer.png", UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return CreateDefaultIcon();
        }
    }

    /// <summary>
    /// Manual P/Invoke declarations for icon extraction APIs.
    /// SHGetFileInfo is not generated by CsWin32 for AnyCPU (PInvoke005).
    /// </summary>
    private static class NativeIcon
    {
        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_LARGEICON = 0x000000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbSizeFileInfo,
            uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconEx(
            string lpszFile,
            int nIconIndex,
            IntPtr[]? phiconLarge,
            IntPtr[]? phiconSmall,
            uint nIcons);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
