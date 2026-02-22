namespace Harbor.Shell.Tests;

public class TrayIconLayoutTests
{
    private const double IconSize = 18.0;
    private const double IconMargin = 4.0; // Each side of the 8px spacing (4px left + 4px right per icon)
    private const double TrailingMargin = 8.0; // Right margin of the tray area

    /// <summary>
    /// Calculates total width of the tray icon area given a number of icons.
    /// Each icon is 18 DIP wide with 4px margin on each side (8px between icons).
    /// Plus 8px trailing margin on the right side of the area.
    /// </summary>
    public static double CalculateTrayWidth(int iconCount)
    {
        if (iconCount <= 0) return 0;
        return iconCount * (IconSize + IconMargin * 2) + TrailingMargin;
    }

    /// <summary>
    /// Calculates the X offset of a specific icon (0-indexed, left-to-right).
    /// </summary>
    public static double CalculateIconOffset(int index)
    {
        return index * (IconSize + IconMargin * 2) + IconMargin;
    }

    [Fact]
    public void TrayWidth_ZeroIcons_ReturnsZero()
    {
        Assert.Equal(0, CalculateTrayWidth(0));
    }

    [Fact]
    public void TrayWidth_OneIcon_IncludesIconAndMargins()
    {
        // 1 * (18 + 8) + 8 = 34
        Assert.Equal(34, CalculateTrayWidth(1));
    }

    [Fact]
    public void TrayWidth_FiveIcons_CalculatesCorrectly()
    {
        // 5 * (18 + 8) + 8 = 138
        Assert.Equal(138, CalculateTrayWidth(5));
    }

    [Fact]
    public void IconOffset_FirstIcon_StartsAtMargin()
    {
        // First icon at index 0: 0 * 26 + 4 = 4
        Assert.Equal(4, CalculateIconOffset(0));
    }

    [Fact]
    public void IconOffset_SecondIcon_AccountsForSpacing()
    {
        // Second icon at index 1: 1 * 26 + 4 = 30
        Assert.Equal(30, CalculateIconOffset(1));
    }

    [Fact]
    public void IconOffset_ConsecutiveIcons_Have26DipSpacing()
    {
        // Spacing between consecutive icons = (IconSize + 2*Margin) = 26
        double offset0 = CalculateIconOffset(0);
        double offset1 = CalculateIconOffset(1);
        Assert.Equal(26, offset1 - offset0);
    }

    [Fact]
    public void IconSize_Is18Dip()
    {
        Assert.Equal(18.0, IconSize);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    public void TrayWidth_NegativeIcons_ReturnsZero(int count)
    {
        Assert.Equal(0, CalculateTrayWidth(count));
    }
}
