using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Harbor.Core.Interop;
using Harbor.Shell.Flyouts;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

public partial class CalendarFlyout : Window
{
    private FlyoutMouseHook? _mouseHook;

    // Dark frosted glass tint: #1E1E1E at ~70% opacity (AABBGGRR)
    private const uint AcrylicTintColor = 0xB01E1E1E;

    public CalendarFlyout()
    {
        InitializeComponent();

        TodayDateText.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        FlyoutCalendar.SelectedDate = DateTime.Today;

        Loaded += (_, _) => _mouseHook = new FlyoutMouseHook(this, Close);
        ContentRendered += OnContentRendered;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyAcrylic();
    }

    private void ApplyAcrylic()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var result = CompositionInterop.EnableAcrylic(new HWND(hwnd), AcrylicTintColor);
        if (result)
        {
            // Let the DWM acrylic blur show through
            FlyoutBorder.Background = Brushes.Transparent;
        }
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        ClampToMonitor();
    }

    private void ClampToMonitor()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)(Left * dpi), (int)(Top * dpi)));
        var workArea = screen.WorkingArea;

        var waLeft = workArea.Left / dpi;
        var waTop = workArea.Top / dpi;
        var waRight = workArea.Right / dpi;
        var waBottom = workArea.Bottom / dpi;

        if (Left + ActualWidth > waRight)
            Left = waRight - ActualWidth;
        if (Left < waLeft)
            Left = waLeft;
        if (Top + ActualHeight > waBottom)
            Top = waBottom - ActualHeight;
        if (Top < waTop)
            Top = waTop;
    }

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        base.OnClosed(e);
    }
}
