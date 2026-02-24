using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Harbor.Core.Models;

namespace Harbor.Core.Services;

/// <summary>
/// Enumerates installed applications by querying the shell:AppsFolder virtual folder.
/// This uses the same data source as the Windows Start Menu "All Apps" list,
/// producing correctly resolved display names and icons for both Win32 and UWP apps.
/// </summary>
public static class ShellAppEnumerator
{
    private static readonly string[] s_filteredExtensions =
        [".chm", ".txt", ".html", ".htm", ".url", ".pdf", ".rtf", ".msi", ".ini"];

    private static readonly string[] s_filteredNamePatterns =
    [
        "uninstall", "readme", "release notes", "license",
        "user's guide", "user guide", "user manual",
        "getting started", "what is new", "frequently asked", "faq",
        "documentation", "report a problem", "error reporter",
        "website", "support center",
    ];

    public static bool IsFilteredOut(string displayName, string parsingName)
    {
        // Filter by parsing name extension (non-executable file targets)
        var ext = Path.GetExtension(parsingName);
        if (!string.IsNullOrEmpty(ext))
        {
            foreach (var filtered in s_filteredExtensions)
            {
                if (ext.Equals(filtered, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Filter by parsing name prefix (web URLs)
        if (parsingName.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            parsingName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;

        // Filter by display name patterns
        foreach (var pattern in s_filteredNamePatterns)
        {
            if (displayName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static List<AppInfo> EnumerateApps(bool filter = false)
    {
        var results = new List<AppInfo>();

        var hr = SHCreateItemFromParsingName(
            "shell:AppsFolder",
            IntPtr.Zero,
            typeof(IShellItem).GUID,
            out var folderItem);

        if (hr != 0 || folderItem is null)
        {
            Trace.WriteLine($"[Harbor] ShellAppEnumerator: Failed to open shell:AppsFolder (HRESULT 0x{hr:X8})");
            return results;
        }

        try
        {
            hr = folderItem.BindToHandler(
                IntPtr.Zero,
                BHID_EnumItems,
                typeof(IEnumShellItems).GUID,
                out var enumObj);

            if (hr != 0 || enumObj is not IEnumShellItems enumItems)
            {
                Trace.WriteLine($"[Harbor] ShellAppEnumerator: Failed to get enumerator (HRESULT 0x{hr:X8})");
                return results;
            }

            try
            {
                while (true)
                {
                    hr = enumItems.Next(1, out var childItem, out var fetched);
                    if (hr != 0 || fetched == 0 || childItem is null)
                        break;

                    try
                    {
                        var app = ProcessShellItem(childItem, filter);
                        if (app is not null)
                            results.Add(app);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(childItem);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumItems);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(folderItem);
        }

        return results;
    }

    private static AppInfo? ProcessShellItem(IShellItem item, bool filter)
    {
        // Get display name (already resolved by the OS — no ms-resource: issues)
        var hr = item.GetDisplayName(SIGDN.NORMALDISPLAY, out var displayNamePtr);
        if (hr != 0 || displayNamePtr == IntPtr.Zero)
            return null;

        var displayName = Marshal.PtrToStringUni(displayNamePtr) ?? string.Empty;
        Marshal.FreeCoTaskMem(displayNamePtr);

        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        // Get parsing name (AUMID for UWP, or file-system path for Win32)
        hr = item.GetDisplayName(SIGDN.DESKTOPABSOLUTEPARSING, out var parsingNamePtr);
        if (hr != 0 || parsingNamePtr == IntPtr.Zero)
            return null;

        var parsingName = Marshal.PtrToStringUni(parsingNamePtr) ?? string.Empty;
        Marshal.FreeCoTaskMem(parsingNamePtr);

        if (string.IsNullOrWhiteSpace(parsingName))
            return null;

        if (filter && IsFilteredOut(displayName, parsingName))
            return null;

        // The parsing name is an AUMID like "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
        // or a path like "C:\...\app.exe". We use shell:AppsFolder\{parsingName} as the
        // launch target, which Process.Start with UseShellExecute handles uniformly.
        var launchPath = $"shell:AppsFolder\\{parsingName}";

        // Multi-strategy icon extraction with fallbacks
        var icon = ExtractIconFromShell(item)
                ?? ExtractIconFallback(parsingName)
                ?? GeneratePlaceholderIcon(displayName);

        return new AppInfo
        {
            DisplayName = displayName,
            ExecutablePath = launchPath,
            Icon = icon,
        };
    }

    #region Icon Extraction

    /// <summary>
    /// Primary: IShellItemImageFactory at 256x256 with ICONONLY, then BIGGERSIZEOK fallback.
    /// Returns null if the result looks like a generic/blank icon.
    /// </summary>
    private static ImageSource? ExtractIconFromShell(IShellItem item)
    {
        try
        {
            if (item is not IShellItemImageFactory imageFactory)
                return null;

            // Request 256x256 — the shell has high-res icons for most apps,
            // and WPF will downscale for display. Requesting small sizes (48)
            // causes some apps to return generic fallback icons.
            var size = new SIZE { cx = 256, cy = 256 };

            // ICONONLY skips thumbnail extraction and goes straight to icon handler.
            // This avoids per-class thumbnail fallbacks that return blank page icons.
            var hr = imageFactory.GetImage(size, SIIGBF.ICONONLY, out var hBitmap);

            if (hr != 0 || hBitmap == IntPtr.Zero)
                hr = imageFactory.GetImage(size, SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out hBitmap);

            if (hr != 0 || hBitmap == IntPtr.Zero)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();

                if (IsLikelyGenericIcon(source))
                    return null;

                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ShellAppEnumerator: Shell icon extraction failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fallback strategies when IShellItemImageFactory returns a generic icon.
    /// Dispatches to UWP manifest parsing or Win32 exe icon extraction based on parsing name format.
    /// </summary>
    private static ImageSource? ExtractIconFallback(string parsingName)
    {
        try
        {
            // UWP/MSIX: parsing name contains '!' (e.g. "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")
            if (parsingName.Contains('!'))
                return TryExtractUwpIcon(parsingName);

            // Win32: parsing name is a file path (e.g. "C:\...\app.exe")
            if (parsingName.Contains('\\') || parsingName.Contains('/'))
                return TryExtractWin32Icon(parsingName);

            return null;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ShellAppEnumerator: Fallback icon extraction failed for {parsingName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves UWP AUMID → PackageFamilyName → install path → AppxManifest.xml → logo asset.
    /// </summary>
    private static ImageSource? TryExtractUwpIcon(string aumid)
    {
        try
        {
            // Parse "PackageFamilyName!AppId" → extract PackageFamilyName
            var bangIndex = aumid.IndexOf('!');
            if (bangIndex <= 0) return null;
            var packageFamilyName = aumid[..bangIndex];

            // Use PackageManager to find the package install location
            var packageManager = new Windows.Management.Deployment.PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty, packageFamilyName);
            var package = packages.FirstOrDefault();
            if (package is null) return null;

            var installPath = package.InstalledLocation.Path;
            var manifestPath = Path.Combine(installPath, "AppxManifest.xml");
            if (!File.Exists(manifestPath)) return null;

            var doc = XDocument.Load(manifestPath);
            var visualElements = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "VisualElements");
            if (visualElements is null) return null;

            // Prefer Square44x44Logo (closest to our display size), then Square150x150Logo
            var logoRelative = visualElements.Attribute("Square44x44Logo")?.Value
                ?? visualElements.Attribute("Square150x150Logo")?.Value;
            if (string.IsNullOrEmpty(logoRelative)) return null;

            var logoPath = Path.Combine(installPath, logoRelative);

            // UWP assets often have scale/targetsize variants instead of the base file
            if (!File.Exists(logoPath))
                logoPath = FindBestScaledVariant(logoPath);

            if (logoPath is null || !File.Exists(logoPath))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ShellAppEnumerator: UWP icon extraction failed for {aumid}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds the best scaled variant for a UWP logo asset.
    /// Looks for .targetsize-{N}_altform-unplated.png and .scale-{N}.png patterns.
    /// </summary>
    private static string? FindBestScaledVariant(string basePath)
    {
        var dir = Path.GetDirectoryName(basePath);
        var baseName = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        if (dir is null) return null;

        if (!Directory.Exists(dir)) return null;

        // Prefer targetsize variants (exact pixel sizes), highest first
        // e.g. Logo.targetsize-256_altform-unplated.png, Logo.targetsize-48.png
        var candidates = Directory.GetFiles(dir, $"{baseName}*{ext}")
            .Where(f => !string.Equals(f, basePath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f =>
            {
                // Extract numeric size from filename for sorting (bigger = better)
                var name = Path.GetFileNameWithoutExtension(f);
                var sizeStart = name.LastIndexOf("-", StringComparison.Ordinal);
                if (sizeStart >= 0)
                {
                    var rest = name[(sizeStart + 1)..];
                    var numEnd = 0;
                    while (numEnd < rest.Length && char.IsDigit(rest[numEnd])) numEnd++;
                    if (numEnd > 0 && int.TryParse(rest[..numEnd], out var size))
                        return size;
                }
                return 0;
            })
            .ToArray();

        return candidates.Length > 0 ? candidates[0] : null;
    }

    /// <summary>
    /// Win32 fallback: extracts icon from exe/lnk target via SHGetFileInfo or ExtractIconEx.
    /// </summary>
    private static ImageSource? TryExtractWin32Icon(string exePath)
    {
        // Resolve .lnk targets
        if (exePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var target = ResolveLnkTarget(exePath);
            if (!string.IsNullOrEmpty(target))
                exePath = target;
        }

        if (!File.Exists(exePath))
            return null;

        // Try ExtractIconEx for best quality (gets the large icon from PE resource table)
        var icon = TryExtractViaExtractIconEx(exePath);
        if (icon is not null)
            return icon;

        // Fall back to SHGetFileInfo
        return TryExtractViaSHGetFileInfo(exePath);
    }

    private static ImageSource? TryExtractViaExtractIconEx(string exePath)
    {
        try
        {
            var count = ExtractIconEx(exePath, -1, null, null, 0);
            if (count == 0) return null;

            var largeIcons = new IntPtr[1];
            var extracted = ExtractIconEx(exePath, 0, largeIcons, null, 1);
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
                DestroyIcon(largeIcons[0]);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ShellAppEnumerator: ExtractIconEx failed for {exePath}: {ex.Message}");
            return null;
        }
    }

    private static ImageSource? TryExtractViaSHGetFileInfo(string exePath)
    {
        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo(
                exePath, 0, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

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
                DestroyIcon(info.hIcon);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ShellAppEnumerator: SHGetFileInfo failed for {exePath}: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveLnkTarget(string lnkPath)
    {
        try
        {
            if (!File.Exists(lnkPath)) return null;

            var shellLink = (IShellLinkW)new CShellLink();
            var persistFile = (IPersistFile)shellLink;
            persistFile.Load(lnkPath, 0);

            var targetBuilder = new StringBuilder(260);
            shellLink.GetPath(targetBuilder, targetBuilder.Capacity, IntPtr.Zero, 0);
            var target = targetBuilder.ToString();
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects generic/blank icons by checking pixel color diversity.
    /// Generic icons (blank document, default app) have very few distinct colors.
    /// </summary>
    private static bool IsLikelyGenericIcon(BitmapSource source)
    {
        try
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            var colors = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 20) continue;

                int quantized = ((pixels[i + 2] & 0xF0) << 4) |
                                (pixels[i + 1] & 0xF0) |
                                (pixels[i] >> 4);
                colors.Add(quantized);

                if (colors.Count >= 12) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ImageSource GeneratePlaceholderIcon(string displayName)
    {
        const int size = 48;
        var visual = new DrawingVisual();

        using (var ctx = visual.RenderOpen())
        {
            var background = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
            ctx.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), 10, 10);

            var letter = displayName.Length > 0 ? char.ToUpper(displayName[0]).ToString() : "?";
            var text = new FormattedText(
                letter,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Variable"),
                24,
                Brushes.White,
                96);
            var origin = new Point((size - text.Width) / 2, (size - text.Height) / 2);
            ctx.DrawText(text, origin);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    #endregion

    /// <summary>
    /// Writes a diagnostic TSV file listing every enumerated app with its display name,
    /// parsing name, and whether an icon was extracted. Useful for reviewing what to filter.
    /// </summary>
    public static void DumpAppList(List<AppInfo> apps, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DisplayName\tParsingName\tHasIcon");

        foreach (var app in apps.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            // Strip the "shell:AppsFolder\" prefix to show the raw parsing name
            var parsingName = app.ExecutablePath.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase)
                ? app.ExecutablePath[@"shell:AppsFolder\".Length..]
                : app.ExecutablePath;

            sb.AppendLine($"{app.DisplayName}\t{parsingName}\t{(app.Icon is not null ? "yes" : "NO")}");
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        Trace.WriteLine($"[Harbor] ShellAppEnumerator: Dumped {apps.Count} apps to {outputPath}");
    }

    #region COM Interop

    private static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    private enum SIGDN : uint
    {
        NORMALDISPLAY = 0x00000000,
        DESKTOPABSOLUTEPARSING = 0x80028000,
    }

    [Flags]
    private enum SIIGBF : uint
    {
        RESIZETOFIT = 0x00000000,
        BIGGERSIZEOK = 0x00000001,
        ICONONLY = 0x00000004,
        SCALEUP = 0x00000100,
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile, int nIconIndex,
        IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // Shell link (IShellLinkW + IPersistFile) for resolving .lnk targets
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 1)] StringBuilder pszFile,
            int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid bhid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object? ppv);

        void GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("70629033-e363-4a28-a567-0db78006e6d7")]
    private interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem? rgelt, out uint pceltFetched);

        void Skip(uint celt);
        void Reset();
        void Clone(out IEnumShellItems ppenum);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    #endregion
}
