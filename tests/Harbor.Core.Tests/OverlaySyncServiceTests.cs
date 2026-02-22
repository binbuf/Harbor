using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Core.Tests;

public class OverlaySyncServiceTests : IDisposable
{
    private readonly OverlaySyncService _service = new();

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public void RectsEqual_SameValues_ReturnsTrue()
    {
        var a = new RECT { left = 10, top = 20, right = 100, bottom = 50 };
        var b = new RECT { left = 10, top = 20, right = 100, bottom = 50 };
        Assert.True(OverlaySyncService.RectsEqual(a, b));
    }

    [Fact]
    public void RectsEqual_DifferentLeft_ReturnsFalse()
    {
        var a = new RECT { left = 10, top = 20, right = 100, bottom = 50 };
        var b = new RECT { left = 11, top = 20, right = 100, bottom = 50 };
        Assert.False(OverlaySyncService.RectsEqual(a, b));
    }

    [Fact]
    public void RectsEqual_DifferentTop_ReturnsFalse()
    {
        var a = new RECT { left = 10, top = 20, right = 100, bottom = 50 };
        var b = new RECT { left = 10, top = 21, right = 100, bottom = 50 };
        Assert.False(OverlaySyncService.RectsEqual(a, b));
    }

    [Fact]
    public void RectsEqual_DifferentRight_ReturnsFalse()
    {
        var a = new RECT { left = 10, top = 20, right = 100, bottom = 50 };
        var b = new RECT { left = 10, top = 20, right = 101, bottom = 50 };
        Assert.False(OverlaySyncService.RectsEqual(a, b));
    }

    [Fact]
    public void RectsEqual_DifferentBottom_ReturnsFalse()
    {
        var a = new RECT { left = 10, top = 20, right = 100, bottom = 50 };
        var b = new RECT { left = 10, top = 20, right = 100, bottom = 51 };
        Assert.False(OverlaySyncService.RectsEqual(a, b));
    }

    [Fact]
    public void RectsEqual_ZeroRects_ReturnsTrue()
    {
        var a = new RECT();
        var b = new RECT();
        Assert.True(OverlaySyncService.RectsEqual(a, b));
    }

    [Fact]
    public void RectsEqual_NegativeCoordinates_WorksCorrectly()
    {
        var a = new RECT { left = -50, top = -30, right = 100, bottom = 200 };
        var b = new RECT { left = -50, top = -30, right = 100, bottom = 200 };
        Assert.True(OverlaySyncService.RectsEqual(a, b));

        var c = new RECT { left = -50, top = -31, right = 100, bottom = 200 };
        Assert.False(OverlaySyncService.RectsEqual(a, c));
    }

    [Fact]
    public void TitleBarOffset_Compute_StandardWindow()
    {
        // Standard window: title bar starts at window top-left corner
        var windowRect = new RECT { left = 100, top = 200, right = 900, bottom = 700 };
        var titleBarRect = new RECT { left = 100, top = 200, right = 900, bottom = 232 };

        var offset = TitleBarOffset.Compute(windowRect, titleBarRect);

        Assert.Equal(0, offset.LeftDelta);
        Assert.Equal(0, offset.TopDelta);
        Assert.Equal(0, offset.RightDelta);
        Assert.Equal(32, offset.Height);
    }

    [Fact]
    public void TitleBarOffset_Compute_PreservesDeltas()
    {
        // Simulates a window where title bar has non-zero deltas
        var windowRect = new RECT { left = 100, top = 200, right = 900, bottom = 700 };
        var titleBarRect = new RECT { left = 108, top = 200, right = 892, bottom = 232 };

        var offset = TitleBarOffset.Compute(windowRect, titleBarRect);

        Assert.Equal(8, offset.LeftDelta);
        Assert.Equal(0, offset.TopDelta);
        Assert.Equal(-8, offset.RightDelta);
        Assert.Equal(32, offset.Height);
    }

    [Fact]
    public void TitleBarOffset_Apply_ReconstructsOriginalRect()
    {
        var windowRect = new RECT { left = 100, top = 200, right = 900, bottom = 700 };
        var titleBarRect = new RECT { left = 100, top = 200, right = 900, bottom = 232 };

        var offset = TitleBarOffset.Compute(windowRect, titleBarRect);
        var result = offset.Apply(windowRect);

        Assert.Equal(titleBarRect.left, result.left);
        Assert.Equal(titleBarRect.top, result.top);
        Assert.Equal(titleBarRect.right, result.right);
        Assert.Equal(titleBarRect.bottom, result.bottom);
    }

    [Fact]
    public void TitleBarOffset_Apply_AfterWindowMove()
    {
        // Compute offset at original position
        var windowRect = new RECT { left = 100, top = 200, right = 900, bottom = 700 };
        var titleBarRect = new RECT { left = 100, top = 200, right = 900, bottom = 232 };
        var offset = TitleBarOffset.Compute(windowRect, titleBarRect);

        // Window moved 300px right, 150px down
        var movedRect = new RECT { left = 400, top = 350, right = 1200, bottom = 850 };
        var result = offset.Apply(movedRect);

        Assert.Equal(400, result.left);
        Assert.Equal(350, result.top);
        Assert.Equal(1200, result.right);
        Assert.Equal(382, result.bottom); // 350 + 32 = 382
    }

    [Fact]
    public void TitleBarOffset_Apply_AfterWindowResize()
    {
        // Compute offset at original position
        var windowRect = new RECT { left = 100, top = 200, right = 900, bottom = 700 };
        var titleBarRect = new RECT { left = 100, top = 200, right = 900, bottom = 232 };
        var offset = TitleBarOffset.Compute(windowRect, titleBarRect);

        // Window resized: 200px wider
        var resizedRect = new RECT { left = 100, top = 200, right = 1100, bottom = 700 };
        var result = offset.Apply(resizedRect);

        // Title bar should stretch with the window
        Assert.Equal(100, result.left);
        Assert.Equal(200, result.top);
        Assert.Equal(1100, result.right);
        Assert.Equal(232, result.bottom);
    }

    [Fact]
    public void TitleBarOffset_Apply_WithNonZeroDeltas_AfterMove()
    {
        var windowRect = new RECT { left = 100, top = 200, right = 900, bottom = 700 };
        var titleBarRect = new RECT { left = 108, top = 200, right = 892, bottom = 232 };
        var offset = TitleBarOffset.Compute(windowRect, titleBarRect);

        // Window moved
        var movedRect = new RECT { left = 500, top = 100, right = 1300, bottom = 600 };
        var result = offset.Apply(movedRect);

        Assert.Equal(508, result.left);   // 500 + 8
        Assert.Equal(100, result.top);    // 100 + 0
        Assert.Equal(1292, result.right); // 1300 + (-8)
        Assert.Equal(132, result.bottom); // 100 + 0 + 32
    }

    [Fact]
    public void TrackedCount_InitiallyZero()
    {
        Assert.Equal(0, _service.TrackedCount);
    }

    [Fact]
    public unsafe void IsTracking_ReturnsFalse_ForUntracked()
    {
        var hwnd = new HWND((void*)0x12345);
        Assert.False(_service.IsTracking(hwnd));
    }

    [Fact]
    public unsafe void Track_IncreasesCount()
    {
        var target = new HWND((void*)0x1111);
        var overlay = new HWND((void*)0x2222);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        _service.Track(target, overlay, windowRect, offset);

        Assert.Equal(1, _service.TrackedCount);
        Assert.True(_service.IsTracking(target));
    }

    [Fact]
    public unsafe void Untrack_DecreasesCount()
    {
        var target = new HWND((void*)0x1111);
        var overlay = new HWND((void*)0x2222);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        _service.Track(target, overlay, windowRect, offset);
        _service.Untrack(target);

        Assert.Equal(0, _service.TrackedCount);
        Assert.False(_service.IsTracking(target));
    }

    [Fact]
    public unsafe void Untrack_NoOp_ForUntracked()
    {
        var hwnd = new HWND((void*)0x12345);
        _service.Untrack(hwnd); // should not throw
    }

    [Fact]
    public unsafe void RepositionFromEvent_ReturnsNull_ForUntracked()
    {
        var hwnd = new HWND((void*)0x12345);
        var result = _service.RepositionFromEvent(hwnd);
        Assert.Null(result);
    }

    [Fact]
    public void GetStats_InitialValues()
    {
        var stats = _service.GetStats();

        Assert.Equal(0, stats.TotalUpdates);
        Assert.Equal(0, stats.PollingUpdates);
        Assert.Equal(0, stats.EventDrivenUpdates);
        Assert.Equal(0, stats.FrameMisses);
        Assert.Equal(0, stats.FrameMissRate);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new OverlaySyncService();
        service.Dispose();
        service.Dispose(); // should not throw
    }

    [Fact]
    public unsafe void Track_MultipleWindows()
    {
        var target1 = new HWND((void*)0x1111);
        var overlay1 = new HWND((void*)0x2222);
        var target2 = new HWND((void*)0x3333);
        var overlay2 = new HWND((void*)0x4444);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        _service.Track(target1, overlay1, windowRect, offset);
        _service.Track(target2, overlay2, windowRect, offset);

        Assert.Equal(2, _service.TrackedCount);
        Assert.True(_service.IsTracking(target1));
        Assert.True(_service.IsTracking(target2));
    }

    [Fact]
    public unsafe void Track_SameTarget_UpdatesExisting()
    {
        var target = new HWND((void*)0x1111);
        var overlay1 = new HWND((void*)0x2222);
        var overlay2 = new HWND((void*)0x3333);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        _service.Track(target, overlay1, windowRect, offset);
        _service.Track(target, overlay2, windowRect, offset);

        // Should still be 1 tracked (updated in place)
        Assert.Equal(1, _service.TrackedCount);
    }

    [Fact]
    public unsafe void UpdateOffset_WorksForTrackedWindow()
    {
        var target = new HWND((void*)0x1111);
        var overlay = new HWND((void*)0x2222);
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var offset = new TitleBarOffset(0, 0, 0, 32);

        _service.Track(target, overlay, windowRect, offset);

        var newRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var newOffset = new TitleBarOffset(0, 0, 0, 40);
        _service.UpdateOffset(target, newRect, newOffset);

        // Should not throw and still be tracked
        Assert.True(_service.IsTracking(target));
    }

    [Fact]
    public unsafe void UpdateOffset_NoOp_ForUntracked()
    {
        var hwnd = new HWND((void*)0x12345);
        var rect = new RECT();
        var offset = new TitleBarOffset(0, 0, 0, 32);

        _service.UpdateOffset(hwnd, rect, offset); // should not throw
    }
}
