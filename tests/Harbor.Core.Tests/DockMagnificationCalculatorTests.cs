using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class DockMagnificationCalculatorTests
{
    private const double MaxScale = 1.5;
    private const double EffectRadius = 3.0;
    private const double IconPitch = 56.0;

    [Fact]
    public void IconDirectlyUnderCursor_GetsMaxScale()
    {
        var centers = new[] { 0.0, 56.0, 112.0, 168.0, 224.0 };
        var scales = DockMagnificationCalculator.ComputeScales(112.0, centers, MaxScale, EffectRadius, IconPitch);

        Assert.Equal(MaxScale, scales[2], precision: 6);
    }

    [Fact]
    public void IconsBeyondEffectRadius_GetScaleOne()
    {
        var centers = new[] { 0.0, 56.0, 112.0, 168.0, 224.0, 280.0, 336.0, 392.0 };
        // Mouse at icon 0 (x=0), effect radius = 3 slots = icons 0,1,2,3
        var scales = DockMagnificationCalculator.ComputeScales(0.0, centers, MaxScale, EffectRadius, IconPitch);

        // Icons at 224 (4 slots away), 280, 336, 392 should be 1.0
        Assert.Equal(1.0, scales[4], precision: 6);
        Assert.Equal(1.0, scales[5], precision: 6);
        Assert.Equal(1.0, scales[6], precision: 6);
        Assert.Equal(1.0, scales[7], precision: 6);
    }

    [Fact]
    public void SymmetricScaling_EqualDistanceEqualsEqualScale()
    {
        var centers = new[] { 0.0, 56.0, 112.0, 168.0, 224.0 };
        // Mouse at center icon (112)
        var scales = DockMagnificationCalculator.ComputeScales(112.0, centers, MaxScale, EffectRadius, IconPitch);

        // Icons equidistant from cursor should have equal scale
        Assert.Equal(scales[1], scales[3], precision: 6); // 1 slot away on each side
        Assert.Equal(scales[0], scales[4], precision: 6); // 2 slots away on each side
    }

    [Fact]
    public void EmptyIconArray_ReturnsEmpty()
    {
        var scales = DockMagnificationCalculator.ComputeScales(100.0, [], MaxScale, EffectRadius, IconPitch);

        Assert.Empty(scales);
    }

    [Fact]
    public void VerticalOffset_ScaleOne_ReturnsZero()
    {
        var offset = DockMagnificationCalculator.ComputeVerticalOffset(1.0, 48.0);
        Assert.Equal(0.0, offset, precision: 6);
    }

    [Fact]
    public void VerticalOffset_ScaleGreaterThanOne_ReturnsNegative()
    {
        var offset = DockMagnificationCalculator.ComputeVerticalOffset(1.5, 48.0);

        // Should be -(1.5 - 1.0) * 48 / 2 = -12.0
        Assert.Equal(-12.0, offset, precision: 6);
    }

    [Fact]
    public void ScalesDecreaseSmoothlyFromCursor()
    {
        var centers = new[] { 0.0, 56.0, 112.0, 168.0, 224.0 };
        var scales = DockMagnificationCalculator.ComputeScales(112.0, centers, MaxScale, EffectRadius, IconPitch);

        // Icon at cursor should be largest
        Assert.True(scales[2] > scales[1]);
        Assert.True(scales[1] > scales[0]);
    }
}
