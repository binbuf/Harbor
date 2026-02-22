using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

public partial class DesktopBackgroundWindow : Window
{
    private WallpaperService? _wallpaperService;

    // WM_WINDOWPOSCHANGING message constant
    private const int WM_WINDOWPOSCHANGING = 0x0046;

    // Offset of hwndInsertAfter in WINDOWPOS struct (IntPtr hwnd, IntPtr hwndInsertAfter, ...)
    private static readonly int HwndInsertAfterOffset = IntPtr.Size; // right after the hwnd field

    public DesktopBackgroundWindow()
    {
        InitializeComponent();
    }

    public void Initialize(WallpaperService wallpaperService)
    {
        _wallpaperService = wallpaperService;
        _wallpaperService.WallpaperChanged += OnWallpaperChanged;
        LoadWallpaper(_wallpaperService.GetCurrentWallpaper());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var hwndSource = HwndSource.FromHwnd(hwnd);

        // Size to primary monitor
        var bounds = DisplayInterop.GetMonitorBounds(new HWND(hwnd));
        if (bounds.HasValue)
        {
            var scale = DisplayInterop.GetScaleFactorForWindow(new HWND(hwnd));
            Left = DisplayInterop.PhysicalToDip(bounds.Value.left, scale);
            Top = DisplayInterop.PhysicalToDip(bounds.Value.top, scale);
            Width = DisplayInterop.PhysicalToDip(bounds.Value.right - bounds.Value.left, scale);
            Height = DisplayInterop.PhysicalToDip(bounds.Value.bottom - bounds.Value.top, scale);
        }

        // Add WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE to prevent taskbar entry and activation stealing
        var exStyle = WindowInterop.GetWindowLongPtr(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (nint)(WindowInterop.WS_EX_TOOLWINDOW | WindowInterop.WS_EX_NOACTIVATE);
        WindowInterop.SetWindowLongPtr(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);

        // Place at HWND_BOTTOM to stay behind all windows
        WindowInterop.SetWindowPos(
            new HWND(hwnd),
            new HWND(1), // HWND_BOTTOM
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        // Hook WndProc to force HWND_BOTTOM on position changes
        hwndSource?.AddHook(WndProc);

        Trace.WriteLine("[Harbor] DesktopBackgroundWindow: Initialized and placed at HWND_BOTTOM.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            // Force the window to stay at HWND_BOTTOM by writing to WINDOWPOS.hwndInsertAfter
            Marshal.WriteIntPtr(lParam, HwndInsertAfterOffset, (IntPtr)1); // HWND_BOTTOM = 1
        }
        return IntPtr.Zero;
    }

    private void OnWallpaperChanged(WallpaperInfo info)
    {
        Dispatcher.Invoke(() => LoadWallpaper(info));
    }

    private void LoadWallpaper(WallpaperInfo info)
    {
        try
        {
            if (string.IsNullOrEmpty(info.ImagePath) || !File.Exists(info.ImagePath))
            {
                // No wallpaper — use solid background color
                WallpaperImage.Source = null;
                BackgroundGrid.Background = new SolidColorBrush(
                    Color.FromRgb(info.BackgroundR, info.BackgroundG, info.BackgroundB));
                Trace.WriteLine($"[Harbor] DesktopBackground: Using solid color ({info.BackgroundR},{info.BackgroundG},{info.BackgroundB}).");
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(info.ImagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            if (info.Style == WallpaperStyle.Tile)
            {
                // Use ImageBrush with tiling for Tile mode
                WallpaperImage.Source = null;
                BackgroundGrid.Background = new ImageBrush(bitmap)
                {
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.None,
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight),
                };
            }
            else
            {
                BackgroundGrid.Background = Brushes.Black;
                WallpaperImage.Source = bitmap;
                WallpaperImage.Stretch = info.Style switch
                {
                    WallpaperStyle.Fill or WallpaperStyle.Span => Stretch.UniformToFill,
                    WallpaperStyle.Fit => Stretch.Uniform,
                    WallpaperStyle.Stretch => Stretch.Fill,
                    WallpaperStyle.Center => Stretch.None,
                    _ => Stretch.UniformToFill,
                };
            }

            Trace.WriteLine($"[Harbor] DesktopBackground: Loaded wallpaper '{info.ImagePath}' with style {info.Style}.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] DesktopBackground: Failed to load wallpaper: {ex.Message}");
            BackgroundGrid.Background = new SolidColorBrush(
                Color.FromRgb(info.BackgroundR, info.BackgroundG, info.BackgroundB));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_wallpaperService is not null)
        {
            _wallpaperService.WallpaperChanged -= OnWallpaperChanged;
            _wallpaperService = null;
        }
        base.OnClosed(e);
    }
}
