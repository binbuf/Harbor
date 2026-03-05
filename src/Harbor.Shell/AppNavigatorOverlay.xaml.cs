using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Harbor.Core.Interop;
using Harbor.Core.Services;
using Harbor.Shell.Controls;
using ManagedShell.WindowsTasks;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Shell;

/// <summary>
/// Full-screen App Navigator overlay. Shows live DWM thumbnails of all open
/// windows arranged in a grid. Triggered by AppNavigatorService events.
///
/// Design notes (see docs/Review.md):
/// - AllowsTransparency=False so DWM thumbnails render correctly in HwndHost slots.
/// - WS_EX_NOACTIVATE prevents overlay from stealing keyboard focus.
/// - Acrylic blur is applied via SetWindowCompositionAttribute.
/// - Entry animation: 350ms SplineEase(0.2,0,0,1) spread from current window positions.
/// - Exit animation: 250ms reverse (selected slot expands, others fade).
/// </summary>
public partial class AppNavigatorOverlay : Window
{
    // -------------------------------------------------------------------------
    // Animation constants

    private const double EntryDurationMs = 350.0;
    private const double ExitDurationMs = 250.0;
    private static readonly IEasingFunction EntryEasing = new SplineEase(0.2, 0, 0, 1);

    // -------------------------------------------------------------------------
    // Layout constants

    private const double DesktopStripHeight = 124.0; // reserved px from top of window canvas
    private const double SlotGap = 16.0;
    private const double SlotMinWidth = 120.0;
    private const double SlotMaxWidth = 480.0;
    private const double LabelHeight = 18.0; // below thumbnail
    private const double LabelOffset = 4.0;

    // -------------------------------------------------------------------------
    // State

    private sealed class SlotInfo
    {
        public required ApplicationWindow Window { get; init; }
        public required string AppName { get; init; }
        public required NavThumbnailSlot Thumbnail { get; init; }
        public required Border HoverBorder { get; init; }
        public required TextBlock Label { get; init; }

        // Source position (physical→DIP, relative to overlay top-left)
        public double SrcX, SrcY, SrcW, SrcH;

        // Target position (grid layout, DIPs)
        public double TgtX, TgtY, TgtW, TgtH;

        // True if the source window was minimized (invalid source position)
        public bool WasMinimized;
    }

    private readonly List<SlotInfo> _slots = [];
    private AppNavigatorService? _service;
    private int _selectedIndex = -1;

    // Animation
    private DispatcherTimer? _animTimer;
    private DateTime _animStart;
    private bool _isExiting;
    private HWND? _activatedOnExit;
    private double _dpiScale = 1.0;

    public AppNavigatorOverlay()
    {
        InitializeComponent();
    }

    // -------------------------------------------------------------------------
    // Initialization

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new HWND(new WindowInteropHelper(this).Handle);

        // WS_EX_TOOLWINDOW — hide from alt-tab (but allow activation so keyboard works)
        var exStyle = WindowInterop.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (nint)WindowInterop.WS_EX_TOOLWINDOW;
        WindowInterop.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);

        // Apply acrylic blur behind (enhances the dark overlay visual)
        CompositionInterop.EnableAcrylic(hwnd, gradientColor: 0x99000000); // dark tint
    }

    public void BindService(AppNavigatorService service)
    {
        _service = service;
    }

    // -------------------------------------------------------------------------
    // Public entry point (called from App.xaml.cs dispatcher)

    public void ShowAppNavigator(List<AppNavigatorService.WindowGroup> groups)
    {
        // Position over primary monitor
        PositionOverMonitor();

        // Show window (HWND now exists)
        Show();
        Activate(); // take keyboard focus so arrow/enter/escape keys work

        // Cache DPI scale for animation
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        _dpiScale = DisplayInterop.GetScaleFactorForWindow(hwnd);

        // Lift to top (above AppBars, below topmost system dialogs)
        WindowInterop.SetWindowPos(hwnd, new HWND(0), // HWND_TOP
            (int)Left, (int)Top, (int)Width, (int)Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        // Build desktop strip
        BuildDesktopStrip(groups);

        // Build thumbnail slots
        BuildSlots(groups);

        // Populate window canvas
        PopulateCanvas();

        // Run entry animation
        _isExiting = false;
        _selectedIndex = -1;
        DimOverlay.Opacity = 0;
        StartAnimation(entry: true);
    }

    public void DismissAppNavigator(HWND? activatedHwnd)
    {
        _activatedOnExit = activatedHwnd;
        _isExiting = true;
        StartAnimation(entry: false);
    }

    // -------------------------------------------------------------------------
    // Monitor positioning

    private void PositionOverMonitor()
    {
        // Use SystemParameters which are already in DIPs (device-independent pixels).
        // Screen.Bounds returns physical pixels which causes the overlay to be oversized
        // on high-DPI displays, leaving a black area.
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
    }

    // -------------------------------------------------------------------------
    // Desktop strip

    private void BuildDesktopStrip(List<AppNavigatorService.WindowGroup> groups)
    {
        DesktopList.Items.Clear();

        // Collect unique desktop IDs in the order they appear
        var desktops = groups
            .Select(g => g.DesktopId)
            .Distinct()
            .ToList();

        if (desktops.Count <= 1)
        {
            // Single desktop — just show "Desktop 1"
            desktops = [Guid.Empty];
        }

        for (int i = 0; i < desktops.Count; i++)
        {
            var desktopBorder = new Border
            {
                Width = 120,
                Height = 72,
                Margin = new Thickness(6, 0, 6, 0),
                Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                Child = new TextBlock
                {
                    Text = $"Desktop {i + 1}",
                    Foreground = Brushes.White,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable"),
                    FontSize = 11,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DesktopList.Items.Add(desktopBorder);
        }
    }

    // -------------------------------------------------------------------------
    // Slot construction

    private void BuildSlots(List<AppNavigatorService.WindowGroup> groups)
    {
        _slots.Clear();

        var overlayHwnd = new HWND(new WindowInteropHelper(this).Handle);
        var scale = DisplayInterop.GetScaleFactorForWindow(overlayHwnd);

        // Get overlay screen position for translating source window rects
        double overlayScreenLeft = Left * scale;
        double overlayScreenTop = Top * scale;

        foreach (var group in groups)
        {
            var window = group.Window;
            var winHwnd = new HWND(window.Handle);

            // Source position from current window rect (physical pixels)
            double srcX = 0, srcY = 0, srcW = 320, srcH = 240;
            bool wasMinimized = false;
            if (WindowInterop.GetWindowRect(winHwnd, out var winRect)
                && winRect.left > -10000) // minimized windows report (-32000, -32000)
            {
                srcX = (winRect.left - overlayScreenLeft) / scale;
                srcY = (winRect.top - overlayScreenTop) / scale;
                srcW = Math.Max(1, (winRect.right - winRect.left) / scale);
                srcH = Math.Max(1, (winRect.bottom - winRect.top) / scale);
            }
            else
            {
                wasMinimized = true;
            }

            // DWM thumbnail slot (HwndHost)
            var thumb = new NavThumbnailSlot(window.Handle)
            {
                Width = srcW,
                Height = srcH,
            };

            // Hover border (WPF sibling, drawn over the HwndHost).
            // Must be Visible with transparent fill for hit testing — Hidden elements
            // don't receive mouse events. Border thickness toggles on hover.
            var hoverBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(178, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = true,
                Background = Brushes.Transparent,
                Visibility = Visibility.Visible,
                Width = srcW,
                Height = srcH,
            };

            // App name label below thumbnail
            var label = new TextBlock
            {
                Text = group.DisplayName,
                Foreground = new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable"),
                FontSize = 11,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var slot = new SlotInfo
            {
                Window = window,
                AppName = group.DisplayName,
                Thumbnail = thumb,
                HoverBorder = hoverBorder,
                Label = label,
                SrcX = srcX,
                SrcY = srcY,
                SrcW = srcW,
                SrcH = srcH,
                WasMinimized = wasMinimized,
            };
            _slots.Add(slot);
        }

        // Compute target grid layout for all slots
        ComputeGridLayout();

        // Minimized windows have no valid source position — fade in at target instead
        foreach (var slot in _slots)
        {
            if (slot.WasMinimized)
            {
                slot.SrcX = slot.TgtX;
                slot.SrcY = slot.TgtY;
                slot.SrcW = slot.TgtW;
                slot.SrcH = slot.TgtH;
            }
        }
    }

    private void ComputeGridLayout()
    {
        int n = _slots.Count;
        if (n == 0) return;

        double availW = Width - SlotGap * 2;
        double availH = Height - DesktopStripHeight - SlotGap * 2 - LabelHeight - LabelOffset;

        // Optimal column count: minimize wasted space
        int cols = (int)Math.Ceiling(Math.Sqrt(n * (availW / availH)));
        cols = Math.Max(1, Math.Min(cols, n));
        int rows = (int)Math.Ceiling((double)n / cols);

        double slotW = Math.Clamp((availW - SlotGap * (cols - 1)) / cols, SlotMinWidth, SlotMaxWidth);
        double slotH = Math.Min((availH - SlotGap * (rows - 1)) / rows, slotW * 0.75);
        slotW = slotH / 0.75; // maintain 4:3 aspect ratio

        // Clamp to max
        if (slotW > SlotMaxWidth) { slotW = SlotMaxWidth; slotH = slotW * 0.75; }

        // Center the grid
        double totalGridW = cols * slotW + (cols - 1) * SlotGap;
        double totalGridH = rows * slotH + (rows - 1) * SlotGap + LabelHeight + LabelOffset;
        double originX = SlotGap + (availW - totalGridW) / 2.0;
        double originY = DesktopStripHeight + SlotGap + (availH - totalGridH) / 2.0;

        for (int i = 0; i < _slots.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;

            _slots[i].TgtX = originX + col * (slotW + SlotGap);
            _slots[i].TgtY = originY + row * (slotH + SlotGap + LabelHeight + LabelOffset);
            _slots[i].TgtW = slotW;
            _slots[i].TgtH = slotH;
        }
    }

    // -------------------------------------------------------------------------
    // Canvas population

    private void PopulateCanvas()
    {
        WindowCanvas.Children.Clear();

        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            int capturedIndex = i;

            // Place thumbnail at source position initially (animation will move it)
            Canvas.SetLeft(slot.Thumbnail, slot.SrcX);
            Canvas.SetTop(slot.Thumbnail, slot.SrcY);
            slot.Thumbnail.Width = slot.SrcW;
            slot.Thumbnail.Height = slot.SrcH;
            WindowCanvas.Children.Add(slot.Thumbnail);

            // Hover border at same position
            Canvas.SetLeft(slot.HoverBorder, slot.SrcX);
            Canvas.SetTop(slot.HoverBorder, slot.SrcY);
            slot.HoverBorder.Width = slot.SrcW;
            slot.HoverBorder.Height = slot.SrcH;
            WindowCanvas.Children.Add(slot.HoverBorder);

            // App label below the thumbnail
            Canvas.SetLeft(slot.Label, slot.SrcX);
            Canvas.SetTop(slot.Label, slot.SrcY + slot.SrcH + LabelOffset);
            slot.Label.Width = slot.SrcW;
            WindowCanvas.Children.Add(slot.Label);

            // Wire hover and click events on the border
            slot.HoverBorder.MouseEnter += (_, _) =>
            {
                slot.HoverBorder.BorderThickness = new Thickness(2);
                _selectedIndex = capturedIndex;
            };
            slot.HoverBorder.MouseLeave += (_, _) =>
            {
                slot.HoverBorder.BorderThickness = new Thickness(0);
            };
            slot.HoverBorder.MouseLeftButtonUp += (_, _) =>
            {
                _selectedIndex = capturedIndex;
                OnWindowSelected(capturedIndex);
            };
        }
    }

    // -------------------------------------------------------------------------
    // Keyboard

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _service?.Dismiss(null);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_selectedIndex >= 0)
                    OnWindowSelected(_selectedIndex);
                e.Handled = true;
                break;

            case Key.Left:
                _selectedIndex = (_selectedIndex - 1 + _slots.Count) % Math.Max(1, _slots.Count);
                e.Handled = true;
                break;

            case Key.Right:
                _selectedIndex = (_selectedIndex + 1) % Math.Max(1, _slots.Count);
                e.Handled = true;
                break;
        }
    }

    private void OnWindowSelected(int index)
    {
        if (index < 0 || index >= _slots.Count) return;
        var hwnd = new HWND(_slots[index].Window.Handle);
        _service?.Dismiss(hwnd);
    }

    // -------------------------------------------------------------------------
    // Animation

    private void StartAnimation(bool entry)
    {
        StopAnimation();

        _animStart = DateTime.UtcNow;
        _animTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnAnimFrame,
            Dispatcher);
        _animTimer.Start();
    }

    private void StopAnimation()
    {
        _animTimer?.Stop();
        _animTimer = null;
    }

    private void OnAnimFrame(object? sender, EventArgs e)
    {
        var durationMs = _isExiting ? ExitDurationMs : EntryDurationMs;
        var elapsed = (DateTime.UtcNow - _animStart).TotalMilliseconds;
        var t = Math.Min(elapsed / durationMs, 1.0);
        var progress = _isExiting ? (1.0 - t) : t;
        var eased = EntryEasing.Ease(progress);

        // Animate each slot
        for (int i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];

            double x, y, w, h;

            if (_isExiting && i == _selectedIndex)
            {
                // Selected slot expands toward its source rect on exit (eased)
                var exitProgress = 1.0 - eased;
                x = Lerp(slot.TgtX, slot.SrcX, exitProgress);
                y = Lerp(slot.TgtY, slot.SrcY, exitProgress);
                w = Lerp(slot.TgtW, slot.SrcW, exitProgress);
                h = Lerp(slot.TgtH, slot.SrcH, exitProgress);
                slot.Thumbnail.Opacity = 1.0;
                slot.Label.Opacity = eased;
            }
            else if (_isExiting)
            {
                // Other slots fade out
                x = slot.TgtX;
                y = slot.TgtY;
                w = slot.TgtW;
                h = slot.TgtH;
                slot.Thumbnail.Opacity = eased;
                slot.Label.Opacity = eased;
            }
            else
            {
                // Entry: spread from source to target
                x = Lerp(slot.SrcX, slot.TgtX, eased);
                y = Lerp(slot.SrcY, slot.TgtY, eased);
                w = Lerp(slot.SrcW, slot.TgtW, eased);
                h = Lerp(slot.SrcH, slot.TgtH, eased);
                slot.Thumbnail.Opacity = eased;
                slot.Label.Opacity = eased;
            }

            MoveSlot(slot, x, y, w, h, _dpiScale);
        }

        // Dim background
        DimOverlay.Opacity = _isExiting ? (1.0 - t) * 0.45 : eased * 0.45;

        if (t >= 1.0)
        {
            StopAnimation();
            OnAnimationComplete();
        }
    }

    private static void MoveSlot(SlotInfo slot, double x, double y, double w, double h, double dpiScale)
    {
        Canvas.SetLeft(slot.Thumbnail, x);
        Canvas.SetTop(slot.Thumbnail, y);
        slot.Thumbnail.Width = w;
        slot.Thumbnail.Height = h;

        // Directly resize the child HWND in physical pixels so the DWM thumbnail
        // updates immediately, without waiting for WPF's deferred layout pass.
        slot.Thumbnail.SetPhysicalSize((int)(w * dpiScale), (int)(h * dpiScale));

        Canvas.SetLeft(slot.HoverBorder, x);
        Canvas.SetTop(slot.HoverBorder, y);
        slot.HoverBorder.Width = w;
        slot.HoverBorder.Height = h;

        Canvas.SetLeft(slot.Label, x);
        Canvas.SetTop(slot.Label, y + h + LabelOffset);
        slot.Label.Width = w;
    }

    private void OnAnimationComplete()
    {
        if (!_isExiting) return;

        // Capture activation target before clearing slots
        HWND? toActivate = null;
        if (_activatedOnExit.HasValue && _activatedOnExit.Value != HWND.Null)
        {
            toActivate = _activatedOnExit.Value;
        }

        // Find matching ApplicationWindow for BringToFront before clearing
        ManagedShell.WindowsTasks.ApplicationWindow? appWindow = null;
        if (toActivate.HasValue)
        {
            appWindow = _slots
                .FirstOrDefault(s => new HWND(s.Window.Handle) == toActivate.Value)
                ?.Window;
        }

        // Cleanup slots
        WindowCanvas.Children.Clear();
        _slots.Clear();
        DesktopList.Items.Clear();

        Hide();

        // Restore or activate focus
        if (appWindow is not null)
        {
            appWindow.BringToFront();
        }
        else if (_service is not null)
        {
            var prev = _service.PreviousForegroundHwnd;
            if (prev != HWND.Null)
                ActivateWindowHandle(prev);
        }
    }

    private static void ActivateWindowHandle(HWND hwnd)
    {
        try
        {
            WindowInterop.SetWindowPos(hwnd, HWND.Null, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] AppNavigatorOverlay: ActivateWindowHandle failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
