namespace Harbor.Shell.Tests;

public class DockLayoutTests
{
    private const double IconSize = 102.0;
    private const double IconHorizontalMargin = 16.0; // 16px each side (32px between icons)
    private const double DockPadding = 16.0; // Horizontal padding on container
    private const double BorderThickness = 1.0;

    /// <summary>
    /// Calculates the expected Dock container width for a given number of icons.
    /// Each icon is 102 DIP wide with 16px margin on each side.
    /// Container has 16px horizontal padding on each side + 1px border on each side.
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
        // 1 * (102 + 32) = 134
        Assert.Equal(134, CalculateDockContentWidth(1));
    }

    [Fact]
    public void DockContentWidth_FiveIcons_CalculatesCorrectly()
    {
        // 5 * (102 + 32) = 670
        Assert.Equal(670, CalculateDockContentWidth(5));
    }

    [Fact]
    public void DockContentWidth_TenIcons_CalculatesCorrectly()
    {
        // 10 * (102 + 32) = 1340
        Assert.Equal(1340, CalculateDockContentWidth(10));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    public void DockContentWidth_NegativeIcons_ReturnsZero(int count)
    {
        Assert.Equal(0, CalculateDockContentWidth(count));
    }

    [Fact]
    public void IconSize_Is102Dip()
    {
        Assert.Equal(102.0, IconSize);
    }

    [Fact]
    public void ConsecutiveIcons_Have134DipSpacing()
    {
        // Each icon occupies 102 + 16 + 16 = 134 DIP
        double width1 = CalculateDockContentWidth(1);
        double width2 = CalculateDockContentWidth(2);
        Assert.Equal(134, width2 - width1);
    }
}
