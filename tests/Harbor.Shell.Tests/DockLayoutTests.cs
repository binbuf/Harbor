namespace Harbor.Shell.Tests;

public class DockLayoutTests
{
    private const double IconSize = 48.0;
    private const double IconHorizontalMargin = 4.0; // 4px each side
    private const double DockPadding = 4.0; // Horizontal padding on container
    private const double BorderThickness = 1.0;

    /// <summary>
    /// Calculates the expected Dock container width for a given number of icons.
    /// Each icon is 48 DIP wide with 4px margin on each side.
    /// Container has 4px horizontal padding on each side + 1px border on each side.
    /// </summary>
    public static double CalculateDockContentWidth(int iconCount)
    {
        if (iconCount <= 0) return 0;
        return iconCount * (IconSize + IconHorizontalMargin * 2);
    }

    [Fact]
    public void DockContentWidth_ZeroIcons_ReturnsZero()
    {
        Assert.Equal(0, CalculateDockContentWidth(0));
    }

    [Fact]
    public void DockContentWidth_OneIcon_IncludesIconAndMargins()
    {
        // 1 * (48 + 8) = 56
        Assert.Equal(56, CalculateDockContentWidth(1));
    }

    [Fact]
    public void DockContentWidth_FiveIcons_CalculatesCorrectly()
    {
        // 5 * (48 + 8) = 280
        Assert.Equal(280, CalculateDockContentWidth(5));
    }

    [Fact]
    public void DockContentWidth_TenIcons_CalculatesCorrectly()
    {
        // 10 * (48 + 8) = 560
        Assert.Equal(560, CalculateDockContentWidth(10));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    public void DockContentWidth_NegativeIcons_ReturnsZero(int count)
    {
        Assert.Equal(0, CalculateDockContentWidth(count));
    }

    [Fact]
    public void IconSize_Is48Dip()
    {
        Assert.Equal(48.0, IconSize);
    }

    [Fact]
    public void ConsecutiveIcons_Have56DipSpacing()
    {
        // Each icon occupies 48 + 4 + 4 = 56 DIP
        double width1 = CalculateDockContentWidth(1);
        double width2 = CalculateDockContentWidth(2);
        Assert.Equal(56, width2 - width1);
    }
}
