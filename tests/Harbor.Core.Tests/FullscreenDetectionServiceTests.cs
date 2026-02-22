using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Core.Tests;

/// <summary>
/// Tests for fullscreen classification logic: window rect vs. display bounds,
/// NONCLIENT area checks, and DXGI exclusive mode detection.
/// </summary>
public class FullscreenDetectionServiceTests
{
    // --- CoversMonitor tests ---

    [Fact]
    public void CoversMonitor_ExactMatch_ReturnsTrue()
    {
        var window = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
        var monitor = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };

        Assert.True(FullscreenDetectionService.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_WindowLargerThanMonitor_ReturnsTrue()
    {
        // Some fullscreen apps render slightly larger than the monitor (negative margins)
        var window = new RECT { left = -8, top = -8, right = 1928, bottom = 1088 };
        var monitor = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };

        Assert.True(FullscreenDetectionService.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_WindowSmallerThanMonitor_ReturnsFalse()
    {
        var window = new RECT { left = 100, top = 100, right = 1820, bottom = 980 };
        var monitor = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };

        Assert.False(FullscreenDetectionService.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_MaximizedWithTaskbar_ReturnsFalse()
    {
        // Maximized window doesn't cover taskbar area
        var window = new RECT { left = 0, top = 0, right = 1920, bottom = 1040 };
        var monitor = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };

        Assert.False(FullscreenDetectionService.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_SecondMonitor_ExactMatch_ReturnsTrue()
    {
        // Second monitor offset to the right
        var window = new RECT { left = 1920, top = 0, right = 3840, bottom = 1080 };
        var monitor = new RECT { left = 1920, top = 0, right = 3840, bottom = 1080 };

        Assert.True(FullscreenDetectionService.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_WindowOnWrongMonitor_ReturnsFalse()
    {
        // Window on primary monitor, checking against secondary
        var window = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
        var monitor = new RECT { left = 1920, top = 0, right = 3840, bottom = 1080 };

        Assert.False(FullscreenDetectionService.CoversMonitor(window, monitor));
    }

    [Fact]
    public void CoversMonitor_4kMonitor_ExactMatch_ReturnsTrue()
    {
        var window = new RECT { left = 0, top = 0, right = 3840, bottom = 2160 };
        var monitor = new RECT { left = 0, top = 0, right = 3840, bottom = 2160 };

        Assert.True(FullscreenDetectionService.CoversMonitor(window, monitor));
    }

    // --- HasNoNonClientArea tests (pure logic, requires live HWND for full test) ---

    [Fact]
    public void HasNoNonClientArea_NullHwnd_ReturnsFalse()
    {
        Assert.False(FullscreenDetectionService.HasNoNonClientArea(HWND.Null));
    }

    [Fact]
    public void HasNoNonClientArea_InvalidHwnd_ReturnsFalse()
    {
        // Invalid handle — GetWindowRect/GetClientRect will fail
        Assert.False(FullscreenDetectionService.HasNoNonClientArea(new HWND(0x7FFFFFFF)));
    }

    // --- IsExclusiveTopmost tests (requires live HWND for style checks) ---

    [Fact]
    public void IsExclusiveTopmost_NullHwnd_ReturnsFalse()
    {
        var monitor = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
        Assert.False(FullscreenDetectionService.IsExclusiveTopmost(HWND.Null, monitor));
    }

    [Fact]
    public void IsExclusiveTopmost_InvalidHwnd_ReturnsFalse()
    {
        var monitor = new RECT { left = 0, top = 0, right = 1920, bottom = 1080 };
        Assert.False(FullscreenDetectionService.IsExclusiveTopmost(new HWND(0x7FFFFFFF), monitor));
    }

    // --- Classify integration tests ---

    [Fact]
    public void Classify_NullHwnd_ReturnsNotFullscreen()
    {
        var service = new FullscreenDetectionService();
        var result = service.Classify(HWND.Null);

        Assert.False(result.IsFullscreen);
        Assert.Equal(FullscreenKind.None, result.Kind);
    }

    [Fact]
    public void Classify_InvalidHwnd_ReturnsNotFullscreen()
    {
        var service = new FullscreenDetectionService();
        var result = service.Classify(new HWND(0x7FFFFFFF));

        Assert.False(result.IsFullscreen);
        Assert.Equal(FullscreenKind.None, result.Kind);
    }

    // --- FullscreenInfo record tests ---

    [Fact]
    public void FullscreenInfo_NotFullscreen_HasCorrectDefaults()
    {
        var info = new FullscreenInfo(false, IntPtr.Zero, FullscreenKind.None);

        Assert.False(info.IsFullscreen);
        Assert.Equal(IntPtr.Zero, info.MonitorHandle);
        Assert.Equal(FullscreenKind.None, info.Kind);
    }

    [Fact]
    public void FullscreenInfo_Fullscreen_PreservesValues()
    {
        var monitor = new IntPtr(0x12345);
        var info = new FullscreenInfo(true, monitor, FullscreenKind.BorderlessFullscreen);

        Assert.True(info.IsFullscreen);
        Assert.Equal(monitor, info.MonitorHandle);
        Assert.Equal(FullscreenKind.BorderlessFullscreen, info.Kind);
    }

    [Fact]
    public void FullscreenKind_HasExpectedValues()
    {
        Assert.Equal(0, (int)FullscreenKind.None);
        Assert.Equal(1, (int)FullscreenKind.BorderlessFullscreen);
        Assert.Equal(2, (int)FullscreenKind.ExclusiveTopmost);
    }
}
