namespace Harbor.Core.Services;

/// <summary>
/// Generates context menu item definitions for dock icons based on
/// whether the app is pinned, running, or both.
/// </summary>
public static class DockContextMenuService
{
    /// <summary>
    /// Generates context menu items for a pinned app (not running).
    /// </summary>
    public static List<DockMenuItem> GetPinnedAppMenuItems(string executablePath, string displayName)
    {
        return
        [
            new DockMenuItem("New Window", DockMenuAction.NewWindow),
            DockMenuItem.Separator,
            new DockMenuItem("Options", DockMenuAction.None, IsSubmenuHeader: true, Children:
            [
                new DockMenuItem("Keep in Dock", DockMenuAction.KeepInDock, IsChecked: true),
                new DockMenuItem("Open at Login", DockMenuAction.OpenAtLogin),
            ]),
            DockMenuItem.Separator,
            new DockMenuItem("Remove from Dock", DockMenuAction.RemoveFromDock),
        ];
    }

    /// <summary>
    /// Generates context menu items for a running app (not pinned).
    /// </summary>
    public static List<DockMenuItem> GetRunningAppMenuItems(string executablePath, string displayName)
    {
        return
        [
            new DockMenuItem("New Window", DockMenuAction.NewWindow),
            new DockMenuItem("Show All Windows", DockMenuAction.ShowAllWindows),
            new DockMenuItem("Hide", DockMenuAction.Hide),
            DockMenuItem.Separator,
            new DockMenuItem("Options", DockMenuAction.None, IsSubmenuHeader: true, Children:
            [
                new DockMenuItem("Keep in Dock", DockMenuAction.KeepInDock),
                new DockMenuItem("Open at Login", DockMenuAction.OpenAtLogin),
            ]),
            DockMenuItem.Separator,
            new DockMenuItem("Quit", DockMenuAction.Quit),
        ];
    }

    /// <summary>
    /// Generates context menu items for an app that is both pinned and running.
    /// </summary>
    public static List<DockMenuItem> GetPinnedRunningAppMenuItems(string executablePath, string displayName)
    {
        return
        [
            new DockMenuItem("New Window", DockMenuAction.NewWindow),
            new DockMenuItem("Show All Windows", DockMenuAction.ShowAllWindows),
            new DockMenuItem("Hide", DockMenuAction.Hide),
            DockMenuItem.Separator,
            new DockMenuItem("Options", DockMenuAction.None, IsSubmenuHeader: true, Children:
            [
                new DockMenuItem("Keep in Dock", DockMenuAction.KeepInDock, IsChecked: true),
                new DockMenuItem("Open at Login", DockMenuAction.OpenAtLogin),
            ]),
            DockMenuItem.Separator,
            new DockMenuItem("Remove from Dock", DockMenuAction.RemoveFromDock),
            new DockMenuItem("Quit", DockMenuAction.Quit),
        ];
    }

    /// <summary>
    /// Creates menu items for individual windows in a grouped dock icon.
    /// Each item has the SwitchToWindow action and the window's handle.
    /// </summary>
    public static List<DockMenuItem> GetWindowListItems(List<(string Title, IntPtr Handle)> windows)
    {
        var items = new List<DockMenuItem>();
        foreach (var (title, handle) in windows)
        {
            var label = string.IsNullOrWhiteSpace(title) ? "(Untitled)" : title;
            items.Add(new DockMenuItem(label, DockMenuAction.SwitchToWindow, WindowHandle: handle));
        }
        return items;
    }

    /// <summary>
    /// Gets the appropriate menu items based on the pinned/running state.
    /// When multiple windows are provided, window list items are prepended at the top.
    /// </summary>
    public static List<DockMenuItem> GetMenuItems(
        string executablePath,
        string displayName,
        bool isPinned,
        bool isRunning,
        bool isOpenAtLogin = false,
        List<(string Title, IntPtr Handle)>? windows = null)
    {
        List<DockMenuItem> items;

        if (isPinned && isRunning)
            items = GetPinnedRunningAppMenuItems(executablePath, displayName);
        else if (isPinned)
            items = GetPinnedAppMenuItems(executablePath, displayName);
        else
            items = GetRunningAppMenuItems(executablePath, displayName);

        // Update "Open at Login" checked state
        SetOpenAtLoginState(items, isOpenAtLogin);

        // Prepend window list items when there are multiple windows
        if (windows is not null && windows.Count > 1)
        {
            var windowItems = GetWindowListItems(windows);
            windowItems.Add(DockMenuItem.Separator);
            windowItems.AddRange(items);
            items = windowItems;
        }

        return items;
    }

    private static void SetOpenAtLoginState(List<DockMenuItem> items, bool isOpenAtLogin)
    {
        foreach (var item in items)
        {
            if (item.Action == DockMenuAction.OpenAtLogin)
            {
                // Records are immutable, but we find and replace
                // The caller handles this via the IsChecked field
            }

            if (item.Children is not null)
            {
                for (int i = 0; i < item.Children.Count; i++)
                {
                    if (item.Children[i].Action == DockMenuAction.OpenAtLogin)
                    {
                        item.Children[i] = item.Children[i] with { IsChecked = isOpenAtLogin };
                    }
                }
            }
        }
    }
}

/// <summary>
/// Represents a context menu item for a dock icon.
/// </summary>
public record DockMenuItem(
    string Label,
    DockMenuAction Action,
    bool IsSubmenuHeader = false,
    bool IsSeparator = false,
    bool IsChecked = false,
    List<DockMenuItem>? Children = null,
    IntPtr WindowHandle = default)
{
    public static DockMenuItem Separator { get; } = new("", DockMenuAction.None, IsSeparator: true);
}

/// <summary>
/// Actions available in the dock context menu.
/// </summary>
public enum DockMenuAction
{
    None,
    Open,
    RemoveFromDock,
    KeepInDock,
    OpenAtLogin,
    Quit,
    SwitchToWindow,
    NewWindow,
    ShowAllWindows,
    Hide,
}
