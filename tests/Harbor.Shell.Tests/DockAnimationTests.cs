namespace Harbor.Shell.Tests;

/// <summary>
/// Verifies that Dock animation timing values match the Design.md spec (Sections 5B, 5D).
/// </summary>
public class DockAnimationTests
{
    [Fact]
    public void HoverScale_IsCorrectRatio()
    {
        // 119/102 ≈ 1.167
        Assert.Equal(119.0 / 102.0, Dock.HoverScaleFactor, precision: 3);
    }

    [Fact]
    public void PressedScale_IsCorrectRatio()
    {
        // 94/102 ≈ 0.922
        Assert.Equal(94.0 / 102.0, Dock.PressedScaleFactor, precision: 3);
    }

    [Fact]
    public void HoverScaleDuration_Is150ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(150), Dock.HoverScaleDuration.TimeSpan);
    }

    [Fact]
    public void PressScaleDownDuration_Is80ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(80), Dock.PressScaleDownDuration.TimeSpan);
    }

    [Fact]
    public void PressScaleUpDuration_Is100ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(100), Dock.PressScaleUpDuration.TimeSpan);
    }

    [Fact]
    public void BounceTranslation_Is25DipUpward()
    {
        // Negative Y means upward in WPF coordinate system
        Assert.Equal(-25.0, Dock.BounceTranslation);
    }

    [Fact]
    public void BounceCount_Is3()
    {
        Assert.Equal(3, Dock.BounceCount);
    }

    [Fact]
    public void SingleBounceDuration_Is300ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(300), Dock.SingleBounceDuration.TimeSpan);
    }

    [Fact]
    public void TotalBounceDuration_Is900ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(900), Dock.TotalBounceDuration.TimeSpan);
    }

    [Fact]
    public void TotalBounceDuration_EqualsBounceCountTimesSingleBounce()
    {
        Assert.Equal(
            Dock.BounceCount * Dock.SingleBounceDuration.TimeSpan.TotalMilliseconds,
            Dock.TotalBounceDuration.TimeSpan.TotalMilliseconds);
    }

    [Fact]
    public void ShowAnimationDuration_Is250ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), Dock.ShowAnimationDuration.TimeSpan);
    }

    [Fact]
    public void HideAnimationDuration_Is200ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), Dock.HideAnimationDuration.TimeSpan);
    }

    [Fact]
    public void IconDefaultSize_Is102Dip()
    {
        Assert.Equal(102.0, Dock.IconDefaultSize);
    }

    [Fact]
    public void IconHoverSize_Is119Dip()
    {
        Assert.Equal(119.0, Dock.IconHoverSize);
    }

    [Fact]
    public void IconPressedSize_Is94Dip()
    {
        Assert.Equal(94.0, Dock.IconPressedSize);
    }
}
