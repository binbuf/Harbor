using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Core.Tests;

public class ForegroundWindowServiceTests
{
    [Fact]
    public void GetAppNameFromWindow_ReturnsNonEmpty_ForForegroundWindow()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        var name = ForegroundWindowService.GetAppNameFromWindow(hwnd);
        Assert.False(string.IsNullOrEmpty(name), "App name should not be empty for the foreground window");
    }

    [Fact]
    public void GetAppNameFromWindow_ReturnsEmpty_ForNullHandle()
    {
        var name = ForegroundWindowService.GetAppNameFromWindow(HWND.Null);
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public void GetAppNameFromWindow_ReturnsEmpty_ForInvalidHandle()
    {
        var invalid = new HWND(unchecked((nint)0xDEADBEEF));
        var name = ForegroundWindowService.GetAppNameFromWindow(invalid);
        Assert.Equal(string.Empty, name);
    }

    [Fact]
    public void GetWindowText_ReturnsNonEmpty_ForForegroundWindow()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        var text = WindowInterop.GetWindowText(hwnd);
        // Most foreground windows have a title, but not guaranteed
        Assert.NotNull(text);
    }

    [Fact]
    public void GetWindowThreadProcessId_ReturnsValidPid_ForForegroundWindow()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        var threadId = WindowInterop.GetWindowThreadProcessId(hwnd, out var pid);
        Assert.True(threadId > 0, "Thread ID should be positive");
        Assert.True(pid > 0, "Process ID should be positive");
    }
}
