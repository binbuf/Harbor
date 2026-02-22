using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Shell;

/// <summary>
/// macOS-style traffic light window control buttons (Close, Minimize, Maximize).
/// Renders as colored circles with vector glyph overlays on hover.
/// </summary>
public partial class TrafficLightButtons : UserControl
{
    // Layout constants (DIP)
    public const double ButtonDiameter = 12.0;
    public const double ButtonSpacing = 8.0; // gap between button edges
    public const double CenterToCenter = 20.0; // ButtonDiameter + ButtonSpacing
    public const double LeftPadding = 8.0; // from left edge to first button center

    // Default colors
    private static readonly SolidColorBrush CloseDefault = new(Color.FromRgb(0xFF, 0x5F, 0x57));
    private static readonly SolidColorBrush MinimizeDefault = new(Color.FromRgb(0xFE, 0xBC, 0x2E));
    private static readonly SolidColorBrush MaximizeDefault = new(Color.FromRgb(0x28, 0xC8, 0x40));

    // Pressed colors
    private static readonly SolidColorBrush ClosePressed = new(Color.FromRgb(0xE0, 0x44, 0x3E));
    private static readonly SolidColorBrush MinimizePressed = new(Color.FromRgb(0xD4, 0xA5, 0x28));
    private static readonly SolidColorBrush MaximizePressed = new(Color.FromRgb(0x1A, 0xAB, 0x29));

    // Disabled color (same as inactive)
    private static readonly SolidColorBrush DisabledBrush = new(Color.FromRgb(0xCD, 0xCD, 0xCD));

    // Inactive color
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(0xCD, 0xCD, 0xCD));

    // Maximize glyph path data
    private const string MaximizeGlyphData = "M 3,6 L 6,3 L 9,6 M 3,6 L 6,9 L 9,6";
    private const string RestoreGlyphData = "M 4,4 L 8,4 L 8,8 L 4,8 Z";

    private static readonly Duration GlyphFadeDuration = new(TimeSpan.FromMilliseconds(80));

    private bool _isActive = true;
    private bool _isHovered;
    private bool _canMinimize = true;
    private bool _canMaximize = true;
    private bool _isTargetMaximized;

    /// <summary>
    /// The target HWND whose window commands will be triggered.
    /// </summary>
    public HWND TargetHwnd { get; set; }

    /// <summary>
    /// Fired when a traffic light button is clicked. Args: (HWND target, TrafficLightAction action).
    /// </summary>
    public event Action<HWND, TrafficLightAction>? ButtonClicked;

    public TrafficLightButtons()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // Freeze static brushes for performance
        CloseDefault.Freeze();
        MinimizeDefault.Freeze();
        MaximizeDefault.Freeze();
        ClosePressed.Freeze();
        MinimizePressed.Freeze();
        MaximizePressed.Freeze();
        DisabledBrush.Freeze();
        InactiveBrush.Freeze();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionButtons();
    }

    /// <summary>
    /// Positions the three button grids on the canvas using the spec layout.
    /// First button center at LeftPadding (8 DIP), subsequent buttons at CenterToCenter (20 DIP) intervals.
    /// </summary>
    private void PositionButtons()
    {
        var verticalCenter = ButtonCanvas.ActualHeight > 0
            ? ButtonCanvas.ActualHeight / 2.0
            : ActualHeight / 2.0;

        var halfButton = ButtonDiameter / 2.0;
        var topOffset = verticalCenter - halfButton;

        // Close at x = LeftPadding - halfButton
        Canvas.SetLeft(CloseButton, LeftPadding - halfButton);
        Canvas.SetTop(CloseButton, topOffset);

        // Minimize at x = LeftPadding + CenterToCenter - halfButton
        Canvas.SetLeft(MinimizeButton, LeftPadding + CenterToCenter - halfButton);
        Canvas.SetTop(MinimizeButton, topOffset);

        // Maximize at x = LeftPadding + 2*CenterToCenter - halfButton
        Canvas.SetLeft(MaximizeButton, LeftPadding + 2 * CenterToCenter - halfButton);
        Canvas.SetTop(MaximizeButton, topOffset);
    }

    /// <summary>
    /// Sets whether the target window is the active (foreground) window.
    /// Inactive windows show all buttons as gray.
    /// </summary>
    public void SetActive(bool isActive)
    {
        if (_isActive == isActive) return;
        _isActive = isActive;
        UpdateButtonColors();
    }

    /// <summary>
    /// Sets whether the minimize button is enabled (window has WS_MINIMIZEBOX).
    /// Disabled buttons show as gray and don't fire click events.
    /// </summary>
    public void SetCanMinimize(bool canMinimize)
    {
        if (_canMinimize == canMinimize) return;
        _canMinimize = canMinimize;
        MinimizeButton.IsHitTestVisible = canMinimize;
        UpdateButtonColors();
    }

    /// <summary>
    /// Sets whether the maximize button is enabled (window has WS_MAXIMIZEBOX).
    /// Disabled buttons show as gray and don't fire click events.
    /// </summary>
    public void SetCanMaximize(bool canMaximize)
    {
        if (_canMaximize == canMaximize) return;
        _canMaximize = canMaximize;
        MaximizeButton.IsHitTestVisible = canMaximize;
        UpdateButtonColors();
    }

    /// <summary>
    /// Updates the maximize button glyph based on whether the target window is maximized.
    /// Shows restore glyph (rectangle) when maximized, plus/expand glyph when normal.
    /// </summary>
    public void SetMaximized(bool isMaximized)
    {
        if (_isTargetMaximized == isMaximized) return;
        _isTargetMaximized = isMaximized;

        var data = isMaximized ? RestoreGlyphData : MaximizeGlyphData;
        MaximizeGlyph.Data = Geometry.Parse(data);
    }

    /// <summary>
    /// Calculates the left position (X) of a button at the given index (0=Close, 1=Minimize, 2=Maximize).
    /// </summary>
    public static double CalculateButtonLeft(int index)
    {
        return LeftPadding + index * CenterToCenter - ButtonDiameter / 2.0;
    }

    /// <summary>
    /// Calculates the top position (Y) of a button for vertical centering within the given container height.
    /// </summary>
    public static double CalculateButtonTop(double containerHeight)
    {
        return containerHeight / 2.0 - ButtonDiameter / 2.0;
    }

    /// <summary>
    /// Returns the default fill color for a given button action when active.
    /// </summary>
    public static Color GetDefaultColor(TrafficLightAction action) => action switch
    {
        TrafficLightAction.Close => Color.FromRgb(0xFF, 0x5F, 0x57),
        TrafficLightAction.Minimize => Color.FromRgb(0xFE, 0xBC, 0x2E),
        TrafficLightAction.Maximize => Color.FromRgb(0x28, 0xC8, 0x40),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>
    /// Returns the pressed fill color for a given button action.
    /// </summary>
    public static Color GetPressedColor(TrafficLightAction action) => action switch
    {
        TrafficLightAction.Close => Color.FromRgb(0xE0, 0x44, 0x3E),
        TrafficLightAction.Minimize => Color.FromRgb(0xD4, 0xA5, 0x28),
        TrafficLightAction.Maximize => Color.FromRgb(0x1A, 0xAB, 0x29),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>
    /// Returns the glyph stroke color for a given button action.
    /// </summary>
    public static Color GetGlyphColor(TrafficLightAction action) => action switch
    {
        TrafficLightAction.Close => Color.FromRgb(0x4D, 0x00, 0x00),
        TrafficLightAction.Minimize => Color.FromRgb(0x6B, 0x44, 0x00),
        TrafficLightAction.Maximize => Color.FromRgb(0x00, 0x3D, 0x00),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>
    /// Returns the inactive fill color for all buttons.
    /// </summary>
    public static Color GetInactiveColor() => Color.FromRgb(0xCD, 0xCD, 0xCD);

    private void UpdateButtonColors()
    {
        if (_isActive || _isHovered)
        {
            CloseCircle.Fill = CloseDefault;
            MinimizeCircle.Fill = _canMinimize ? MinimizeDefault : DisabledBrush;
            MaximizeCircle.Fill = _canMaximize ? MaximizeDefault : DisabledBrush;
        }
        else
        {
            CloseCircle.Fill = InactiveBrush;
            MinimizeCircle.Fill = InactiveBrush;
            MaximizeCircle.Fill = InactiveBrush;

            // Hide glyphs when inactive and not hovered
            SetGlyphOpacity(0, immediate: true);
        }
    }

    private void OnGroupMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = true;

        // Restore active colors if window is inactive (disabled buttons stay gray)
        CloseCircle.Fill = CloseDefault;
        MinimizeCircle.Fill = _canMinimize ? MinimizeDefault : DisabledBrush;
        MaximizeCircle.Fill = _canMaximize ? MaximizeDefault : DisabledBrush;

        // Show all glyphs simultaneously with fade-in
        SetGlyphOpacity(1.0, immediate: false);
    }

    private void OnGroupMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isHovered = false;

        // Restore buttons to default/inactive state
        CloseCircle.Fill = _isActive ? CloseDefault : InactiveBrush;
        MinimizeCircle.Fill = _isActive ? (_canMinimize ? MinimizeDefault : DisabledBrush) : InactiveBrush;
        MaximizeCircle.Fill = _isActive ? (_canMaximize ? MaximizeDefault : DisabledBrush) : InactiveBrush;

        // Hide all glyphs
        SetGlyphOpacity(0, immediate: true);
    }

    private void SetGlyphOpacity(double targetOpacity, bool immediate)
    {
        if (immediate)
        {
            CloseGlyph.Opacity = targetOpacity;
            MinimizeGlyph.Opacity = targetOpacity;
            MaximizeGlyph.Opacity = targetOpacity;
        }
        else
        {
            var animation = new DoubleAnimation(targetOpacity, GlyphFadeDuration)
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            CloseGlyph.BeginAnimation(OpacityProperty, animation);
            MinimizeGlyph.BeginAnimation(OpacityProperty, animation);
            MaximizeGlyph.BeginAnimation(OpacityProperty, animation);
        }
    }

    // --- Press/Release handlers ---

    private void OnClosePressed(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CloseCircle.Fill = ClosePressed;
        e.Handled = true;
    }

    private void OnCloseReleased(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CloseCircle.Fill = CloseDefault;
        Trace.WriteLine($"[Harbor] TrafficLight: Close clicked for HWND {TargetHwnd}");
        ButtonClicked?.Invoke(TargetHwnd, TrafficLightAction.Close);
        e.Handled = true;
    }

    private void OnMinimizePressed(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_canMinimize) { e.Handled = true; return; }
        MinimizeCircle.Fill = MinimizePressed;
        e.Handled = true;
    }

    private void OnMinimizeReleased(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_canMinimize) { e.Handled = true; return; }
        MinimizeCircle.Fill = MinimizeDefault;
        Trace.WriteLine($"[Harbor] TrafficLight: Minimize clicked for HWND {TargetHwnd}");
        ButtonClicked?.Invoke(TargetHwnd, TrafficLightAction.Minimize);
        e.Handled = true;
    }

    private void OnMaximizePressed(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_canMaximize) { e.Handled = true; return; }
        MaximizeCircle.Fill = MaximizePressed;
        e.Handled = true;
    }

    private void OnMaximizeReleased(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_canMaximize) { e.Handled = true; return; }
        MaximizeCircle.Fill = MaximizeDefault;
        Trace.WriteLine($"[Harbor] TrafficLight: Maximize clicked for HWND {TargetHwnd}");
        ButtonClicked?.Invoke(TargetHwnd, TrafficLightAction.Maximize);
        e.Handled = true;
    }
}
