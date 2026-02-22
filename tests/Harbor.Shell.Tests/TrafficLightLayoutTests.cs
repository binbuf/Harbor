namespace Harbor.Shell.Tests;

/// <summary>
/// Tests for traffic light button positioning math (spacing, centering).
/// </summary>
public class TrafficLightLayoutTests
{
    [Fact]
    public void ButtonDiameter_Is12Dip()
    {
        Assert.Equal(12.0, TrafficLightButtons.ButtonDiameter);
    }

    [Fact]
    public void ButtonSpacing_Is8Dip()
    {
        Assert.Equal(8.0, TrafficLightButtons.ButtonSpacing);
    }

    [Fact]
    public void CenterToCenter_Is20Dip()
    {
        Assert.Equal(20.0, TrafficLightButtons.CenterToCenter);
    }

    [Fact]
    public void LeftPadding_Is8Dip()
    {
        Assert.Equal(8.0, TrafficLightButtons.LeftPadding);
    }

    [Fact]
    public void CenterToCenter_EqualsDiameterPlusSpacing()
    {
        Assert.Equal(
            TrafficLightButtons.ButtonDiameter + TrafficLightButtons.ButtonSpacing,
            TrafficLightButtons.CenterToCenter);
    }

    [Fact]
    public void CloseButton_LeftPosition_Is2Dip()
    {
        // LeftPadding(8) - ButtonDiameter/2(6) = 2
        var left = TrafficLightButtons.CalculateButtonLeft(0);
        Assert.Equal(2.0, left);
    }

    [Fact]
    public void MinimizeButton_LeftPosition_Is22Dip()
    {
        // LeftPadding(8) + CenterToCenter(20) - ButtonDiameter/2(6) = 22
        var left = TrafficLightButtons.CalculateButtonLeft(1);
        Assert.Equal(22.0, left);
    }

    [Fact]
    public void MaximizeButton_LeftPosition_Is42Dip()
    {
        // LeftPadding(8) + 2*CenterToCenter(40) - ButtonDiameter/2(6) = 42
        var left = TrafficLightButtons.CalculateButtonLeft(2);
        Assert.Equal(42.0, left);
    }

    [Fact]
    public void ConsecutiveButtons_Have20DipCenterToCenter()
    {
        var closeLeft = TrafficLightButtons.CalculateButtonLeft(0);
        var minimizeLeft = TrafficLightButtons.CalculateButtonLeft(1);
        var maximizeLeft = TrafficLightButtons.CalculateButtonLeft(2);

        // Center positions: left + diameter/2
        var closeCenter = closeLeft + TrafficLightButtons.ButtonDiameter / 2.0;
        var minimizeCenter = minimizeLeft + TrafficLightButtons.ButtonDiameter / 2.0;
        var maximizeCenter = maximizeLeft + TrafficLightButtons.ButtonDiameter / 2.0;

        Assert.Equal(20.0, minimizeCenter - closeCenter);
        Assert.Equal(20.0, maximizeCenter - minimizeCenter);
    }

    [Fact]
    public void FirstButtonCenter_AtLeftPadding()
    {
        var left = TrafficLightButtons.CalculateButtonLeft(0);
        var center = left + TrafficLightButtons.ButtonDiameter / 2.0;
        Assert.Equal(TrafficLightButtons.LeftPadding, center);
    }

    [Theory]
    [InlineData(24.0, 6.0)]  // 24 DIP title bar → top at 6
    [InlineData(30.0, 9.0)]  // 30 DIP title bar → top at 9
    [InlineData(32.0, 10.0)] // 32 DIP title bar → top at 10
    [InlineData(12.0, 0.0)]  // Exact fit → top at 0
    public void ButtonTop_VerticallyCentered(double containerHeight, double expectedTop)
    {
        var top = TrafficLightButtons.CalculateButtonTop(containerHeight);
        Assert.Equal(expectedTop, top);
    }

    [Fact]
    public void ButtonTop_CentersWithinContainer()
    {
        const double containerHeight = 30.0;
        var top = TrafficLightButtons.CalculateButtonTop(containerHeight);
        var buttonBottom = top + TrafficLightButtons.ButtonDiameter;
        var topMargin = top;
        var bottomMargin = containerHeight - buttonBottom;
        Assert.Equal(topMargin, bottomMargin);
    }
}
