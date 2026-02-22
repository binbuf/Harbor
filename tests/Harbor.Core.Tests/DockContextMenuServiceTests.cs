using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class DockContextMenuServiceTests
{
    private const string TestExe = @"C:\app\test.exe";
    private const string TestName = "Test App";

    [Fact]
    public void PinnedApp_HasCorrectItems()
    {
        var items = DockContextMenuService.GetPinnedAppMenuItems(TestExe, TestName);

        Assert.Equal("Open", items[0].Label);
        Assert.Equal(DockMenuAction.Open, items[0].Action);

        Assert.Equal("Remove from Dock", items[1].Label);
        Assert.Equal(DockMenuAction.RemoveFromDock, items[1].Action);

        Assert.True(items[2].IsSeparator);

        Assert.Equal("Options", items[3].Label);
        Assert.True(items[3].IsSubmenuHeader);
        Assert.NotNull(items[3].Children);
        Assert.Equal(2, items[3].Children!.Count);
        Assert.Equal("Keep in Dock", items[3].Children![0].Label);
        Assert.True(items[3].Children![0].IsChecked); // already pinned
        Assert.Equal("Open at Login", items[3].Children![1].Label);
    }

    [Fact]
    public void RunningApp_HasCorrectItems()
    {
        var items = DockContextMenuService.GetRunningAppMenuItems(TestExe, TestName);

        Assert.Equal("Open", items[0].Label);
        Assert.True(items[1].IsSeparator);
        Assert.Equal("Options", items[2].Label);
        Assert.True(items[2].IsSubmenuHeader);
        Assert.True(items[3].IsSeparator);
        Assert.Equal("Quit", items[4].Label);
        Assert.Equal(DockMenuAction.Quit, items[4].Action);
    }

    [Fact]
    public void RunningApp_KeepInDock_NotChecked()
    {
        var items = DockContextMenuService.GetRunningAppMenuItems(TestExe, TestName);

        var options = items.First(i => i.IsSubmenuHeader);
        var keepInDock = options.Children!.First(c => c.Action == DockMenuAction.KeepInDock);
        Assert.False(keepInDock.IsChecked);
    }

    [Fact]
    public void PinnedRunningApp_HasQuitAndRemove()
    {
        var items = DockContextMenuService.GetPinnedRunningAppMenuItems(TestExe, TestName);

        Assert.Contains(items, i => i.Action == DockMenuAction.Quit);
        Assert.Contains(items, i => i.Action == DockMenuAction.RemoveFromDock);
    }

    [Fact]
    public void PinnedRunningApp_KeepInDock_IsChecked()
    {
        var items = DockContextMenuService.GetPinnedRunningAppMenuItems(TestExe, TestName);

        var options = items.First(i => i.IsSubmenuHeader);
        var keepInDock = options.Children!.First(c => c.Action == DockMenuAction.KeepInDock);
        Assert.True(keepInDock.IsChecked);
    }

    [Fact]
    public void GetMenuItems_PinnedOnly_ReturnsCorrectStructure()
    {
        var items = DockContextMenuService.GetMenuItems(TestExe, TestName, isPinned: true, isRunning: false);

        Assert.Equal("Open", items[0].Label);
        Assert.Equal("Remove from Dock", items[1].Label);
        Assert.DoesNotContain(items, i => i.Action == DockMenuAction.Quit);
    }

    [Fact]
    public void GetMenuItems_RunningOnly_ReturnsCorrectStructure()
    {
        var items = DockContextMenuService.GetMenuItems(TestExe, TestName, isPinned: false, isRunning: true);

        Assert.Contains(items, i => i.Action == DockMenuAction.Quit);
        Assert.DoesNotContain(items, i => i.Action == DockMenuAction.RemoveFromDock);
    }

    [Fact]
    public void GetMenuItems_PinnedAndRunning_HasBothQuitAndRemove()
    {
        var items = DockContextMenuService.GetMenuItems(TestExe, TestName, isPinned: true, isRunning: true);

        Assert.Contains(items, i => i.Action == DockMenuAction.Quit);
        Assert.Contains(items, i => i.Action == DockMenuAction.RemoveFromDock);
    }

    [Fact]
    public void GetMenuItems_OpenAtLoginTrue_SetsChecked()
    {
        var items = DockContextMenuService.GetMenuItems(TestExe, TestName, isPinned: false, isRunning: true, isOpenAtLogin: true);

        var options = items.First(i => i.IsSubmenuHeader);
        var openAtLogin = options.Children!.First(c => c.Action == DockMenuAction.OpenAtLogin);
        Assert.True(openAtLogin.IsChecked);
    }

    [Fact]
    public void GetMenuItems_OpenAtLoginFalse_NotChecked()
    {
        var items = DockContextMenuService.GetMenuItems(TestExe, TestName, isPinned: false, isRunning: true, isOpenAtLogin: false);

        var options = items.First(i => i.IsSubmenuHeader);
        var openAtLogin = options.Children!.First(c => c.Action == DockMenuAction.OpenAtLogin);
        Assert.False(openAtLogin.IsChecked);
    }

    [Fact]
    public void Separator_HasCorrectProperties()
    {
        var sep = DockMenuItem.Separator;
        Assert.True(sep.IsSeparator);
        Assert.Equal(DockMenuAction.None, sep.Action);
        Assert.Equal("", sep.Label);
    }

    [Fact]
    public void PinnedApp_HasFourTopLevelItems()
    {
        var items = DockContextMenuService.GetPinnedAppMenuItems(TestExe, TestName);
        Assert.Equal(4, items.Count); // Open, Remove, Separator, Options
    }

    [Fact]
    public void RunningApp_HasFiveTopLevelItems()
    {
        var items = DockContextMenuService.GetRunningAppMenuItems(TestExe, TestName);
        Assert.Equal(5, items.Count); // Open, Sep, Options, Sep, Quit
    }
}
