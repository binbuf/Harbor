using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ManagedShell.WindowsTasks;

namespace Harbor.Shell;

public partial class DockWindowPickerPopup : Window
{
    private readonly List<WindowEntry> _entries = [];

    public DockWindowPickerPopup()
    {
        InitializeComponent();
        Deactivated += (_, _) => Close();
    }

    public void Populate(List<ApplicationWindow> windows)
    {
        _entries.Clear();
        foreach (var w in windows)
        {
            _entries.Add(new WindowEntry
            {
                Title = string.IsNullOrWhiteSpace(w.Title) ? "(Untitled)" : w.Title,
                Window = w,
            });
        }
        WindowList.ItemsSource = _entries;
    }

    public void ShowCenteredAbove(FrameworkElement target)
    {
        // Measure desired size
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var targetPos = target.PointToScreen(new Point(target.ActualWidth / 2, 0));
        Left = targetPos.X - DesiredSize.Width / 2;
        Top = targetPos.Y - DesiredSize.Height - 8;
        Show();
    }

    private void WindowItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WindowEntry entry })
        {
            entry.Window.BringToFront();
            Close();
        }
    }

    private void WindowItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
    }

    private void WindowItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
            border.Background = Brushes.Transparent;
    }

    private class WindowEntry
    {
        public required string Title { get; set; }
        public required ApplicationWindow Window { get; set; }
    }
}
