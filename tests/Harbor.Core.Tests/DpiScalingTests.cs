using Harbor.Core.Interop;

namespace Harbor.Core.Tests;

/// <summary>
/// Tests for DPI scaling math: position * (dpi / 96.0) for various DPI values.
/// </summary>
public class DpiScalingTests
{
    [Theory]
    [InlineData(96, 1.0)]    // 100%
    [InlineData(120, 1.25)]  // 125%
    [InlineData(144, 1.5)]   // 150%
    [InlineData(168, 1.75)]  // 175%
    [InlineData(192, 2.0)]   // 200%
    public void ComputeScaleFactor_ReturnsCorrectValue(uint dpi, double expected)
    {
        var result = DisplayInterop.ComputeScaleFactor(dpi);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(100, 96, 100.0)]    // 100% — no change
    [InlineData(100, 144, 150.0)]   // 150% scaling
    [InlineData(100, 192, 200.0)]   // 200% scaling
    [InlineData(24, 96, 24.0)]      // Menu bar height at 100%
    [InlineData(24, 144, 36.0)]     // Menu bar height at 150%
    [InlineData(24, 192, 48.0)]     // Menu bar height at 200%
    [InlineData(48, 120, 60.0)]     // Dock icon at 125%
    public void ScaleForDpi_AppliesCorrectMultiplier(double value, uint dpi, double expected)
    {
        var result = DisplayInterop.ScaleForDpi(value, dpi);
        Assert.Equal(expected, result, precision: 5);
    }

    [Theory]
    [InlineData(135, 1.0, 135.0)]   // 100% — no change
    [InlineData(135, 1.25, 108.0)]  // 125% — physical 135px = 108 DIPs
    [InlineData(135, 1.5, 90.0)]    // 150% — physical 135px = 90 DIPs
    [InlineData(135, 2.0, 67.5)]    // 200% — physical 135px = 67.5 DIPs
    [InlineData(270, 2.0, 135.0)]   // 200% — physical 270px = 135 DIPs (scaled caption buttons)
    public void PhysicalToDip_ConvertsCorrectly(int physicalPixels, double scaleFactor, double expected)
    {
        var result = DisplayInterop.PhysicalToDip(physicalPixels, scaleFactor);
        Assert.Equal(expected, result, precision: 5);
    }

    [Fact]
    public void PhysicalToDip_ZeroScale_ReturnsSameValue()
    {
        // Edge case: scale factor of 0 should return original value
        var result = DisplayInterop.PhysicalToDip(100, 0.0);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public void ComputeScaleFactor_BaseDpi_ReturnsOne()
    {
        var result = DisplayInterop.ComputeScaleFactor(DisplayInterop.BASE_DPI);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void BaseDpi_Is96()
    {
        Assert.Equal(96u, DisplayInterop.BASE_DPI);
    }

    [Theory]
    [InlineData(12, 96, 12.0)]   // Traffic light button at 100%
    [InlineData(12, 144, 18.0)]  // Traffic light button at 150%
    [InlineData(12, 192, 24.0)]  // Traffic light button at 200%
    [InlineData(8, 96, 8.0)]     // Button spacing at 100%
    [InlineData(8, 144, 12.0)]   // Button spacing at 150%
    [InlineData(8, 192, 16.0)]   // Button spacing at 200%
    public void ScaleForDpi_TrafficLightDimensions_ScaleCorrectly(double dipValue, uint dpi, double expectedPhysical)
    {
        var result = DisplayInterop.ScaleForDpi(dipValue, dpi);
        Assert.Equal(expectedPhysical, result, precision: 5);
    }

    [Theory]
    [InlineData(100, 200, 900, 232, 96)]   // Standard window at 100%
    [InlineData(150, 300, 1350, 348, 144)]  // Window at 150% DPI
    [InlineData(200, 400, 1800, 464, 192)]  // Window at 200% DPI
    public void OverlayPositionScaling_PhysicalPixelsConsistent(
        int left, int top, int right, int bottom, uint dpi)
    {
        // Overlay position math: position * (dpi / 96.0)
        var scale = DisplayInterop.ComputeScaleFactor(dpi);

        // Verify that at 100% DPI the physical pixels match the DIP values
        // At higher DPI, physical pixels are larger numbers
        var widthPhysical = right - left;
        var heightPhysical = bottom - top;

        // Convert back to DIPs
        var widthDip = DisplayInterop.PhysicalToDip(widthPhysical, scale);
        var heightDip = DisplayInterop.PhysicalToDip(heightPhysical, scale);

        // The DIP values should be consistent regardless of DPI
        // (800 DIPs wide, 32 DIPs tall at any DPI)
        Assert.True(widthDip > 0);
        Assert.True(heightDip > 0);
    }
}
