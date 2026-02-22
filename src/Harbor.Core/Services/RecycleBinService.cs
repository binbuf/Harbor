using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Harbor.Core.Services;

/// <summary>
/// Monitors the Recycle Bin state and provides Open/Empty operations.
/// Polls every 5 seconds to detect changes. Provides icons for empty/full states.
/// </summary>
public sealed class RecycleBinService : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer _pollTimer;
    private bool _hasItems;
    private ImageSource? _currentIcon;
    private ImageSource? _emptyIcon;
    private ImageSource? _fullIcon;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasItems
    {
        get => _hasItems;
        private set
        {
            if (_hasItems == value) return;
            _hasItems = value;
            CurrentIcon = value ? _fullIcon : _emptyIcon;
            OnPropertyChanged();
        }
    }

    public ImageSource? CurrentIcon
    {
        get => _currentIcon;
        private set
        {
            if (_currentIcon == value) return;
            _currentIcon = value;
            OnPropertyChanged();
        }
    }

    public RecycleBinService()
    {
        LoadIcons();
        CheckState();

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _pollTimer.Tick += (_, _) => CheckState();
        _pollTimer.Start();

        Trace.WriteLine("[Harbor] RecycleBinService: Started polling.");
    }

    public void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:RecycleBinFolder",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] RecycleBinService: Failed to open: {ex.Message}");
        }
    }

    public void Empty()
    {
        var result = MessageBox.Show(
            "Are you sure you want to permanently delete all items in the Recycle Bin?",
            "Empty Recycle Bin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                // SHEmptyRecycleBin flags: SHERB_NOCONFIRMATION=1, SHERB_NOPROGRESSUI=2, SHERB_NOSOUND=4
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x0007);
                CheckState();
                Trace.WriteLine("[Harbor] RecycleBinService: Recycle bin emptied.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] RecycleBinService: Failed to empty: {ex.Message}");
            }
        }
    }

    private void CheckState()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<SHQUERYRBINFO>() };
            var hr = SHQueryRecycleBin(null, ref info);
            if (hr == 0)
            {
                HasItems = info.i64NumItems > 0;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] RecycleBinService: Poll failed: {ex.Message}");
        }
    }

    private void LoadIcons()
    {
        _emptyIcon = ExtractStockIcon(SIID_RECYCLER);
        _fullIcon = ExtractStockIcon(SIID_RECYCLERFULL);

        // Fallback if stock icon extraction fails
        _emptyIcon ??= CreateFallbackIcon(false);
        _fullIcon ??= CreateFallbackIcon(true);

        _currentIcon = _hasItems ? _fullIcon : _emptyIcon;
    }

    private static ImageSource? ExtractStockIcon(uint siid)
    {
        try
        {
            var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
            var hr = SHGetStockIconInfo(siid, SHGSI_ICON | SHGSI_LARGEICON, ref info);
            if (hr != 0 || info.hIcon == IntPtr.Zero) return null;

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
        catch
        {
            return null;
        }
    }

    private static ImageSource CreateFallbackIcon(bool hasItems)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var brush = new SolidColorBrush(hasItems ? Color.FromRgb(0x80, 0x80, 0x80) : Color.FromRgb(0x60, 0x60, 0x60));
            brush.Freeze();
            ctx.DrawRoundedRectangle(brush, null, new Rect(4, 8, 40, 36), 4, 4);
        }
        var bitmap = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Stop();
    }

    // P/Invoke declarations
    private const uint SIID_RECYCLER = 31;
    private const uint SIID_RECYCLERFULL = 32;
    private const uint SHGSI_ICON = 0x000000100;
    private const uint SHGSI_LARGEICON = 0x000000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public uint cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll")]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? pszRootPath, uint dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
