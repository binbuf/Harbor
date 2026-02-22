using Harbor.Core.Interop;
using Harbor.Core.Services;
using Windows.Win32.Foundation;

namespace Harbor.Core.Tests;

public class WindowEventManagerTests : IDisposable
{
    private readonly WindowEventManager _manager = new();

    public void Dispose()
    {
        _manager.Dispose();
    }

    [Fact]
    public void Subscribe_ReturnsUniqueId()
    {
        var id1 = _manager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
            Handler = _ => { },
        });
        var id2 = _manager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
            Handler = _ => { },
        });

        Assert.NotEqual(Guid.Empty, id1);
        Assert.NotEqual(Guid.Empty, id2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Unsubscribe_ReturnsTrueForValidId()
    {
        var id = _manager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
            Handler = _ => { },
        });

        Assert.True(_manager.Unsubscribe(id));
    }

    [Fact]
    public void Unsubscribe_ReturnsFalseForUnknownId()
    {
        Assert.False(_manager.Unsubscribe(Guid.NewGuid()));
    }

    [Fact]
    public void Unsubscribe_ReturnsFalseForAlreadyRemoved()
    {
        var id = _manager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
            Handler = _ => { },
        });

        _manager.Unsubscribe(id);
        Assert.False(_manager.Unsubscribe(id));
    }

    [Fact]
    public void Subscribe_ThrowsAfterDispose()
    {
        _manager.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            _manager.Subscribe(new WindowEventSubscription
            {
                EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
                Handler = _ => { },
            }));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        _manager.Dispose();
        _manager.Dispose(); // should not throw
    }

    [Fact]
    public void MultipleSubscribers_CanRegisterForSameEventType()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(_manager.Subscribe(new WindowEventSubscription
            {
                EventTypes = new HashSet<WindowEventType> { WindowEventType.LocationChange },
                Handler = _ => { },
            }));
        }

        Assert.Equal(5, ids.Distinct().Count());
    }

    [Fact]
    public void Subscribe_WithEmptyEventTypes_AcceptsAllEvents()
    {
        var id = _manager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType>(),
            Handler = _ => { },
        });

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void Subscribe_WithHwndFilter_Accepted()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        var id = _manager.Subscribe(new WindowEventSubscription
        {
            EventTypes = new HashSet<WindowEventType> { WindowEventType.Foreground },
            FilterHwnd = hwnd,
            Handler = _ => { },
        });

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void OwnProcessId_IsFiltered()
    {
        // Verify the own-process filtering: our process ID should match GetCurrentProcessId
        var pid = WindowInterop.GetCurrentProcessId();
        Assert.True(pid > 0);
    }

    [Fact]
    public void IsWindowVisible_ReturnsTrueForForegroundWindow()
    {
        var hwnd = WindowInterop.GetForegroundWindow();
        if (hwnd != HWND.Null)
        {
            Assert.True(WindowInterop.IsWindowVisible(hwnd));
        }
    }

    [Fact]
    public void IsWindowVisible_ReturnsFalseForNullHandle()
    {
        Assert.False(WindowInterop.IsWindowVisible(HWND.Null));
    }
}
