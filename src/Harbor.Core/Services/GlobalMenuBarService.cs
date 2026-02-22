using System.Diagnostics;
using System.Windows.Automation;

namespace Harbor.Core.Services;

/// <summary>
/// Represents a single top-level menu item discovered via UI Automation.
/// </summary>
public sealed record GlobalMenuItem(string Name, nint WindowHandle, int[] RuntimeId);

/// <summary>
/// Queries the active window's menu bar via UI Automation and exposes
/// the top-level menu item names. Activates items by expanding them
/// through UIA patterns.
/// </summary>
public sealed class GlobalMenuBarService : IDisposable
{
    private static readonly TimeSpan UiaTimeout = TimeSpan.FromMilliseconds(150);

    private bool _disposed;

    public event Action<IReadOnlyList<GlobalMenuItem>>? MenuItemsChanged;

    /// <summary>
    /// Queries UIA for the menu bar of the given window and fires <see cref="MenuItemsChanged"/>.
    /// Safe to call from any thread — UIA work runs on the thread pool.
    /// </summary>
    public async void UpdateForWindow(nint hwnd)
    {
        if (_disposed) return;

        var items = await Task.Run(() => QueryMenuItems(hwnd));
        MenuItemsChanged?.Invoke(items);
    }

    /// <summary>
    /// Activates a previously discovered menu item by re-finding it via RuntimeId
    /// and invoking ExpandCollapse or Invoke.
    /// </summary>
    public void ActivateMenuItem(GlobalMenuItem item)
    {
        if (_disposed) return;

        Task.Run(() =>
        {
            try
            {
                var element = AutomationElement.FromHandle(item.WindowHandle);
                if (element is null) return;

                var menuBar = element.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar));

                if (menuBar is null) return;

                var condition = new PropertyCondition(
                    AutomationElement.RuntimeIdProperty, item.RuntimeId);
                var target = menuBar.FindFirst(TreeScope.Children, condition);

                if (target is null) return;

                if (target.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandObj))
                {
                    ((ExpandCollapsePattern)expandObj).Expand();
                }
                else if (target.TryGetCurrentPattern(InvokePattern.Pattern, out var invokeObj))
                {
                    ((InvokePattern)invokeObj).Invoke();
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Harbor] GlobalMenuBarService: ActivateMenuItem failed: {ex.Message}");
            }
        });
    }

    private static IReadOnlyList<GlobalMenuItem> QueryMenuItems(nint hwnd)
    {
        if (hwnd == 0) return [];

        try
        {
            AutomationElement? menuBar = null;

            var task = Task.Run(() =>
            {
                var element = AutomationElement.FromHandle(hwnd);
                if (element is null) return;

                menuBar = element.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuBar));
            });

            if (!task.Wait(UiaTimeout))
            {
                Trace.WriteLine($"[Harbor] GlobalMenuBarService: UIA timeout for HWND {hwnd}");
                return [];
            }

            if (task.IsFaulted || menuBar is null)
                return [];

            var children = menuBar.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));

            var items = new List<GlobalMenuItem>(children.Count);
            foreach (AutomationElement child in children)
            {
                var name = child.Current.Name;
                if (string.IsNullOrWhiteSpace(name)) continue;

                items.Add(new GlobalMenuItem(name, hwnd, child.GetRuntimeId()));
            }

            return items;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] GlobalMenuBarService: QueryMenuItems failed: {ex.Message}");
            return [];
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Trace.WriteLine("[Harbor] GlobalMenuBarService: Disposed.");
    }
}
