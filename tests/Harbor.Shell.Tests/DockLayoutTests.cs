namespace Harbor.Shell.Tests;

public class DockLayoutTests
{
    private const double IconSize = 52.0;
    private const double IconHorizontalMargin = 8.0; // 8px each side (16px between icons)
    private const double DockPadding = 8.0; // Horizontal padding on container
    private const double BorderThickness = 1.0;

    /// <summary>
    /// Calculates the expected Dock container width for a given number of icons.
    /// Each icon is 52 DIP wide with 8px margin on each side.
    /// Container has 8px horizontal padding on each side + 1px border on each side.
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
        // 1 * (52 + 16) = 68
        Assert.Equal(68, CalculateDockContentWidth(1));
    }

    [Fact]
    public void DockContentWidth_FiveIcons_CalculatesCorrectly()
    {
        // 5 * (52 + 16) = 340
        Assert.Equal(340, CalculateDockContentWidth(5));
    }

    [Fact]
    public void DockContentWidth_TenIcons_CalculatesCorrectly()
    {
        // 10 * (52 + 16) = 680
        Assert.Equal(680, CalculateDockContentWidth(10));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    public void DockContentWidth_NegativeIcons_ReturnsZero(int count)
    {
        Assert.Equal(0, CalculateDockContentWidth(count));
    }

    [Fact]
    public void IconSize_Is52Dip()
    {
        Assert.Equal(52.0, IconSize);
    }

    [Fact]
    public void ConsecutiveIcons_Have68DipSpacing()
    {
        // Each icon occupies 52 + 8 + 8 = 68 DIP
        double width1 = CalculateDockContentWidth(1);
        double width2 = CalculateDockContentWidth(2);
        Assert.Equal(68, width2 - width1);
    }
}
