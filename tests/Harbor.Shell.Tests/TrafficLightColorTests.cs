using System.Windows.Media;
using Harbor.Core.Services;

namespace Harbor.Shell.Tests;

/// <summary>
/// Tests for traffic light button color state transitions (default, hover glyphs, pressed, inactive).
/// </summary>
public class TrafficLightColorTests
{
    // --- Default colors ---

    [Fact]
    public void CloseDefaultColor_IsFF5F57()
    {
        var color = TrafficLightButtons.GetDefaultColor(TrafficLightAction.Close);
        Assert.Equal(Color.FromRgb(0xFF, 0x5F, 0x57), color);
    }

    [Fact]
    public void MinimizeDefaultColor_IsFEBC2E()
    {
        var color = TrafficLightButtons.GetDefaultColor(TrafficLightAction.Minimize);
        Assert.Equal(Color.FromRgb(0xFE, 0xBC, 0x2E), color);
    }

    [Fact]
    public void MaximizeDefaultColor_Is28C840()
    {
        var color = TrafficLightButtons.GetDefaultColor(TrafficLightAction.Maximize);
        Assert.Equal(Color.FromRgb(0x28, 0xC8, 0x40), color);
    }

    // --- Pressed colors ---

    [Fact]
    public void ClosePressedColor_IsE0443E()
    {
        var color = TrafficLightButtons.GetPressedColor(TrafficLightAction.Close);
        Assert.Equal(Color.FromRgb(0xE0, 0x44, 0x3E), color);
    }

    [Fact]
    public void MinimizePressedColor_IsD4A528()
    {
        var color = TrafficLightButtons.GetPressedColor(TrafficLightAction.Minimize);
        Assert.Equal(Color.FromRgb(0xD4, 0xA5, 0x28), color);
    }

    [Fact]
    public void MaximizePressedColor_Is1AAB29()
    {
        var color = TrafficLightButtons.GetPressedColor(TrafficLightAction.Maximize);
        Assert.Equal(Color.FromRgb(0x1A, 0xAB, 0x29), color);
    }

    // --- Glyph colors ---

    [Fact]
    public void CloseGlyphColor_Is4D0000()
    {
        var color = TrafficLightButtons.GetGlyphColor(TrafficLightAction.Close);
        Assert.Equal(Color.FromRgb(0x4D, 0x00, 0x00), color);
    }

    [Fact]
    public void MinimizeGlyphColor_Is6B4400()
    {
        var color = TrafficLightButtons.GetGlyphColor(TrafficLightAction.Minimize);
        Assert.Equal(Color.FromRgb(0x6B, 0x44, 0x00), color);
    }

    [Fact]
    public void MaximizeGlyphColor_Is003D00()
    {
        var color = TrafficLightButtons.GetGlyphColor(TrafficLightAction.Maximize);
        Assert.Equal(Color.FromRgb(0x00, 0x3D, 0x00), color);
    }

    // --- Inactive color ---

    [Fact]
    public void InactiveColor_IsCDCDCD()
    {
        var color = TrafficLightButtons.GetInactiveColor();
        Assert.Equal(Color.FromRgb(0xCD, 0xCD, 0xCD), color);
    }

    // --- State transitions ---

    [Fact]
    public void DefaultColors_AreDifferentFromPressed()
    {
        foreach (var action in Enum.GetValues<TrafficLightAction>())
        {
            var defaultColor = TrafficLightButtons.GetDefaultColor(action);
            var pressedColor = TrafficLightButtons.GetPressedColor(action);
            Assert.NotEqual(defaultColor, pressedColor);
        }
    }

    [Fact]
    public void PressedColors_AreDarkerThanDefault()
    {
        foreach (var action in Enum.GetValues<TrafficLightAction>())
        {
            var defaultColor = TrafficLightButtons.GetDefaultColor(action);
            var pressedColor = TrafficLightButtons.GetPressedColor(action);

            // Pressed should be darker (lower luminance) than default
            var defaultLuminance = 0.299 * defaultColor.R + 0.587 * defaultColor.G + 0.114 * defaultColor.B;
            var pressedLuminance = 0.299 * pressedColor.R + 0.587 * pressedColor.G + 0.114 * pressedColor.B;
            Assert.True(pressedLuminance < defaultLuminance,
                $"{action}: pressed ({pressedLuminance:F1}) should be darker than default ({defaultLuminance:F1})");
        }
    }

    [Fact]
    public void InactiveColor_IsSameForAllButtons()
    {
        var inactive = TrafficLightButtons.GetInactiveColor();
        // All three buttons use the same inactive color
        Assert.Equal(inactive.R, inactive.G);
        Assert.Equal(inactive.G, inactive.B);
    }

    [Fact]
    public void AllThreeActions_HaveDistinctDefaultColors()
    {
        var close = TrafficLightButtons.GetDefaultColor(TrafficLightAction.Close);
        var minimize = TrafficLightButtons.GetDefaultColor(TrafficLightAction.Minimize);
        var maximize = TrafficLightButtons.GetDefaultColor(TrafficLightAction.Maximize);

        Assert.NotEqual(close, minimize);
        Assert.NotEqual(minimize, maximize);
        Assert.NotEqual(close, maximize);
    }

    [Fact]
    public void AllThreeActions_HaveDistinctGlyphColors()
    {
        var close = TrafficLightButtons.GetGlyphColor(TrafficLightAction.Close);
        var minimize = TrafficLightButtons.GetGlyphColor(TrafficLightAction.Minimize);
        var maximize = TrafficLightButtons.GetGlyphColor(TrafficLightAction.Maximize);

        Assert.NotEqual(close, minimize);
        Assert.NotEqual(minimize, maximize);
        Assert.NotEqual(close, maximize);
    }

    [Fact]
    public void InvalidAction_ThrowsArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrafficLightButtons.GetDefaultColor((TrafficLightAction)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrafficLightButtons.GetPressedColor((TrafficLightAction)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrafficLightButtons.GetGlyphColor((TrafficLightAction)99));
    }
}
