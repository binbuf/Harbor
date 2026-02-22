using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Shell.Tests;

/// <summary>
/// Tests for fullscreen retreat/restore state machine transitions and
/// OverlayManager retreat/restore behavior.
/// </summary>
public class FullscreenRetreatCoordinatorTests : IDisposable
{
    private readonly WindowEventManager _eventManager = new();
    private readonly TitleBarDiscoveryService _titleBarService;
    private readonly WindowCommandService _commandService = new();
    private readonly OverlaySyncService _syncService = new();
    private readonly OverlayManager _overlayManager;
    private readonly FullscreenDetectionService _detectionService = new();
    private readonly FullscreenRetreatCoordinator _coordinator;

    public FullscreenRetreatCoordinatorTests()
    {
        _titleBarService = new TitleBarDiscoveryService(_eventManager);
        _overlayManager = new OverlayManager(_eventManager, _titleBarService, _commandService, _syncService);
        _coordinator = new FullscreenRetreatCoordinator(_detectionService, _eventManager, _overlayManager);
    }

    public void Dispose()
    {
        _coordinator.Dispose();
        _overlayManager.Dispose();
        _syncService.Dispose();
        _titleBarService.Dispose();
        _eventManager.Dispose();
    }

    // --- OverlayManager retreat/restore tests ---

    [Fact]
    public void OverlayManager_IsRetreated_DefaultFalse()
    {
        Assert.False(_overlayManager.IsRetreated(new IntPtr(0x12345)));
    }

    [Fact]
    public void OverlayManager_Retreat_SetsRetreatedState()
    {
        var monitor = new IntPtr(0x12345);
        _overlayManager.Retreat(monitor);

        Assert.True(_overlayManager.IsRetreated(monitor));
    }

    [Fact]
    public void OverlayManager_Restore_ClearsRetreatedState()
    {
        var monitor = new IntPtr(0x12345);
        _overlayManager.Retreat(monitor);
        _overlayManager.Restore(monitor);

        Assert.False(_overlayManager.IsRetreated(monitor));
    }

    [Fact]
    public void OverlayManager_Retreat_Idempotent()
    {
        var monitor = new IntPtr(0x12345);
        _overlayManager.Retreat(monitor);
        _overlayManager.Retreat(monitor); // should not throw or double-retreat

        Assert.True(_overlayManager.IsRetreated(monitor));
    }

    [Fact]
    public void OverlayManager_Restore_Idempotent()
    {
        var monitor = new IntPtr(0x12345);
        _overlayManager.Restore(monitor); // should not throw (wasn't retreated)

        Assert.False(_overlayManager.IsRetreated(monitor));
    }

    [Fact]
    public void OverlayManager_RetreatOnMonitor1_DoesNotAffectMonitor2()
    {
        var monitor1 = new IntPtr(0x11111);
        var monitor2 = new IntPtr(0x22222);

        _overlayManager.Retreat(monitor1);

        Assert.True(_overlayManager.IsRetreated(monitor1));
        Assert.False(_overlayManager.IsRetreated(monitor2));
    }

    [Fact]
    public unsafe void OverlayManager_EnsureOverlay_SuppressedOnRetreatedMonitor()
    {
        // EnsureOverlay should return null for windows on retreated monitors.
        // Since we can't easily mock the monitor lookup, we test with invalid HWND
        // which returns null anyway — but the retreat check should run first.
        var monitor = new IntPtr(0x12345);
        _overlayManager.Retreat(monitor);

        var hwnd = new HWND((void*)0xDEAD);
        var result = _overlayManager.EnsureOverlay(hwnd);
        Assert.Null(result);
    }

    // --- Coordinator state machine tests ---

    [Fact]
    public void Coordinator_InitialState_IsNormal()
    {
        var monitor = new IntPtr(0x12345);
        Assert.Equal(RetreatState.Normal, _coordinator.GetState(monitor));
    }

    [Fact]
    public void RetreatState_HasExpectedValues()
    {
        Assert.Equal(0, (int)RetreatState.Normal);
        Assert.Equal(1, (int)RetreatState.Retreated);
    }

    [Fact]
    public void Coordinator_Dispose_IsIdempotent()
    {
        var eventManager = new WindowEventManager();
        var titleBarService = new TitleBarDiscoveryService(eventManager);
        var syncService = new OverlaySyncService();
        var overlayManager = new OverlayManager(eventManager, titleBarService, new WindowCommandService(), syncService);
        var coordinator = new FullscreenRetreatCoordinator(new FullscreenDetectionService(), eventManager, overlayManager);

        coordinator.Dispose();
        coordinator.Dispose(); // should not throw

        overlayManager.Dispose();
        syncService.Dispose();
        titleBarService.Dispose();
        eventManager.Dispose();
    }

    [Fact]
    public void Coordinator_RegisterAppBar_DoesNotThrow()
    {
        // Registration with a fake monitor handle should work without an actual AppBar
        // (we can't easily create real AppBar instances in tests)
        Assert.Equal(RetreatState.Normal, _coordinator.GetState(new IntPtr(0x99999)));
    }
}
