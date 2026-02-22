using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class DockAutoHideServiceTests
{
    [Fact]
    public void InitialState_IsVisible()
    {
        using var service = new DockAutoHideService();
        Assert.Equal(DockAutoHideService.AutoHideState.Visible, service.State);
    }

    [Fact]
    public void RevealDelay_Is200ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), DockAutoHideService.RevealDelay);
    }

    [Fact]
    public void HideDelay_Is1000ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(1000), DockAutoHideService.HideDelay);
    }

    [Fact]
    public void ShowAnimationDuration_Is250ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), DockAutoHideService.ShowAnimationDuration);
    }

    [Fact]
    public void HideAnimationDuration_Is200ms()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(200), DockAutoHideService.HideAnimationDuration);
    }

    [Fact]
    public void TriggerZoneHeight_Is2Dip()
    {
        Assert.Equal(2.0, DockAutoHideService.TriggerZoneHeight);
    }

    [Fact]
    public void DockAreaLeave_WhenVisible_TransitionsToHiding()
    {
        using var service = new DockAutoHideService();
        Assert.Equal(DockAutoHideService.AutoHideState.Visible, service.State);

        service.OnDockAreaLeave();

        Assert.Equal(DockAutoHideService.AutoHideState.Hiding, service.State);
    }

    [Fact]
    public void DockAreaEnter_WhenHiding_CancelsAndReturnsToVisible()
    {
        using var service = new DockAutoHideService();

        service.OnDockAreaLeave(); // Visible → Hiding
        Assert.Equal(DockAutoHideService.AutoHideState.Hiding, service.State);

        service.OnDockAreaEnter(); // Hiding → Visible (cancel hide)
        Assert.Equal(DockAutoHideService.AutoHideState.Visible, service.State);
    }

    [Fact]
    public void TriggerZoneEnter_WhenHidden_TransitionsToRevealing()
    {
        using var service = new DockAutoHideService();

        // Force to Hidden state via the public API
        service.OnDockAreaLeave(); // Visible → Hiding
        service.OnHideAnimationCompleted(); // Hiding → Hidden

        Assert.Equal(DockAutoHideService.AutoHideState.Hidden, service.State);

        service.OnTriggerZoneEnter();
        Assert.Equal(DockAutoHideService.AutoHideState.Revealing, service.State);
    }

    [Fact]
    public void DockAreaLeave_WhenRevealing_CancelsAndReturnsToHidden()
    {
        using var service = new DockAutoHideService();

        // Force to Hidden
        service.OnDockAreaLeave();
        service.OnHideAnimationCompleted();

        service.OnTriggerZoneEnter(); // Hidden → Revealing
        Assert.Equal(DockAutoHideService.AutoHideState.Revealing, service.State);

        service.OnDockAreaLeave(); // Revealing → Hidden (cancel reveal)
        Assert.Equal(DockAutoHideService.AutoHideState.Hidden, service.State);
    }

    [Fact]
    public void TriggerZoneEnter_WhenHiding_CancelsAndGoesToVisible()
    {
        using var service = new DockAutoHideService();

        service.OnDockAreaLeave(); // Visible → Hiding
        Assert.Equal(DockAutoHideService.AutoHideState.Hiding, service.State);

        service.OnTriggerZoneEnter(); // Hiding → Visible
        Assert.Equal(DockAutoHideService.AutoHideState.Visible, service.State);
    }

    [Fact]
    public void StateChanged_FiresOnTransition()
    {
        using var service = new DockAutoHideService();
        var states = new List<DockAutoHideService.AutoHideState>();
        service.StateChanged += s => states.Add(s);

        service.OnDockAreaLeave(); // Visible → Hiding
        service.OnDockAreaEnter(); // Hiding → Visible

        Assert.Equal(
        [
            DockAutoHideService.AutoHideState.Hiding,
            DockAutoHideService.AutoHideState.Visible,
        ], states);
    }

    [Fact]
    public void HideRequested_NotFiredImmediately_OnDockAreaLeave()
    {
        using var service = new DockAutoHideService();
        var hideFired = false;
        service.HideRequested += () => hideFired = true;

        service.OnDockAreaLeave();

        // The hide event is delayed by 1000ms, not immediate
        Assert.False(hideFired);
    }

    [Fact]
    public void Dispose_PreventsStateChanges()
    {
        var service = new DockAutoHideService();
        service.Dispose();

        // Should be no-op after dispose
        service.OnDockAreaLeave();
        Assert.Equal(DockAutoHideService.AutoHideState.Visible, service.State);
    }

    [Fact]
    public void FullStateMachineCycle_HiddenToRevealingToVisibleToHidingToHidden()
    {
        using var service = new DockAutoHideService();

        // Start Visible, go to Hiding → Hidden
        service.OnDockAreaLeave();
        Assert.Equal(DockAutoHideService.AutoHideState.Hiding, service.State);
        service.OnHideAnimationCompleted();
        Assert.Equal(DockAutoHideService.AutoHideState.Hidden, service.State);

        // Hidden → Revealing
        service.OnTriggerZoneEnter();
        Assert.Equal(DockAutoHideService.AutoHideState.Revealing, service.State);

        // Revealing → Visible (via show animation complete)
        service.OnShowAnimationCompleted();
        Assert.Equal(DockAutoHideService.AutoHideState.Visible, service.State);

        // Visible → Hiding
        service.OnDockAreaLeave();
        Assert.Equal(DockAutoHideService.AutoHideState.Hiding, service.State);

        // Hiding → Hidden
        service.OnHideAnimationCompleted();
        Assert.Equal(DockAutoHideService.AutoHideState.Hidden, service.State);
    }
}
