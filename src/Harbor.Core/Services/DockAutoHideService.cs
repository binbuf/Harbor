using System.Diagnostics;

namespace Harbor.Core.Services;

/// <summary>
/// State machine for the Dock auto-hide behavior.
/// Transitions: Hidden → Revealing → Visible → Hiding → Hidden.
/// </summary>
public sealed class DockAutoHideService : IDisposable
{
    /// <summary>Auto-hide states.</summary>
    public enum AutoHideState
    {
        Hidden,
        Revealing,
        Visible,
        Hiding,
    }

    /// <summary>Delay before revealing the dock after cursor enters the trigger zone.</summary>
    public static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Delay before hiding the dock after cursor leaves the dock area.</summary>
    public static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Duration of the slide-up show animation.</summary>
    public static readonly TimeSpan ShowAnimationDuration = TimeSpan.FromMilliseconds(400);

    /// <summary>Duration of the slide-down hide animation.</summary>
    public static readonly TimeSpan HideAnimationDuration = TimeSpan.FromMilliseconds(350);

    /// <summary>Height of the invisible trigger zone at the bottom screen edge (DIP).</summary>
    public const double TriggerZoneHeight = 2.0;

    private AutoHideState _state = AutoHideState.Visible;
    private CancellationTokenSource? _delayCts;
    private bool _disposed;
    private bool _suppressHide;

    /// <summary>Current state of the auto-hide system.</summary>
    public AutoHideState State => _state;

    /// <summary>
    /// Raised when the dock should begin its show animation (transition to Visible).
    /// </summary>
    public event Action? ShowRequested;

    /// <summary>
    /// Raised when the dock should begin its hide animation (transition to Hidden).
    /// </summary>
    public event Action? HideRequested;

    /// <summary>
    /// Raised whenever the state changes.
    /// </summary>
    public event Action<AutoHideState>? StateChanged;

    /// <summary>
    /// Immediately transitions to Hidden state without animation.
    /// Used when starting in "Always" auto-hide mode.
    /// </summary>
    public void ForceHidden()
    {
        if (_disposed) return;
        CancelPendingDelay();
        TransitionTo(AutoHideState.Hidden);
    }

    /// <summary>
    /// Call when the cursor enters the trigger zone at the bottom of the screen.
    /// </summary>
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
            // Cancel the hide and go directly to visible
            CancelPendingDelay();
            ShowRequested?.Invoke();
            TransitionTo(AutoHideState.Visible);
        }
    }

    /// <summary>
    /// Call when the cursor enters the dock area (the dock panel itself).
    /// </summary>
    public void OnDockAreaEnter()
    {
        if (_disposed) return;

        if (_state == AutoHideState.Hiding)
        {
            CancelPendingDelay();
            TransitionTo(AutoHideState.Visible);
        }
    }

    /// <summary>
    /// Suppresses auto-hide behavior (e.g. while a context menu is open).
    /// Cancels any pending hide delay.
    /// </summary>
    public void SuppressHide()
    {
        _suppressHide = true;
        CancelPendingDelay();
    }

    /// <summary>
    /// Resumes auto-hide behavior after suppression.
    /// Does not automatically trigger hide — caller should check mouse position.
    /// </summary>
    public void ResumeHide()
    {
        _suppressHide = false;
    }

    /// <summary>
    /// Call when the cursor leaves the dock area entirely (neither trigger zone nor dock panel).
    /// </summary>
    public void OnDockAreaLeave()
    {
        if (_disposed) return;
        if (_suppressHide) return;

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
            // Cancel the reveal and go back to hidden
            CancelPendingDelay();
            TransitionTo(AutoHideState.Hidden);
        }
    }

    /// <summary>
    /// Called when the show animation completes.
    /// </summary>
    public void OnShowAnimationCompleted()
    {
        if (_state == AutoHideState.Revealing)
            TransitionTo(AutoHideState.Visible);
    }

    /// <summary>
    /// Called when the hide animation completes.
    /// </summary>
    public void OnHideAnimationCompleted()
    {
        if (_state == AutoHideState.Hiding)
            TransitionTo(AutoHideState.Hidden);
    }

    private void TransitionTo(AutoHideState newState)
    {
        if (_state == newState) return;
        Trace.WriteLine($"[Harbor] DockAutoHide: {_state} → {newState}");
        _state = newState;
        StateChanged?.Invoke(newState);
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
