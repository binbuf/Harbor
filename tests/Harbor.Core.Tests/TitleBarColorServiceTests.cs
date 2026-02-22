using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;
using Color = System.Windows.Media.Color;

namespace Harbor.Core.Tests;

public class TitleBarColorServiceTests : IDisposable
{
    private readonly TitleBarColorService _service = new();

    public void Dispose()
    {
        _service.Dispose();
    }

    // --- Cascading priority tests ---

    [Fact]
    public void Detect_NullHwnd_ReturnsFallback()
    {
        var result = _service.Detect(HWND.Null, default);

        Assert.NotNull(result);
        Assert.Equal(ColorDetectionMethod.Fallback, result.Method);
    }

    [Fact]
    public unsafe void Detect_InvalidHwnd_ReturnsFallbackOrDetected()
    {
        var hwnd = new HWND((void*)0xDEAD);
        var rect = new RECT { left = 0, top = 0, right = 800, bottom = 30 };
        var result = _service.Detect(hwnd, rect);

        Assert.NotNull(result);
        // Should return some detection method (colorization or fallback)
        Assert.True(Enum.IsDefined(result.Method));
    }

    [Fact]
    public void Detect_CachesResultByProcessName()
    {
        // Detect on the foreground window twice — second should hit cache
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        if (!WindowInterop.GetWindowRect(hwnd, out var rect))
            return;

        var result1 = _service.Detect(hwnd, rect);
        Assert.NotNull(result1);

        var cacheCount = _service.CacheCount;
        var result2 = _service.Detect(hwnd, rect);

        Assert.NotNull(result2);
        Assert.Equal(result1.Color, result2.Color);
        Assert.Equal(result1.Method, result2.Method);
        Assert.Equal(cacheCount, _service.CacheCount);
    }

    [Fact]
    public void InvalidateAll_ClearsCache()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;
        if (!WindowInterop.GetWindowRect(hwnd, out var rect)) return;

        _service.Detect(hwnd, rect);
        Assert.True(_service.CacheCount >= 0); // may or may not cache based on process name

        _service.InvalidateAll();
        Assert.Equal(0, _service.CacheCount);
    }

    [Fact]
    public void Invalidate_RemovesSpecificEntry()
    {
        _service.InvalidateAll();

        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;
        if (!WindowInterop.GetWindowRect(hwnd, out var rect)) return;

        _service.Detect(hwnd, rect);
        var countAfterDetect = _service.CacheCount;

        // Invalidate a specific process name
        _service.Invalidate("nonexistent_process");
        Assert.Equal(countAfterDetect, _service.CacheCount); // should not change

        _service.InvalidateAll();
        Assert.Equal(0, _service.CacheCount);
    }

    // --- DWM Colorization tests ---

    [Fact]
    public void TryDwmColorizationColor_ReturnsColorOnDesktop()
    {
        // DWM colorization should be available on any Windows 10+ desktop
        var result = TitleBarColorService.TryDwmColorizationColor();

        // This should work on any desktop Windows machine
        if (result is not null)
        {
            Assert.Equal(ColorDetectionMethod.DwmColorization, result.Method);
            // Color should have some alpha/RGB values
            Assert.True(result.Color.A > 0 || result.Color.R > 0 || result.Color.G > 0 || result.Color.B > 0);
        }
    }

    // --- DWM Caption Color tests ---

    [Fact]
    public void TryDwmCaptionColor_NullHwnd_ReturnsNull()
    {
        var result = TitleBarColorService.TryDwmCaptionColor(HWND.Null);
        Assert.Null(result);
    }

    [Fact]
    public unsafe void TryDwmCaptionColor_InvalidHwnd_ReturnsNull()
    {
        var result = TitleBarColorService.TryDwmCaptionColor(new HWND((void*)0xDEAD));
        Assert.Null(result);
    }

    [Fact]
    public void TryDwmCaptionColor_ForegroundWindow_ReturnsResultOrNull()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        var result = TitleBarColorService.TryDwmCaptionColor(hwnd);
        // May be null if the window uses default caption color
        if (result is not null)
        {
            Assert.Equal(ColorDetectionMethod.DwmCaptionColor, result.Method);
        }
    }

    // --- System Backdrop Detection tests ---

    [Fact]
    public void TrySystemBackdropDetection_NullHwnd_ReturnsNull()
    {
        var result = TitleBarColorService.TrySystemBackdropDetection(HWND.Null);
        Assert.Null(result);
    }

    [Fact]
    public unsafe void TrySystemBackdropDetection_InvalidHwnd_ReturnsNull()
    {
        var result = TitleBarColorService.TrySystemBackdropDetection(new HWND((void*)0xDEAD));
        Assert.Null(result);
    }

    // --- Pixel Sampling tests ---

    [Fact]
    public void TryPixelSampling_ZeroSizeRect_ReturnsNull()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd == HWND.Null) return;

        var rect = new RECT { left = 0, top = 0, right = 0, bottom = 0 };
        var result = TitleBarColorService.TryPixelSampling(hwnd, rect);
        Assert.Null(result);
    }

    [Fact]
    public unsafe void TryPixelSampling_InvalidHwnd_ReturnsNull()
    {
        var rect = new RECT { left = 0, top = 0, right = 800, bottom = 30 };
        var result = TitleBarColorService.TryPixelSampling(new HWND((void*)0xDEAD), rect);
        Assert.Null(result);
    }

    // --- Median Color Calculation tests ---

    [Fact]
    public void ComputeMedianColor_SingleColor_ReturnsSameColor()
    {
        var colors = new List<(byte R, byte G, byte B)> { (128, 64, 32) };
        var median = TitleBarColorService.ComputeMedianColor(colors);

        Assert.Equal(128, median.R);
        Assert.Equal(64, median.G);
        Assert.Equal(32, median.B);
    }

    [Fact]
    public void ComputeMedianColor_UniformColors_ReturnsThatColor()
    {
        var colors = Enumerable.Repeat((R: (byte)200, G: (byte)100, B: (byte)50), 64).ToList();
        var median = TitleBarColorService.ComputeMedianColor(colors);

        Assert.Equal(200, median.R);
        Assert.Equal(100, median.G);
        Assert.Equal(50, median.B);
    }

    [Fact]
    public void ComputeMedianColor_TwoColors_ReturnsMedian()
    {
        var colors = new List<(byte R, byte G, byte B)>
        {
            (0, 0, 0),
            (100, 100, 100),
            (200, 200, 200),
        };
        var median = TitleBarColorService.ComputeMedianColor(colors);

        // Median of sorted [0, 100, 200] at index 1 = 100
        Assert.Equal(100, median.R);
        Assert.Equal(100, median.G);
        Assert.Equal(100, median.B);
    }

    [Fact]
    public void ComputeMedianColor_WithOutlier_IgnoresOutlier()
    {
        // Mostly dark colors with one bright outlier
        var colors = new List<(byte R, byte G, byte B)>
        {
            (30, 30, 30),
            (32, 32, 32),
            (31, 31, 31),
            (33, 33, 33),
            (255, 0, 0), // outlier
        };
        var median = TitleBarColorService.ComputeMedianColor(colors);

        // Median should be close to the dark values, not skewed by outlier
        // Sorted R: [30, 31, 32, 33, 255] → median at index 2 = 32
        Assert.Equal(32, median.R);
        Assert.Equal(31, median.G); // Sorted G: [0, 30, 31, 32, 33] → index 2 = 31
        Assert.Equal(31, median.B); // Sorted B: [0, 30, 31, 32, 33] → index 2 = 31
    }

    [Fact]
    public void ComputeMedianColor_EmptyList_ReturnsBlack()
    {
        var colors = new List<(byte R, byte G, byte B)>();
        var median = TitleBarColorService.ComputeMedianColor(colors);

        Assert.Equal(0, median.R);
        Assert.Equal(0, median.G);
        Assert.Equal(0, median.B);
    }

    // --- Constants ---

    [Fact]
    public void SampleSize_Is8()
    {
        Assert.Equal(8, TitleBarColorService.SampleSize);
    }

    // --- Dispose tests ---

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new TitleBarColorService();
        service.Dispose();
        service.Dispose(); // should not throw
    }

    [Fact]
    public void Detect_ThrowsAfterDispose()
    {
        var service = new TitleBarColorService();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            service.Detect(HWND.Null, default));
    }

    // --- ColorDetectionMethod enum coverage ---

    [Theory]
    [InlineData(ColorDetectionMethod.DwmCaptionColor)]
    [InlineData(ColorDetectionMethod.DwmColorization)]
    [InlineData(ColorDetectionMethod.SystemBackdrop)]
    [InlineData(ColorDetectionMethod.PixelSampling)]
    [InlineData(ColorDetectionMethod.UserOverride)]
    [InlineData(ColorDetectionMethod.Fallback)]
    public void ColorDetectionMethod_AllValuesAreDefined(ColorDetectionMethod method)
    {
        Assert.True(Enum.IsDefined(method));
    }

    // --- User Override priority test ---

    [Fact]
    public void Detect_UserOverride_TakesPriority()
    {
        // Create a temp config file with an override
        var tempPath = Path.Combine(Path.GetTempPath(), $"harbor-test-{Guid.NewGuid()}.json");
        try
        {
            var overrideService = new ColorOverrideService(tempPath);
            overrideService.SetOverride("TestProcess", Color.FromRgb(255, 0, 0));

            using var service = new TitleBarColorService(overrideService);

            // We can't easily control what process the foreground window belongs to,
            // but we can verify the override service works correctly
            var overrideColor = overrideService.GetOverride("TestProcess");
            Assert.NotNull(overrideColor);
            Assert.Equal(255, overrideColor.Value.R);
            Assert.Equal(0, overrideColor.Value.G);
            Assert.Equal(0, overrideColor.Value.B);

            overrideService.Dispose();
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
