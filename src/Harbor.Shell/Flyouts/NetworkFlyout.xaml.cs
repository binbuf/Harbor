using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Harbor.Core.Services;

namespace Harbor.Shell.Flyouts;

public partial class NetworkFlyout : Window
{
    private readonly NetworkService _networkService;
    private FlyoutMouseHook? _mouseHook;

    public NetworkFlyout(NetworkService networkService)
    {
        InitializeComponent();

        _networkService = networkService;

        // Set initial state
        UpdateConnectionDisplay();
        UpdateNetworkList();

        // Subscribe to live changes
        _networkService.NetworkChanged += OnNetworkChanged;
        _networkService.AvailableNetworksChanged += OnAvailableNetworksChanged;

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

    private void OnNetworkChanged(object? sender, NetworkChangedEventArgs e)
    {
        Dispatcher.Invoke(UpdateConnectionDisplay);
    }

    private void OnAvailableNetworksChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdateNetworkList);
    }

    private void UpdateConnectionDisplay()
    {
        var type = _networkService.ConnectionType;

        if (type == NetworkConnectionType.Ethernet)
        {
            HeaderText.Text = "Ethernet";
            ConnectionStatusText.Text = "Connected via Ethernet";
            NetworkNameText.Visibility = Visibility.Collapsed;

            // Hide network list for Ethernet
            NetworksLabel.Visibility = Visibility.Collapsed;
            NetworkList.Visibility = Visibility.Collapsed;
            NoNetworksText.Visibility = Visibility.Collapsed;
        }
        else if (type == NetworkConnectionType.WiFi)
        {
            HeaderText.Text = "Wi-Fi";
            ConnectionStatusText.Text = "Connected";
            NetworkNameText.Text = _networkService.WiFiNetworkName ?? "Unknown Network";
            NetworkNameText.Visibility = Visibility.Visible;

            NetworksLabel.Visibility = Visibility.Visible;
        }
        else
        {
            HeaderText.Text = "Wi-Fi";
            ConnectionStatusText.Text = "Not Connected";
            NetworkNameText.Visibility = Visibility.Collapsed;

            NetworksLabel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateNetworkList()
    {
        var networks = _networkService.AvailableNetworks;
        // Filter out the currently connected network from "Other Networks"
        var otherNetworks = networks.Where(n => !n.IsConnected).ToList();

        if (otherNetworks.Count > 0)
        {
            NetworkList.ItemsSource = otherNetworks;
            NetworkList.Visibility = Visibility.Visible;
            NoNetworksText.Visibility = Visibility.Collapsed;
        }
        else
        {
            NetworkList.ItemsSource = null;
            NetworkList.Visibility = Visibility.Collapsed;

            // Only show "no networks" if we're in Wi-Fi mode
            if (_networkService.ConnectionType != NetworkConnectionType.Ethernet)
                NoNetworksText.Visibility = Visibility.Visible;
        }
    }

    private void NetworkRow_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = (Brush)FindResource("FlyoutItemHoverBackground");
        }
    }

    private void NetworkRow_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
        }
    }

    private void NetworkRow_Click(object sender, MouseButtonEventArgs e)
    {
        // Open Windows Wi-Fi settings as a fallback since programmatic WPA
        // connection requires credentials UI
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-wifi") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] NetworkFlyout: Failed to open Wi-Fi Settings: {ex.Message}");
        }
        Close();
    }

    private void NetworkSettings_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var settingsUri = _networkService.ConnectionType == NetworkConnectionType.Ethernet
                ? "ms-settings:network-ethernet"
                : "ms-settings:network-wifi";
            Process.Start(new ProcessStartInfo(settingsUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] NetworkFlyout: Failed to open Network Settings: {ex.Message}");
        }
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _networkService.NetworkChanged -= OnNetworkChanged;
        _networkService.AvailableNetworksChanged -= OnAvailableNetworksChanged;
        base.OnClosed(e);
    }
}
