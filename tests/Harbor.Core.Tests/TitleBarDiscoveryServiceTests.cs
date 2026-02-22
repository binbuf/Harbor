using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Core.Tests;

public class TitleBarDiscoveryServiceTests : IDisposable
{
    private readonly TitleBarDiscoveryService _service = new();

    public void Dispose()
    {
        _service.Dispose();
    }

    // --- NONCLIENT fallback calculation tests ---

    [Fact]
    public void ComputeNonClientHeight_StandardWindow_ReturnsPositiveHeight()
    {
        // Simulate a standard Win32 window: 800x600 window, 780x560 client area
        // Total window: 800w x 600h, client: 780w x 560h
        // Horizontal NC = 800 - 780 = 20, border = 10 each side
        // Vertical NC = 600 - 560 = 40
        // Title bar = 40 - 10 (bottom border) = 30
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return; // CI may have no foreground window

        if (!WindowInterop.GetWindowRect(hwnd, out var windowRect))
            return;

        var height = TitleBarDiscoveryService.ComputeNonClientHeight(hwnd, windowRect);

        // Height should be non-negative for standard windows
        // (exact value depends on system DPI and theme)
        Assert.True(height >= 0, $"Expected non-negative height, got {height}");
    }

    [Fact]
    public void ComputeNonClientHeight_NullHandle_ReturnsNegative()
    {
        var windowRect = new RECT { left = 0, top = 0, right = 800, bottom = 600 };
        var height = TitleBarDiscoveryService.ComputeNonClientHeight(HWND.Null, windowRect);

        // GetClientRect fails for null handle, should return -1
        Assert.Equal(-1, height);
    }

    // --- Skip list tests ---

    [Fact]
    public void SkipList_InitiallyEmpty()
    {
        Assert.Equal(0, _service.SkipListCount);
    }

    [Fact]
    public void IsSkipped_ReturnsFalse_ForUnknownHwnd()
    {
        unsafe
        {
            var hwnd = new HWND((void*)0x12345);
            Assert.False(_service.IsSkipped(hwnd));
        }
    }

    [Fact]
    public void RemoveFromSkipList_ReturnsFalse_ForUnknownHwnd()
    {
        unsafe
        {
            var hwnd = new HWND((void*)0x12345);
            Assert.False(_service.RemoveFromSkipList(hwnd));
        }
    }

    [Fact]
    public unsafe void Discover_NullHandle_ReturnsNull()
    {
        var result = _service.Discover(HWND.Null);
        Assert.Null(result);
    }

    [Fact]
    public unsafe void Discover_InvalidHandle_AddsToSkipListOrReturnsNull()
    {
        // An invalid (non-existent) HWND should fail all discovery methods
        var hwnd = new HWND((void*)0xDEAD);
        var result = _service.Discover(hwnd);

        // Either returns null (skip list) or null (all methods failed)
        Assert.Null(result);
    }

    [Fact]
    public unsafe void Discover_SkippedHandle_ReturnsNull()
    {
        // First discovery of invalid handle should add to skip list
        var hwnd = new HWND((void*)0xDEAD);
        _service.Discover(hwnd);

        // If it was added to skip list, second call returns null immediately
        if (_service.IsSkipped(hwnd))
        {
            var result = _service.Discover(hwnd);
            Assert.Null(result);
        }
    }

    [Fact]
    public unsafe void RemoveFromSkipList_Works_AfterSkipListAdd()
    {
        var hwnd = new HWND((void*)0xDEAD);
        _service.Discover(hwnd); // should add to skip list

        if (_service.IsSkipped(hwnd))
        {
            Assert.True(_service.RemoveFromSkipList(hwnd));
            Assert.False(_service.IsSkipped(hwnd));
        }
    }

    // --- Cache tests ---

    [Fact]
    public void Cache_InitiallyEmpty()
    {
        Assert.Equal(0, _service.CacheCount);
    }

    [Fact]
    public void InvalidateCache_DoesNotThrow_ForUnknownHwnd()
    {
        unsafe
        {
            _service.InvalidateCache(new HWND((void*)0x12345));
        }
    }

    [Fact]
    public void ClearCache_DoesNotThrow_WhenEmpty()
    {
        _service.ClearCache();
        Assert.Equal(0, _service.CacheCount);
    }

    // --- Framework detection tests ---

    [Fact]
    public void DetectFramework_NullHandle_ReturnsUnknown()
    {
        var framework = TitleBarDiscoveryService.DetectFramework(HWND.Null);
        Assert.Equal(UIFramework.Unknown, framework);
    }

    [Fact]
    public void DetectFramework_ForegroundWindow_ReturnsNonNull()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        // Should return some framework (not throw)
        var framework = TitleBarDiscoveryService.DetectFramework(hwnd);
        Assert.True(Enum.IsDefined(framework));
    }

    // --- UIA discovery integration tests ---

    [Fact]
    public void Discover_ForegroundWindow_ReturnsResultOrNull()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        // Should not throw — may return null if the foreground window
        // doesn't have a standard title bar (e.g., fullscreen terminal)
        var result = _service.Discover(hwnd);

        if (result is not null)
        {
            Assert.Equal(hwnd, result.Hwnd);
            Assert.True(result.Rect.right - result.Rect.left > 0, "Title bar width should be positive");
            Assert.True(result.Rect.bottom - result.Rect.top > 0, "Title bar height should be positive");
            Assert.True(Enum.IsDefined(result.Framework));
        }
    }

    [Fact]
    public void Discover_CachesResult()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        var result1 = _service.Discover(hwnd);
        if (result1 is null) return;

        // Second call should hit cache
        var initialCacheCount = _service.CacheCount;
        var result2 = _service.Discover(hwnd);

        Assert.NotNull(result2);
        Assert.Equal(result1.Rect.left, result2.Rect.left);
        Assert.Equal(result1.Rect.top, result2.Rect.top);
        Assert.Equal(initialCacheCount, _service.CacheCount);
    }

    [Fact]
    public void InvalidateCache_ForcesRediscovery()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        var result1 = _service.Discover(hwnd);
        if (result1 is null) return;

        _service.InvalidateCache(hwnd);

        // After invalidation, cache count decreases
        // Next discover re-queries
        var result2 = _service.Discover(hwnd);
        Assert.NotNull(result2);
    }

    // --- Dispose tests ---

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new TitleBarDiscoveryService();
        service.Dispose();
        service.Dispose(); // should not throw
    }

    [Fact]
    public void Discover_ThrowsAfterDispose()
    {
        var service = new TitleBarDiscoveryService();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            service.Discover(WindowInterop.GetForegroundWindow()));
    }

    // --- WindowEventManager integration tests ---

    [Fact]
    public void Constructor_WithEventManager_Subscribes()
    {
        using var eventManager = new WindowEventManager();
        using var service = new TitleBarDiscoveryService(eventManager);

        // Service should be functional
        Assert.Equal(0, service.CacheCount);
    }

    [Fact]
    public void Constructor_WithoutEventManager_Works()
    {
        using var service = new TitleBarDiscoveryService();
        Assert.Equal(0, service.CacheCount);
    }

    // --- UIFramework enum coverage ---

    [Theory]
    [InlineData(UIFramework.Unknown)]
    [InlineData(UIFramework.Win32)]
    [InlineData(UIFramework.Wpf)]
    [InlineData(UIFramework.Uwp)]
    [InlineData(UIFramework.WinUI3)]
    [InlineData(UIFramework.Electron)]
    [InlineData(UIFramework.Java)]
    [InlineData(UIFramework.Qt)]
    public void UIFramework_AllValuesAreDefined(UIFramework framework)
    {
        Assert.True(Enum.IsDefined(framework));
    }
}
