using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;
using FontFamily = System.Windows.Media.FontFamily;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Harbor.Shell.Flyouts;

public partial class CalendarFlyout : Window
{
    private enum ViewMode { Day, Month, Decade }

    private FlyoutMouseHook? _mouseHook;

    // Dark frosted glass tint
    private const uint AcrylicTintColor = 0xB01E1E1E;

    // State
    private DateTime _displayMonth;
    private int _displayYear;
    private int _displayDecadeStart;
    private DateTime _selectedDate;
    private ViewMode _viewMode = ViewMode.Day;

    // Cell arrays
    private readonly Border[] _dayCells = new Border[42];
    private readonly Border[] _monthCells = new Border[12];
    private readonly Border[] _yearCells = new Border[12];

    // Reveal highlight
    private RadialGradientBrush? _revealBrush;

    // Clock
    private DispatcherTimer? _clockTimer;

    public CalendarFlyout()
    {
        InitializeComponent();

        _selectedDate = DateTime.Today;
        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _displayYear = DateTime.Today.Year;
        _displayDecadeStart = DateTime.Today.Year / 10 * 10;

        BuildDayGrid();
        BuildMonthGrid();
        BuildDecadeGrid();
        InitRevealHighlight();

        UpdateClock();
        PopulateDayView();

        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _mouseHook = new FlyoutMouseHook(this, Close);

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
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

        // Round the HWND at the DWM level so acrylic is clipped to rounded corners
        var cornerPref = DwmInterop.DWMWCP_ROUND;
        DwmInterop.SetWindowAttribute(
            new HWND(hwnd),
            (Windows.Win32.Graphics.Dwm.DWMWINDOWATTRIBUTE)DwmInterop.DWMWA_WINDOW_CORNER_PREFERENCE,
            in cornerPref);

        var result = CompositionInterop.EnableAcrylic(new HWND(hwnd), AcrylicTintColor);
        if (result)
            FlyoutBorder.Background = Brushes.Transparent;
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        ClampToMonitor();
    }

    private void ClampToMonitor()
    {
        var dpi = DpiScale;
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

    private double DpiScale
    {
        get
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }

    #region Clock

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("h:mm tt", CultureInfo.CurrentCulture);
        DateText.Text = now.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
    }

    #endregion

    #region Grid Building

    private void BuildDayGrid()
    {
        // Day headers
        var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        foreach (var dayName in dayNames)
        {
            var tb = new TextBlock
            {
                Text = dayName[..2],
                FontFamily = new FontFamily("Segoe UI Variable"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };
            DayHeaderGrid.Children.Add(tb);
        }

        // 42 day cells
        for (int i = 0; i < 42; i++)
        {
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Child = tb,
                Cursor = Cursors.Hand
            };

            border.MouseEnter += DayCell_MouseEnter;
            border.MouseLeave += DayCell_MouseLeave;
            border.MouseLeftButtonUp += DayCell_Click;

            _dayCells[i] = border;
            DayGrid.Children.Add(border);
        }
    }

    private void BuildMonthGrid()
    {
        var monthNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedMonthNames;
        for (int i = 0; i < 12; i++)
        {
            var tb = new TextBlock
            {
                Text = monthNames[i],
                FontFamily = new FontFamily("Segoe UI Variable"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Child = tb,
                Cursor = Cursors.Hand,
                Margin = new Thickness(2)
            };

            border.MouseEnter += MonthCell_MouseEnter;
            border.MouseLeave += MonthCell_MouseLeave;
            border.MouseLeftButtonUp += MonthCell_Click;

            _monthCells[i] = border;
            MonthGrid.Children.Add(border);
        }
    }

    private void BuildDecadeGrid()
    {
        for (int i = 0; i < 12; i++)
        {
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI Variable"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                Child = tb,
                Cursor = Cursors.Hand,
                Margin = new Thickness(2)
            };

            border.MouseEnter += YearCell_MouseEnter;
            border.MouseLeave += YearCell_MouseLeave;
            border.MouseLeftButtonUp += YearCell_Click;

            _yearCells[i] = border;
            DecadeGrid.Children.Add(border);
        }
    }

    #endregion

    #region Populate Views

    private void PopulateDayView()
    {
        MonthYearButton.Content = _displayMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

        var firstOfMonth = _displayMonth;
        var startDow = (int)firstOfMonth.DayOfWeek; // 0=Sun

        var today = DateTime.Today;
        var foreground = Brushes.White;
        var inactiveFg = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
        var todayBg = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        var todayFg = Brushes.White;
        var selectedBorder = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));

        for (int i = 0; i < 42; i++)
        {
            var cell = _dayCells[i];
            var tb = (TextBlock)cell.Child;

            var dayOffset = i - startDow;
            var date = firstOfMonth.AddDays(dayOffset);

            tb.Text = date.Day.ToString();
            cell.Tag = date;

            var isCurrentMonth = date.Month == firstOfMonth.Month && date.Year == firstOfMonth.Year;
            var isToday = date.Date == today;
            var isSelected = date.Date == _selectedDate.Date && !isToday;

            // Reset
            cell.Background = Brushes.Transparent;
            cell.BorderBrush = (Brush?)_revealBrush ?? Brushes.Transparent;
            cell.BorderThickness = new Thickness(1);

            if (isToday)
            {
                cell.Background = todayBg;
                tb.Foreground = todayFg;
                cell.BorderBrush = Brushes.Transparent;
            }
            else if (isSelected)
            {
                cell.Background = Brushes.Transparent;
                cell.BorderBrush = selectedBorder;
                cell.BorderThickness = new Thickness(2);
                tb.Foreground = foreground;
            }
            else if (isCurrentMonth)
            {
                tb.Foreground = foreground;
            }
            else
            {
                tb.Foreground = inactiveFg;
            }
        }
    }

    private void PopulateMonthView()
    {
        YearButton.Content = _displayYear.ToString();

        var foreground = Brushes.White;
        var todayBg = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        var todayFg = Brushes.White;
        var today = DateTime.Today;

        for (int i = 0; i < 12; i++)
        {
            var cell = _monthCells[i];
            cell.Tag = i + 1; // month number
            cell.Background = Brushes.Transparent;
            cell.BorderBrush = (Brush?)_revealBrush ?? Brushes.Transparent;

            var tb = (TextBlock)cell.Child;

            if (_displayYear == today.Year && i + 1 == today.Month)
            {
                cell.Background = todayBg;
                tb.Foreground = todayFg;
                cell.BorderBrush = Brushes.Transparent;
            }
            else
            {
                tb.Foreground = foreground;
            }
        }
    }

    private void PopulateDecadeView()
    {
        DecadeButton.Content = $"{_displayDecadeStart} – {_displayDecadeStart + 9}";

        var foreground = Brushes.White;
        var inactiveFg = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
        var todayBg = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        var todayFg = Brushes.White;
        var currentYear = DateTime.Today.Year;

        for (int i = 0; i < 12; i++)
        {
            var year = _displayDecadeStart - 1 + i;
            var cell = _yearCells[i];
            var tb = (TextBlock)cell.Child;
            tb.Text = year.ToString();
            cell.Tag = year;
            cell.Background = Brushes.Transparent;
            cell.BorderBrush = (Brush?)_revealBrush ?? Brushes.Transparent;

            var isInDecade = year >= _displayDecadeStart && year <= _displayDecadeStart + 9;

            if (year == currentYear)
            {
                cell.Background = todayBg;
                tb.Foreground = todayFg;
                cell.BorderBrush = Brushes.Transparent;
            }
            else
            {
                tb.Foreground = isInDecade ? foreground : inactiveFg;
            }
        }
    }

    #endregion

    #region Cell Interaction

    private void DayCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border cell && cell.Tag is DateTime date)
        {
            var isToday = date.Date == DateTime.Today;
            if (!isToday)
                cell.Background = new SolidColorBrush(Color.FromArgb(0x2D, 0xFF, 0xFF, 0xFF));
        }
    }

    private void DayCell_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border cell && cell.Tag is DateTime date)
        {
            var isToday = date.Date == DateTime.Today;
            if (!isToday)
                cell.Background = Brushes.Transparent;
        }
    }

    private void DayCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border cell && cell.Tag is DateTime date)
        {
            _selectedDate = date;

            // If clicked a date in a different month, navigate there
            if (date.Month != _displayMonth.Month || date.Year != _displayMonth.Year)
            {
                _displayMonth = new DateTime(date.Year, date.Month, 1);
            }

            PopulateDayView();
        }
    }

    private void MonthCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border cell)
        {
            var month = (int)cell.Tag;
            var today = DateTime.Today;
            if (!(_displayYear == today.Year && month == today.Month))
                cell.Background = new SolidColorBrush(Color.FromArgb(0x2D, 0xFF, 0xFF, 0xFF));
        }
    }

    private void MonthCell_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border cell)
        {
            var month = (int)cell.Tag;
            var today = DateTime.Today;
            if (!(_displayYear == today.Year && month == today.Month))
                cell.Background = Brushes.Transparent;
        }
    }

    private void MonthCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border cell && cell.Tag is int month)
        {
            _displayMonth = new DateTime(_displayYear, month, 1);
            PopulateDayView();
            AnimateViewTransition(MonthView, DayView, false);
            _viewMode = ViewMode.Day;
        }
    }

    private void YearCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border cell && cell.Tag is int year)
        {
            if (year != DateTime.Today.Year)
                cell.Background = new SolidColorBrush(Color.FromArgb(0x2D, 0xFF, 0xFF, 0xFF));
        }
    }

    private void YearCell_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border cell && cell.Tag is int year)
        {
            if (year != DateTime.Today.Year)
                cell.Background = Brushes.Transparent;
        }
    }

    private void YearCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border cell && cell.Tag is int year)
        {
            _displayYear = year;
            PopulateMonthView();
            AnimateViewTransition(DecadeView, MonthView, false);
            _viewMode = ViewMode.Month;
        }
    }

    #endregion

    #region Navigation

    private void MonthYearButton_Click(object sender, RoutedEventArgs e)
    {
        // Zoom out: Day → Month
        _displayYear = _displayMonth.Year;
        PopulateMonthView();
        AnimateViewTransition(DayView, MonthView, true);
        _viewMode = ViewMode.Month;
    }

    private void YearButton_Click(object sender, RoutedEventArgs e)
    {
        // Zoom out: Month → Decade
        _displayDecadeStart = _displayYear / 10 * 10;
        PopulateDecadeView();
        AnimateViewTransition(MonthView, DecadeView, true);
        _viewMode = ViewMode.Decade;
    }

    private void DayPrev_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(-1);
        PopulateDayView();
    }

    private void DayNext_Click(object sender, RoutedEventArgs e)
    {
        _displayMonth = _displayMonth.AddMonths(1);
        PopulateDayView();
    }

    private void MonthViewPrev_Click(object sender, RoutedEventArgs e)
    {
        _displayYear--;
        PopulateMonthView();
    }

    private void MonthViewNext_Click(object sender, RoutedEventArgs e)
    {
        _displayYear++;
        PopulateMonthView();
    }

    private void DecadeViewPrev_Click(object sender, RoutedEventArgs e)
    {
        _displayDecadeStart -= 10;
        PopulateDecadeView();
    }

    private void DecadeViewNext_Click(object sender, RoutedEventArgs e)
    {
        _displayDecadeStart += 10;
        PopulateDecadeView();
    }

    #endregion

    #region Animations

    private void AnimateViewTransition(Grid outgoing, Grid incoming, bool zoomingOut)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(250));
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

        // Outgoing: scale and fade out
        var outScale = zoomingOut ? 1.4 : 0.6;
        var outScaleTransform = (ScaleTransform)outgoing.RenderTransform;
        var outScaleXAnim = new DoubleAnimation(1.0, outScale, duration) { EasingFunction = ease };
        var outScaleYAnim = new DoubleAnimation(1.0, outScale, duration) { EasingFunction = ease };
        var outFadeAnim = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

        outScaleXAnim.Completed += (_, _) =>
        {
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.Opacity = 1.0;
            outScaleTransform.ScaleX = 1.0;
            outScaleTransform.ScaleY = 1.0;
        };

        // Incoming: scale and fade in
        var inScale = zoomingOut ? 0.6 : 1.4;
        var inScaleTransform = (ScaleTransform)incoming.RenderTransform;
        inScaleTransform.ScaleX = inScale;
        inScaleTransform.ScaleY = inScale;
        incoming.Opacity = 0.0;
        incoming.Visibility = Visibility.Visible;

        var inScaleXAnim = new DoubleAnimation(inScale, 1.0, duration) { EasingFunction = ease };
        var inScaleYAnim = new DoubleAnimation(inScale, 1.0, duration) { EasingFunction = ease };
        var inFadeAnim = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };

        // Start animations
        outScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, outScaleXAnim);
        outScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, outScaleYAnim);
        outgoing.BeginAnimation(OpacityProperty, outFadeAnim);

        inScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, inScaleXAnim);
        inScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, inScaleYAnim);
        incoming.BeginAnimation(OpacityProperty, inFadeAnim);
    }

    #endregion

    #region Reveal Highlight

    private void InitRevealHighlight()
    {
        var revealColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);

        _revealBrush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            RadiusX = 80,
            RadiusY = 80,
            Center = new Point(-1000, -1000),
            GradientOrigin = new Point(-1000, -1000),
            GradientStops =
            {
                new GradientStop(revealColor, 0.0),
                new GradientStop(Colors.Transparent, 1.0)
            }
        };
    }

    private void CalendarGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_revealBrush == null || sender is not UIElement grid) return;

        var pos = e.GetPosition(grid);
        _revealBrush.Center = pos;
        _revealBrush.GradientOrigin = pos;
    }

    private void CalendarGrid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_revealBrush == null) return;

        _revealBrush.Center = new Point(-1000, -1000);
        _revealBrush.GradientOrigin = new Point(-1000, -1000);
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _clockTimer?.Stop();
        _clockTimer = null;
        _mouseHook?.Dispose();
        _mouseHook = null;
        base.OnClosed(e);
    }
}
