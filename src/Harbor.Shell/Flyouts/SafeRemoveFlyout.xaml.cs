using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Harbor.Core.Services;

namespace Harbor.Shell.Flyouts;

public partial class SafeRemoveFlyout : Window
{
    private readonly SafeRemoveService _safeRemoveService;
    private FlyoutMouseHook? _mouseHook;

    public SafeRemoveFlyout(SafeRemoveService safeRemoveService)
    {
        InitializeComponent();

        _safeRemoveService = safeRemoveService;

        UpdateDeviceList();

        // Subscribe to live changes
        _safeRemoveService.DevicesChanged += OnDevicesChanged;

        // Clamp position to monitor bounds once layout is known
        ContentRendered += OnContentRendered;

        // Install global mouse hook to dismiss on click-outside
        Loaded += (_, _) => _mouseHook = new FlyoutMouseHook(this, Close);
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        ClampToMonitor();
    }

    private void ClampToMonitor()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)(Left * DpiScale), (int)(Top * DpiScale)));
        var workArea = screen.WorkingArea;

        var dpi = DpiScale;
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

    private double DpiScale
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateDeviceList);
    }

    private void UpdateDeviceList()
    {
        var devices = _safeRemoveService.EjectableDevices;
        DeviceList.ItemsSource = devices;
        DeviceList.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DeviceRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EjectableDevice device }) return;

        var success = _safeRemoveService.EjectDevice(device);
        if (!success)
        {
            Trace.WriteLine($"[Harbor] SafeRemoveFlyout: Failed to eject '{device.Name}'.");
        }

        Close();
    }

    private void DeviceRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)FindResource("FlyoutItemHoverBackground");
        }
    }

    private void DeviceRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _safeRemoveService.DevicesChanged -= OnDevicesChanged;
        base.OnClosed(e);
    }
}
