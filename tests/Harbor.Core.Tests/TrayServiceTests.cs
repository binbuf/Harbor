using Harbor.Core.Services;

namespace Harbor.Core.Tests;

[Collection("ShellServices")]
public class TrayServiceTests
{
    private readonly ShellServicesFixture _fixture;

    public TrayServiceTests(ShellServicesFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void NotificationArea_TrayIcons_IsAccessibleAndNotFailed()
    {
        // TrayService requires being the registered shell host to intercept icons,
        // so in test context we verify the collection is accessible and the service didn't fail.
        var area = _fixture.Services.NotificationArea;
        Assert.NotNull(area.TrayIcons);
        Assert.False(area.IsFailed, "NotificationArea should not be in a failed state");
    }

    [Fact]
    public void NotificationArea_TrayIcons_HaveNonEmptyIdentifiers()
    {
        var icons = _fixture.Services.NotificationArea.TrayIcons;
        foreach (var icon in icons)
        {
            // Each icon should have a valid identifier (GUID or path-based)
            Assert.False(string.IsNullOrEmpty(icon.Identifier),
                "Tray icon Identifier should not be null or empty");
        }
    }

    [Fact]
    public void GetDoubleClickTime_ReturnsPositiveValue()
    {
        uint time = Harbor.Core.Interop.SystemInterop.GetDoubleClickTime();
        Assert.True(time > 0, $"DoubleClickTime {time} should be positive");
    }
}
