using Harbor.Core.Interop;
using Harbor.Core.Services;

namespace Harbor.Core.Tests;

/// <summary>
/// Tests for WindowCommandService command resolution and capability detection.
/// </summary>
public class WindowCommandServiceTests
{
    // --- ResolveCommand tests ---

    [Fact]
    public void ResolveCommand_Close_ReturnsSC_CLOSE()
    {
        var command = WindowCommandService.ResolveCommand(TrafficLightAction.Close, isMaximized: false);
        Assert.Equal(WindowInterop.SC_CLOSE, command);
    }

    [Fact]
    public void ResolveCommand_Close_IgnoresMaximizedState()
    {
        var normal = WindowCommandService.ResolveCommand(TrafficLightAction.Close, isMaximized: false);
        var maximized = WindowCommandService.ResolveCommand(TrafficLightAction.Close, isMaximized: true);
        Assert.Equal(normal, maximized);
        Assert.Equal(WindowInterop.SC_CLOSE, normal);
    }

    [Fact]
    public void ResolveCommand_Minimize_ReturnsSC_MINIMIZE()
    {
        var command = WindowCommandService.ResolveCommand(TrafficLightAction.Minimize, isMaximized: false);
        Assert.Equal(WindowInterop.SC_MINIMIZE, command);
    }

    [Fact]
    public void ResolveCommand_Minimize_IgnoresMaximizedState()
    {
        var normal = WindowCommandService.ResolveCommand(TrafficLightAction.Minimize, isMaximized: false);
        var maximized = WindowCommandService.ResolveCommand(TrafficLightAction.Minimize, isMaximized: true);
        Assert.Equal(normal, maximized);
        Assert.Equal(WindowInterop.SC_MINIMIZE, normal);
    }

    [Fact]
    public void ResolveCommand_Maximize_WhenNotMaximized_ReturnsSC_MAXIMIZE()
    {
        var command = WindowCommandService.ResolveCommand(TrafficLightAction.Maximize, isMaximized: false);
        Assert.Equal(WindowInterop.SC_MAXIMIZE, command);
    }

    [Fact]
    public void ResolveCommand_Maximize_WhenMaximized_ReturnsSC_RESTORE()
    {
        var command = WindowCommandService.ResolveCommand(TrafficLightAction.Maximize, isMaximized: true);
        Assert.Equal(WindowInterop.SC_RESTORE, command);
    }

    // --- SC_ constant value tests ---

    [Fact]
    public void SC_CLOSE_HasCorrectValue()
    {
        Assert.Equal(0xF060u, WindowInterop.SC_CLOSE);
    }

    [Fact]
    public void SC_MINIMIZE_HasCorrectValue()
    {
        Assert.Equal(0xF020u, WindowInterop.SC_MINIMIZE);
    }

    [Fact]
    public void SC_MAXIMIZE_HasCorrectValue()
    {
        Assert.Equal(0xF030u, WindowInterop.SC_MAXIMIZE);
    }

    [Fact]
    public void SC_RESTORE_HasCorrectValue()
    {
        Assert.Equal(0xF120u, WindowInterop.SC_RESTORE);
    }

    [Fact]
    public void WM_SYSCOMMAND_HasCorrectValue()
    {
        Assert.Equal(0x0112u, WindowInterop.WM_SYSCOMMAND);
    }

    // --- Window style constant tests ---

    [Fact]
    public void WS_MINIMIZEBOX_HasCorrectValue()
    {
        Assert.Equal(0x00020000u, WindowInterop.WS_MINIMIZEBOX);
    }

    [Fact]
    public void WS_MAXIMIZEBOX_HasCorrectValue()
    {
        Assert.Equal(0x00010000u, WindowInterop.WS_MAXIMIZEBOX);
    }

    [Fact]
    public void WS_MAXIMIZE_HasCorrectValue()
    {
        Assert.Equal(0x01000000u, WindowInterop.WS_MAXIMIZE);
    }

    [Fact]
    public void WS_MINIMIZEBOX_And_WS_MAXIMIZEBOX_AreDistinctBits()
    {
        Assert.Equal(0u, WindowInterop.WS_MINIMIZEBOX & WindowInterop.WS_MAXIMIZEBOX);
    }

    // --- All actions produce valid commands ---

    [Theory]
    [InlineData(TrafficLightAction.Close)]
    [InlineData(TrafficLightAction.Minimize)]
    [InlineData(TrafficLightAction.Maximize)]
    public void ResolveCommand_AllActions_ReturnNonZero(TrafficLightAction action)
    {
        var command = WindowCommandService.ResolveCommand(action, isMaximized: false);
        Assert.NotEqual(0u, command);
    }

    [Fact]
    public void ResolveCommand_InvalidAction_ReturnsZero()
    {
        var command = WindowCommandService.ResolveCommand((TrafficLightAction)99, isMaximized: false);
        Assert.Equal(0u, command);
    }
}
