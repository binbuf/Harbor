using System.Diagnostics;

namespace Harbor.Core.Services;

/// <summary>
/// State machine for the Menu Bar auto-hide behavior.
/// Transitions: Hidden → Revealing → Visible → Hiding → Hidden.
/// </summary>
public sealed class MenuBarAutoHideService : IDisposable
{
    public enum AutoHideState
    {
        Hidden,
        Revealing,
        Visible,
        Hiding,
    }

    public static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    private AutoHideState _state = AutoHideState.Visible;
    private CancellationTokenSource? _delayCts;
    private bool _disposed;

    public AutoHideState State => _state;

    public event Action? ShowRequested;
    public event Action? HideRequested;

    public void ForceHidden()
    {
        if (_disposed) return;
        CancelPendingDelay();
        TransitionTo(AutoHideState.Hidden);
    }

    public void OnTriggerZoneEnter()
    {
        if (_disposed) return;

        if (_state == AutoHideState.Hidden)
        {
            TransitionTo(AutoHideState.Revealing);
            StartDelayedTransition(RevealDelay, () =>
            {
                ShowRequested?.Invoke();
                TransitionTo(AutoHideState.Visible);
            });
        }
        else if (_state == AutoHideState.Hiding)
        {
            CancelPendingDelay();
            ShowRequested?.Invoke();
            TransitionTo(AutoHideState.Visible);
        }
    }

    public void OnMenuBarEnter()
    {
        if (_disposed) return;

        if (_state == AutoHideState.Hiding)
        {
            CancelPendingDelay();
            TransitionTo(AutoHideState.Visible);
        }
    }

    public void OnMenuBarLeave()
    {
        if (_disposed) return;

        if (_state == AutoHideState.Visible)
        {
            TransitionTo(AutoHideState.Hiding);
            StartDelayedTransition(HideDelay, () =>
            {
                HideRequested?.Invoke();
                TransitionTo(AutoHideState.Hidden);
            });
        }
        else if (_state == AutoHideState.Revealing)
        {
            CancelPendingDelay();
            TransitionTo(AutoHideState.Hidden);
        }
    }

    public void OnShowAnimationCompleted()
    {
        if (_state == AutoHideState.Revealing)
            TransitionTo(AutoHideState.Visible);
    }

    public void OnHideAnimationCompleted()
    {
        if (_state == AutoHideState.Hiding)
            TransitionTo(AutoHideState.Hidden);
    }

    private void TransitionTo(AutoHideState newState)
    {
        if (_state == newState) return;
        Trace.WriteLine($"[Harbor] MenuBarAutoHide: {_state} → {newState}");
        _state = newState;
    }

    private void StartDelayedTransition(TimeSpan delay, Action callback)
    {
        CancelPendingDelay();
        _delayCts = new CancellationTokenSource();
        var token = _delayCts.Token;

        _ = Task.Delay(delay, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                callback();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void CancelPendingDelay()
    {
        _delayCts?.Cancel();
        _delayCts?.Dispose();
        _delayCts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelPendingDelay();
    }
}
