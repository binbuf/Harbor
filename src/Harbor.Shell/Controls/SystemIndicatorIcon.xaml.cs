using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Harbor.Shell.Controls;

public partial class SystemIndicatorIcon : UserControl
{
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    // Use the same hover colors as TopMenuBar
    private static readonly Color DarkHoverColor = Color.FromArgb(26, 255, 255, 255);
    private static readonly Color DarkPressedColor = Color.FromArgb(51, 255, 255, 255);
    private static readonly Color LightHoverColor = Color.FromArgb(20, 0, 0, 0);
    private static readonly Color LightPressedColor = Color.FromArgb(41, 0, 0, 0);

    private Color _hoverColor = DarkHoverColor;
    private Color _pressedColor = DarkPressedColor;

    public static readonly DependencyProperty IconDataProperty =
        DependencyProperty.Register(nameof(IconData), typeof(Geometry), typeof(SystemIndicatorIcon),
            new PropertyMetadata(null, OnIconDataChanged));

    public Geometry? IconData
    {
        get => (Geometry?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public event EventHandler? Clicked;

    public SystemIndicatorIcon()
    {
        InitializeComponent();
        HitArea.Background = TransparentBrush.Clone();
    }

    private static void OnIconDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SystemIndicatorIcon control)
        {
            control.IconPath.Data = e.NewValue as Geometry;
        }
    }

    public void SetThemeColors(bool isLight)
    {
        _hoverColor = isLight ? LightHoverColor : DarkHoverColor;
        _pressedColor = isLight ? LightPressedColor : DarkPressedColor;
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        var brush = HitArea.Background as SolidColorBrush;
        if (brush is null || brush.IsFrozen)
        {
            brush = TransparentBrush.Clone();
            HitArea.Background = brush;
        }

        var animation = new ColorAnimation
        {
            To = _hoverColor,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (HitArea.Background is not SolidColorBrush brush || brush.IsFrozen) return;

        var animation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HitArea.Background is not SolidColorBrush brush || brush.IsFrozen) return;

        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = _pressedColor;
    }
}
