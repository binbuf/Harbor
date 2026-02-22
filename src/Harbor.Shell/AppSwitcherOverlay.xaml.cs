using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

public partial class AppSwitcherOverlay : Window
{
    private List<AppSwitcherService.AppEntry> _apps = [];
    private int _selectedIndex;

    public AppSwitcherOverlay()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Make this window non-activatable so it doesn't steal focus
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = WindowInterop.GetWindowLongPtr(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (nint)(WindowInterop.WS_EX_NOACTIVATE | WindowInterop.WS_EX_TOOLWINDOW);
        WindowInterop.SetWindowLongPtr(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
    }

    public void ShowWithApps(List<AppSwitcherService.AppEntry> apps, int selectedIndex)
    {
        _apps = apps;
        _selectedIndex = selectedIndex;
        AppList.ItemsSource = _apps;
        UpdateSelection();
        Show();
    }

    public void UpdateSelectedIndex(int index)
    {
        _selectedIndex = index;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        // Update visual selection by iterating containers
        Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < _apps.Count; i++)
            {
                var container = AppList.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container is null) continue;

                var border = FindChild<Border>(container);
                if (border is null) continue;

                if (i == _selectedIndex)
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                    border.BorderBrush = (Brush?)FindResource("AppSwitcherAccentBrush") ??
                                         new SolidColorBrush(Color.FromRgb(0, 120, 212));
                    border.BorderThickness = new Thickness(2);
                }
                else
                {
                    border.Background = Brushes.Transparent;
                    border.BorderBrush = null;
                    border.BorderThickness = new Thickness(0);
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var nested = FindChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
