using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Shell.Tests;

public class OverlayManagerTests : IDisposable
{
    private readonly WindowEventManager _eventManager = new();
    private readonly TitleBarDiscoveryService _titleBarService;
    private readonly WindowCommandService _commandService = new();
    private readonly OverlayManager _manager;

    public OverlayManagerTests()
    {
        _titleBarService = new TitleBarDiscoveryService(_eventManager);
        _manager = new OverlayManager(_eventManager, _titleBarService, _commandService);
    }

    public void Dispose()
    {
        _manager.Dispose();
        _titleBarService.Dispose();
        _eventManager.Dispose();
    }

    [Fact]
    public void InitialOverlayCount_IsZero()
    {
        Assert.Equal(0, _manager.OverlayCount);
    }

    [Fact]
    public unsafe void HasOverlay_ReturnsFalse_ForUnknownHwnd()
    {
        var hwnd = new HWND((void*)0x12345);
        Assert.False(_manager.HasOverlay(hwnd));
    }

    [Fact]
    public unsafe void GetOverlay_ReturnsNull_ForUnknownHwnd()
    {
        var hwnd = new HWND((void*)0x12345);
        Assert.Null(_manager.GetOverlay(hwnd));
    }

    [Fact]
    public unsafe void DestroyOverlay_DoesNotThrow_ForUnknownHwnd()
    {
        var hwnd = new HWND((void*)0x12345);
        _manager.DestroyOverlay(hwnd); // should not throw
    }

    [Fact]
    public void EnsureOverlay_NullHandle_ReturnsNull()
    {
        var result = _manager.EnsureOverlay(HWND.Null);
        Assert.Null(result);
    }

    [Fact]
    public unsafe void EnsureOverlay_InvalidHandle_ReturnsNull()
    {
        // An invalid HWND should fail title bar discovery and return null
        var hwnd = new HWND((void*)0xDEADBEEF);
        var result = _manager.EnsureOverlay(hwnd);
        Assert.Null(result);
        Assert.Equal(0, _manager.OverlayCount);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var eventManager = new WindowEventManager();
        var titleBarService = new TitleBarDiscoveryService(eventManager);
        var manager = new OverlayManager(eventManager, titleBarService, new WindowCommandService());

        manager.Dispose();
        manager.Dispose(); // should not throw

        titleBarService.Dispose();
        eventManager.Dispose();
    }

    [Fact]
    public void Dispose_ClearsOverlays()
    {
        // After dispose, overlay count should be zero
        var eventManager = new WindowEventManager();
        var titleBarService = new TitleBarDiscoveryService(eventManager);
        var manager = new OverlayManager(eventManager, titleBarService, new WindowCommandService());

        manager.Dispose();
        Assert.Equal(0, manager.OverlayCount);

        titleBarService.Dispose();
        eventManager.Dispose();
    }

    [Fact]
    public unsafe void EnsureOverlay_SkippedWindow_ReturnsNull()
    {
        // Force a window into the skip list via discovery of an invalid handle
        var hwnd = new HWND((void*)0xDEAD);
        _titleBarService.Discover(hwnd);

        if (_titleBarService.IsSkipped(hwnd))
        {
            var result = _manager.EnsureOverlay(hwnd);
            Assert.Null(result);
        }
    }
}
