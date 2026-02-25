using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class DockMagnificationCalculatorTests
{
    private const double MaxScale = 2.0;
    private const double EffectRadius = 2.5;
    private const double IconPitch = 80.0;

    [Fact]
    public void IconDirectlyUnderCursor_GetsMaxScale()
    {
        var centers = new[] { 0.0, 80.0, 160.0, 240.0, 320.0 };
        var scales = DockMagnificationCalculator.ComputeScales(160.0, centers, MaxScale, EffectRadius, IconPitch);

        Assert.Equal(MaxScale, scales[2], precision: 6);
    }

    [Fact]
    public void IconsBeyondEffectRadius_GetScaleOne()
    {
        var centers = new[] { 0.0, 80.0, 160.0, 240.0, 320.0, 400.0, 480.0, 560.0 };
        // Mouse at icon 0 (x=0), effect radius = 3 slots = icons 0,1,2,3
        var scales = DockMagnificationCalculator.ComputeScales(0.0, centers, MaxScale, EffectRadius, IconPitch);

        // Icons at 320 (4 slots away), 400, 480, 560 should be 1.0
        Assert.Equal(1.0, scales[4], precision: 6);
        Assert.Equal(1.0, scales[5], precision: 6);
        Assert.Equal(1.0, scales[6], precision: 6);
        Assert.Equal(1.0, scales[7], precision: 6);
    }

    [Fact]
    public void SymmetricScaling_EqualDistanceEqualsEqualScale()
    {
        var centers = new[] { 0.0, 80.0, 160.0, 240.0, 320.0 };
        // Mouse at center icon (112)
        var scales = DockMagnificationCalculator.ComputeScales(160.0, centers, MaxScale, EffectRadius, IconPitch);

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
        var offset = DockMagnificationCalculator.ComputeVerticalOffset(1.0, 52.0);
        Assert.Equal(0.0, offset, precision: 6);
    }

    [Fact]
    public void VerticalOffset_ScaleGreaterThanOne_ReturnsNegative()
    {
        var offset = DockMagnificationCalculator.ComputeVerticalOffset(2.0, 52.0);

        // Should return negative value (upward translation) for pop-out effect
        Assert.True(offset < 0);
        // Expected: -(2.0 - 1.0) * 52.0 * 0.35 = -18.2
        Assert.Equal(-18.2, offset, precision: 6);
    }

    [Fact]
    public void ScalesDecreaseSmoothlyFromCursor()
    {
        var centers = new[] { 0.0, 80.0, 160.0, 240.0, 320.0 };
        var scales = DockMagnificationCalculator.ComputeScales(160.0, centers, MaxScale, EffectRadius, IconPitch);

        // Icon at cursor should be largest
        Assert.True(scales[2] > scales[1]);
        Assert.True(scales[1] > scales[0]);
    }
}
