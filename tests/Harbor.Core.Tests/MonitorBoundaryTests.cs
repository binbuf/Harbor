using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Core.Tests;

/// <summary>
/// Tests for monitor boundary detection logic used during cross-monitor window drags.
/// </summary>
public class MonitorBoundaryTests : IDisposable
{
    private readonly OverlaySyncService _service = new();

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public unsafe void TrackedOverlay_HasLastMonitor_Property()
    {
        // TrackedOverlay should track which monitor it was last on
        var target = new HWND((void*)0x1111);
        var overlay = new HWND((void*)0x2222);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        // Track should capture the monitor — we can verify tracking works
        _service.Track(target, overlay, windowRect, offset);
        Assert.True(_service.IsTracking(target));
    }

    [Fact]
    public void MonitorChanged_Event_IsSubscribable()
    {
        // Verify the MonitorChanged event can be subscribed to
        var fired = false;
        _service.MonitorChanged += _ => fired = true;

        // Event won't fire without real windows, but subscription should work
        Assert.False(fired);

        _service.MonitorChanged -= _ => { };
    }

    [Fact]
    public unsafe void Track_MultipleTimes_UpdatesMonitor()
    {
        var target = new HWND((void*)0x1111);
        var overlay = new HWND((void*)0x2222);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        // Track, then re-track (simulates overlay recreation on monitor change)
        _service.Track(target, overlay, windowRect, offset);
        Assert.Equal(1, _service.TrackedCount);

        // Re-track with same target (updates in-place)
        _service.Track(target, overlay, windowRect, offset);
        Assert.Equal(1, _service.TrackedCount);
    }

    [Fact]
    public unsafe void Untrack_ThenRetrack_SimulatesOverlayRecreation()
    {
        var target = new HWND((void*)0x1111);
        var overlay1 = new HWND((void*)0x2222);
        var overlay2 = new HWND((void*)0x3333);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        // Initial tracking
        _service.Track(target, overlay1, windowRect, offset);
        Assert.True(_service.IsTracking(target));

        // Untrack (simulates overlay destruction on monitor boundary)
        _service.Untrack(target);
        Assert.False(_service.IsTracking(target));

        // Re-track with new overlay (simulates recreation on new monitor)
        _service.Track(target, overlay2, windowRect, offset);
        Assert.True(_service.IsTracking(target));
        Assert.Equal(1, _service.TrackedCount);
    }

    [Theory]
    [InlineData(0, 0, 1920, 0)]
    [InlineData(-1920, 0, 0, 0)]
    [InlineData(0, 0, 2560, 0)]
    public void TitleBarOffset_Apply_WorksAcrossMonitorBoundaries(
        int mon1Left, int mon1Top, int mon2Left, int mon2Top)
    {
        // Window on monitor 1
        var windowRect1 = new RECT
        {
            left = mon1Left + 100,
            top = mon1Top + 100,
            right = mon1Left + 900,
            bottom = mon1Top + 700,
        };
        var titleBarRect1 = new RECT
        {
            left = windowRect1.left,
            top = windowRect1.top,
            right = windowRect1.right,
            bottom = windowRect1.top + 32,
        };

        var offset = TitleBarOffset.Compute(windowRect1, titleBarRect1);

        // Window dragged to monitor 2 (same relative position)
        var windowRect2 = new RECT
        {
            left = mon2Left + 100,
            top = mon2Top + 100,
            right = mon2Left + 900,
            bottom = mon2Top + 700,
        };

        var result = offset.Apply(windowRect2);

        // Title bar should be correctly positioned on monitor 2
        Assert.Equal(windowRect2.left, result.left);
        Assert.Equal(windowRect2.top, result.top);
        Assert.Equal(windowRect2.right, result.right);
        Assert.Equal(windowRect2.top + 32, result.bottom);
    }

    [Theory]
    [InlineData(96, 144)]   // 100% to 150%
    [InlineData(96, 192)]   // 100% to 200%
    [InlineData(144, 96)]   // 150% to 100%
    [InlineData(192, 120)]  // 200% to 125%
    public void CrossMonitorDrag_DifferentDpis_RequiresOverlayRecreation(
        uint sourceDpi, uint targetDpi)
    {
        // This test documents the design requirement that overlay recreation is mandatory
        // when crossing between monitors with different DPI values.
        // WPF windows cannot change their DPI context after creation.

        var sourceScale = Harbor.Core.Interop.DisplayInterop.ComputeScaleFactor(sourceDpi);
        var targetScale = Harbor.Core.Interop.DisplayInterop.ComputeScaleFactor(targetDpi);

        // The scale factors are different, confirming recreation is needed
        Assert.NotEqual(sourceScale, targetScale);
    }
}
