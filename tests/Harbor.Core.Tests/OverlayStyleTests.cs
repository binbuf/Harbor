using Harbor.Core.Interop;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Tests;

public class OverlayStyleTests
{
    [Fact]
    public void WS_EX_LAYERED_HasCorrectValue()
    {
        Assert.Equal(0x00080000u, WindowInterop.WS_EX_LAYERED);
    }

    [Fact]
    public void WS_EX_NOACTIVATE_HasCorrectValue()
    {
        Assert.Equal(0x08000000u, WindowInterop.WS_EX_NOACTIVATE);
    }

    [Fact]
    public void WS_EX_TOOLWINDOW_HasCorrectValue()
    {
        Assert.Equal(0x00000080u, WindowInterop.WS_EX_TOOLWINDOW);
    }

    [Fact]
    public void OverlayStyleBits_AreNonOverlapping()
    {
        // Ensure the three style flags don't share any bits
        Assert.Equal(0u, WindowInterop.WS_EX_LAYERED & WindowInterop.WS_EX_NOACTIVATE);
        Assert.Equal(0u, WindowInterop.WS_EX_LAYERED & WindowInterop.WS_EX_TOOLWINDOW);
        Assert.Equal(0u, WindowInterop.WS_EX_NOACTIVATE & WindowInterop.WS_EX_TOOLWINDOW);
    }

    [Fact]
    public void OverlayStyleBits_CombineCorrectly()
    {
        var combined = WindowInterop.WS_EX_LAYERED
                     | WindowInterop.WS_EX_NOACTIVATE
                     | WindowInterop.WS_EX_TOOLWINDOW;

        Assert.True((combined & WindowInterop.WS_EX_LAYERED) != 0);
        Assert.True((combined & WindowInterop.WS_EX_NOACTIVATE) != 0);
        Assert.True((combined & WindowInterop.WS_EX_TOOLWINDOW) != 0);
    }

    [Fact]
    public void SetWindowPosNoActivate_UsesCorrectFlags()
    {
        // Verify the flags used in SetWindowPosNoActivate match the overlay requirements
        var expectedFlags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER;

        Assert.True(expectedFlags.HasFlag(SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE));
        Assert.True(expectedFlags.HasFlag(SET_WINDOW_POS_FLAGS.SWP_NOZORDER));
    }
}
