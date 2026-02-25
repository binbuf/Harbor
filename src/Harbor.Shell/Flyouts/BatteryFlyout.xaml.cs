using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Harbor.Core.Services;

namespace Harbor.Shell.Flyouts;

public partial class BatteryFlyout : Window
{
    private readonly BatteryService _batteryService;
    private FlyoutMouseHook? _mouseHook;

    public BatteryFlyout(BatteryService batteryService)
    {
        InitializeComponent();

        _batteryService = batteryService;

        // Set initial state
        UpdateDisplay();

        // Subscribe to live changes
        _batteryService.BatteryChanged += OnBatteryChanged;

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

    private void OnBatteryChanged(object? sender, BatteryChangedEventArgs e)
    {
        Dispatcher.Invoke(UpdateDisplay);
    }

    private void UpdateDisplay()
    {
        PercentageText.Text = $"{_batteryService.ChargePercent}%";

        StatusText.Text = _batteryService.IsCharging
            ? "Charging"
            : _batteryService.PowerSource == PowerSource.AC
                ? "Fully Charged"
                : "On Battery Power";

        if (!_batteryService.IsCharging && _batteryService.EstimatedMinutesRemaining is { } minutes && minutes > 0)
        {
            TimeRemainingText.Visibility = Visibility.Visible;
            if (minutes >= 60)
            {
                var hours = minutes / 60;
                var remainingMinutes = minutes % 60;
                TimeRemainingText.Text = remainingMinutes > 0
                    ? $"About {hours} hr {remainingMinutes} min remaining"
                    : $"About {hours} hr remaining";
            }
            else
            {
                TimeRemainingText.Text = $"About {minutes} min remaining";
            }
        }
        else
        {
            TimeRemainingText.Visibility = Visibility.Collapsed;
        }
    }

    private void SettingsRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border)
        {
            border.Background = (Brush)FindResource("FlyoutItemHoverBackground");
        }
    }

    private void SettingsRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border)
        {
            border.Background = Brushes.Transparent;
        }
    }

    private void BatterySettings_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:batterysaver") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] BatteryFlyout: Failed to open Battery Settings: {ex.Message}");
        }
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _batteryService.BatteryChanged -= OnBatteryChanged;
        base.OnClosed(e);
    }
}
