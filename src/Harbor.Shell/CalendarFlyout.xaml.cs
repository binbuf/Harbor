using System.Globalization;
using System.Windows;

namespace Harbor.Shell;

public partial class CalendarFlyout : Window
{
    public CalendarFlyout()
    {
        InitializeComponent();

        TodayDateText.Text = DateTime.Now.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        FlyoutCalendar.SelectedDate = DateTime.Today;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        Close();
    }
}
