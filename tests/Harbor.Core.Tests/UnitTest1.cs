using System.Runtime.InteropServices;
using Harbor.Core.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Harbor.Core.Tests;

public class StructLayoutTests
{
    [Fact]
    public void RECT_SizeMatchesNative()
    {
        // RECT is 4 ints = 16 bytes
        Assert.Equal(16, Marshal.SizeOf<RECT>());
    }

    [Fact]
    public void DWM_THUMBNAIL_PROPERTIES_HasExpectedMinimumSize()
    {
        int size = Marshal.SizeOf<DWM_THUMBNAIL_PROPERTIES>();
        Assert.True(size >= 45, $"DWM_THUMBNAIL_PROPERTIES size {size} is smaller than expected minimum of 45 bytes");
    }

    [Fact]
    public void AccentPolicy_SizeIs16Bytes()
    {
        // AccentState(4) + AccentFlags(4) + GradientColor(4) + AnimationId(4) = 16
        Assert.Equal(16, Marshal.SizeOf<CompositionInterop.AccentPolicy>());
    }

    [Fact]
    public void WindowCompositionAttributeData_HasExpectedSize()
    {
        int size = Marshal.SizeOf<CompositionInterop.WindowCompositionAttributeData>();
        // Attribute(4) + padding(4 on x64) + Data(8 on x64) + SizeOfData(4) + padding(4)
        Assert.True(size >= 16, $"WindowCompositionAttributeData size {size} is smaller than expected");
    }
}

public class ConstantsTests
{
    [Fact]
    public void WindowInterop_SystemCommandConstants()
    {
        Assert.Equal(0xF060u, WindowInterop.SC_CLOSE);
        Assert.Equal(0xF020u, WindowInterop.SC_MINIMIZE);
        Assert.Equal(0xF030u, WindowInterop.SC_MAXIMIZE);
        Assert.Equal(0xF120u, WindowInterop.SC_RESTORE);
        Assert.Equal(0x0112u, WindowInterop.WM_SYSCOMMAND);
    }

    [Fact]
    public void WindowInterop_StyleConstants()
    {
        Assert.Equal(-20, WindowInterop.GWL_EXSTYLE);
        Assert.Equal(0x00080000u, WindowInterop.WS_EX_LAYERED);
        Assert.Equal(0x08000000u, WindowInterop.WS_EX_NOACTIVATE);
        Assert.Equal(0x00000008u, WindowInterop.WS_EX_TOPMOST);
    }

    [Fact]
    public void EventHookInterop_EventConstants()
    {
        Assert.Equal(0x0003u, EventHookInterop.EVENT_SYSTEM_FOREGROUND);
        Assert.Equal(0x800Bu, EventHookInterop.EVENT_OBJECT_LOCATIONCHANGE);
        Assert.Equal(0x8000u, EventHookInterop.EVENT_OBJECT_CREATE);
        Assert.Equal(0x8001u, EventHookInterop.EVENT_OBJECT_DESTROY);
    }

    [Fact]
    public void SystemInterop_Constants()
    {
        Assert.Equal(4, SystemInterop.SM_CYCAPTION);
        Assert.Equal(0x1043u, SystemInterop.SPI_SETCLIENTAREAANIMATION);
        Assert.Equal(0x1042u, SystemInterop.SPI_GETCLIENTAREAANIMATION);
    }

    [Fact]
    public void DwmInterop_Constants()
    {
        Assert.Equal(5u, DwmInterop.DWMWA_CAPTION_BUTTON_BOUNDS);
        Assert.Equal(38u, DwmInterop.DWMWA_SYSTEMBACKDROP_TYPE);
        Assert.Equal(33u, DwmInterop.DWMWA_WINDOW_CORNER_PREFERENCE);
    }

    [Fact]
    public void DisplayInterop_BaseDpi()
    {
        Assert.Equal(96u, DisplayInterop.BASE_DPI);
    }

    [Fact]
    public void CompositionInterop_Constants()
    {
        Assert.Equal(19, CompositionInterop.WCA_ACCENT_POLICY);
    }
}

public class IntegrationTests
{
    [Fact]
    public void GetForegroundWindow_ReturnsNonZero()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        Assert.NotEqual(HWND.Null, hwnd);
    }

    [Fact]
    public void GetWindowRect_ReturnsValidRect()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        bool result = WindowInterop.GetWindowRect(hwnd, out var rect);
        Assert.True(result);
        Assert.True(rect.Width > 0, "Window width should be positive");
        Assert.True(rect.Height > 0, "Window height should be positive");
    }

    [Fact]
    public void GetDpiForWindow_ReturnsAtLeast96()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        uint dpi = DisplayInterop.GetDpiForWindow(hwnd);
        Assert.True(dpi >= 96, $"DPI {dpi} should be at least 96");
    }

    [Fact]
    public void GetScaleFactorForWindow_ReturnsAtLeast1()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        double scale = DisplayInterop.GetScaleFactorForWindow(hwnd);
        Assert.True(scale >= 1.0, $"Scale factor {scale} should be at least 1.0");
    }

    [Fact]
    public void GetCaptionHeight_ReturnsPositiveValue()
    {
        int height = SystemInterop.GetCaptionHeight();
        Assert.True(height > 0, $"Caption height {height} should be positive");
    }

    [Fact]
    public void IsWindow_ReturnsTrueForForegroundWindow()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        Assert.True(WindowInterop.IsWindow(hwnd));
    }

    [Fact]
    public void IsWindow_ReturnsFalseForInvalidHandle()
    {
        var invalid = new HWND(unchecked((nint)0xDEADBEEF));
        Assert.False(WindowInterop.IsWindow(invalid));
    }

    [Fact]
    public void DwmGetColorizationColor_Succeeds()
    {
        var hr = DwmInterop.GetColorizationColor(out uint color, out _);
        Assert.True(hr.Succeeded, $"DwmGetColorizationColor failed with HRESULT {hr.Value}");
        Assert.NotEqual(0u, color);
    }

    [Fact]
    public void MonitorFromWindow_ReturnsNonNull()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        var monitor = WindowInterop.MonitorFromWindow(
            hwnd,
            Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        Assert.False(monitor.IsNull);
    }
}
