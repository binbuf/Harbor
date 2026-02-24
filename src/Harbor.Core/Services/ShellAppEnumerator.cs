using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        // Extract icon via IShellItemImageFactory, with placeholder fallback
        var icon = ExtractIcon(item) ?? GeneratePlaceholderIcon(displayName);

        return new AppInfo
        {
            DisplayName = displayName,
            ExecutablePath = launchPath,
            Icon = icon,
        };
    }

    private static ImageSource? ExtractIcon(IShellItem item)
    {
        try
        {
            if (item is not IShellItemImageFactory imageFactory)
                return null;

            var size = new SIZE { cx = 48, cy = 48 };

            // Try RESIZETOFIT first, then fall back to BIGGERSIZEOK
            var hr = imageFactory.GetImage(size, SIIGBF.RESIZETOFIT, out var hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
                hr = imageFactory.GetImage(size, SIIGBF.BIGGERSIZEOK, out hBitmap);

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

                // Reject generic/blank document icons returned by the shell
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
            Trace.WriteLine($"[Harbor] ShellAppEnumerator: Icon extraction failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Detects generic/blank icons (e.g. the Windows "blank document" icon) by checking
    /// pixel color diversity. Real app icons have many distinct colors; generic icons
    /// like the blank page have very few (white body, gray border, maybe a blue fold).
    /// </summary>
    private static bool IsLikelyGenericIcon(BitmapSource source)
    {
        try
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);

            // Count unique colors quantized to 4 bits per channel to reduce anti-aliasing noise
            var colors = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 20) continue; // skip transparent

                int quantized = ((pixels[i + 2] & 0xF0) << 4) |
                                (pixels[i + 1] & 0xF0) |
                                (pixels[i] >> 4);
                colors.Add(quantized);

                if (colors.Count >= 12) return false; // diverse enough = real icon
            }

            // Too few distinct colors — likely a generic blank document icon
            return true;
        }
        catch
        {
            return false; // if analysis fails, assume icon is fine
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
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

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
