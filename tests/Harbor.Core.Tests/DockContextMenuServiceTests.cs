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

    #region Window Grouping Tests

    [Fact]
    public void GetMenuItems_WithMultipleWindows_PrependsWindowList()
    {
        var windows = new List<(string Title, IntPtr Handle)>
        {
            ("Document 1", new IntPtr(100)),
            ("Document 2", new IntPtr(200)),
        };

        var items = DockContextMenuService.GetMenuItems(
            TestExe, TestName, isPinned: false, isRunning: true, windows: windows);

        // First two items should be window titles
        Assert.Equal("Document 1", items[0].Label);
        Assert.Equal(DockMenuAction.SwitchToWindow, items[0].Action);
        Assert.Equal("Document 2", items[1].Label);
        Assert.Equal(DockMenuAction.SwitchToWindow, items[1].Action);

        // Third item should be a separator between window list and regular menu
        Assert.True(items[2].IsSeparator);

        // Regular menu items follow after the separator
        Assert.Equal("Open", items[3].Label);
    }

    [Fact]
    public void GetMenuItems_WithMultipleWindows_HasCorrectWindowHandles()
    {
        var windows = new List<(string Title, IntPtr Handle)>
        {
            ("Window A", new IntPtr(111)),
            ("Window B", new IntPtr(222)),
        };

        var items = DockContextMenuService.GetMenuItems(
            TestExe, TestName, isPinned: false, isRunning: true, windows: windows);

        Assert.Equal(new IntPtr(111), items[0].WindowHandle);
        Assert.Equal(new IntPtr(222), items[1].WindowHandle);
    }

    [Fact]
    public void GetMenuItems_WithSingleWindow_DoesNotPrependWindowList()
    {
        var windows = new List<(string Title, IntPtr Handle)>
        {
            ("Only Window", new IntPtr(100)),
        };

        var items = DockContextMenuService.GetMenuItems(
            TestExe, TestName, isPinned: false, isRunning: true, windows: windows);

        // Should be the same as without windows — no window list prepended
        Assert.Equal("Open", items[0].Label);
        Assert.DoesNotContain(items, i => i.Action == DockMenuAction.SwitchToWindow);
    }

    [Fact]
    public void GetMenuItems_WithoutWindows_BackwardCompatible()
    {
        // Calling without the windows parameter should work as before
        var items = DockContextMenuService.GetMenuItems(
            TestExe, TestName, isPinned: false, isRunning: true);

        Assert.Equal("Open", items[0].Label);
        Assert.DoesNotContain(items, i => i.Action == DockMenuAction.SwitchToWindow);
    }

    [Fact]
    public void GetWindowListItems_CreatesCorrectItems()
    {
        var windows = new List<(string Title, IntPtr Handle)>
        {
            ("First", new IntPtr(1)),
            ("Second", new IntPtr(2)),
            ("Third", new IntPtr(3)),
        };

        var items = DockContextMenuService.GetWindowListItems(windows);

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(DockMenuAction.SwitchToWindow, i.Action));
        Assert.Equal("First", items[0].Label);
        Assert.Equal(new IntPtr(1), items[0].WindowHandle);
        Assert.Equal("Second", items[1].Label);
        Assert.Equal(new IntPtr(2), items[1].WindowHandle);
        Assert.Equal("Third", items[2].Label);
        Assert.Equal(new IntPtr(3), items[2].WindowHandle);
    }

    [Fact]
    public void GetWindowListItems_EmptyTitle_ShowsUntitled()
    {
        var windows = new List<(string Title, IntPtr Handle)>
        {
            ("", new IntPtr(1)),
            ("  ", new IntPtr(2)),
        };

        var items = DockContextMenuService.GetWindowListItems(windows);

        Assert.Equal("(Untitled)", items[0].Label);
        Assert.Equal("(Untitled)", items[1].Label);
    }

    [Fact]
    public void DockMenuItem_WindowHandle_DefaultsToZero()
    {
        var item = new DockMenuItem("Test", DockMenuAction.Open);
        Assert.Equal(IntPtr.Zero, item.WindowHandle);
    }

    [Fact]
    public void GetMenuItems_WithMultipleWindows_PinnedRunning_PrependsWindowList()
    {
        var windows = new List<(string Title, IntPtr Handle)>
        {
            ("Win 1", new IntPtr(10)),
            ("Win 2", new IntPtr(20)),
        };

        var items = DockContextMenuService.GetMenuItems(
            TestExe, TestName, isPinned: true, isRunning: true, windows: windows);

        // Window list at top
        Assert.Equal("Win 1", items[0].Label);
        Assert.Equal(DockMenuAction.SwitchToWindow, items[0].Action);
        Assert.Equal("Win 2", items[1].Label);
        Assert.True(items[2].IsSeparator);

        // Regular pinned+running items follow
        Assert.Equal("Open", items[3].Label);
        Assert.Contains(items, i => i.Action == DockMenuAction.Quit);
        Assert.Contains(items, i => i.Action == DockMenuAction.RemoveFromDock);
    }

    #endregion
}
