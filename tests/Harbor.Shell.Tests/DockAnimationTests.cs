namespace Harbor.Shell.Tests;

/// <summary>
/// Verifies that Dock animation timing values match the Design.md spec (Sections 5B, 5D).
/// </summary>
public class DockAnimationTests
{
    [Fact]
    public void HoverScale_Is1Point167()
    {
        // 56/48 = 1.16667
        Assert.Equal(56.0 / 48.0, Dock.HoverScaleFactor, precision: 3);
    }

    [Fact]
    public void PressedScale_Is0Point917()
    {
        // 44/48 = 0.91667
        Assert.Equal(44.0 / 48.0, Dock.PressedScaleFactor, precision: 3);
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
    public void BounceTranslation_Is12DipUpward()
    {
        // Negative Y means upward in WPF coordinate system
        Assert.Equal(-12.0, Dock.BounceTranslation);
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
    public void IconDefaultSize_Is48Dip()
    {
        Assert.Equal(48.0, Dock.IconDefaultSize);
    }

    [Fact]
    public void IconHoverSize_Is56Dip()
    {
        Assert.Equal(56.0, Dock.IconHoverSize);
    }

    [Fact]
    public void IconPressedSize_Is44Dip()
    {
        Assert.Equal(44.0, Dock.IconPressedSize);
    }
}
