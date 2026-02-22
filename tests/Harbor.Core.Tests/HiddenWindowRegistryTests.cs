using Harbor.Core.Services;

namespace Harbor.Core.Tests;

/// <summary>
/// Tests for the HiddenWindowRegistry: add, remove, clear, duplicate handling,
/// max capacity, and atomic count tracking.
/// </summary>
public class HiddenWindowRegistryTests : IDisposable
{
    private readonly HiddenWindowRegistry _registry = new();

    public void Dispose() => _registry.Dispose();

    [Fact]
    public void NewRegistry_HasZeroCount()
    {
        // Clear any state from previous test runs sharing the named MMF
        _registry.Clear();
        Assert.Equal(0, _registry.Count);
    }

    [Fact]
    public void Add_SingleHwnd_IncrementsCount()
    {
        _registry.Clear();
        _registry.Add(0x1234);

        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void Add_MultipleHwnds_TracksAll()
    {
        _registry.Clear();
        _registry.Add(0x1000);
        _registry.Add(0x2000);
        _registry.Add(0x3000);

        Assert.Equal(3, _registry.Count);

        var all = _registry.GetAll();
        Assert.Contains((nint)0x1000, all);
        Assert.Contains((nint)0x2000, all);
        Assert.Contains((nint)0x3000, all);
    }

    [Fact]
    public void Add_DuplicateHwnd_DoesNotIncrement()
    {
        _registry.Clear();
        _registry.Add(0x1234);
        _registry.Add(0x1234);

        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void Remove_ExistingHwnd_DecrementsCount()
    {
        _registry.Clear();
        _registry.Add(0x1000);
        _registry.Add(0x2000);

        _registry.Remove(0x1000);

        Assert.Equal(1, _registry.Count);
        var all = _registry.GetAll();
        Assert.Contains((nint)0x2000, all);
        Assert.DoesNotContain((nint)0x1000, all);
    }

    [Fact]
    public void Remove_NonExistentHwnd_NoEffect()
    {
        _registry.Clear();
        _registry.Add(0x1000);

        _registry.Remove(0x9999);

        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void Remove_LastEntry_CountGoesToZero()
    {
        _registry.Clear();
        _registry.Add(0x1000);
        _registry.Remove(0x1000);

        Assert.Equal(0, _registry.Count);
        Assert.Empty(_registry.GetAll());
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        _registry.Clear();
        _registry.Add(0x1000);
        _registry.Add(0x2000);
        _registry.Add(0x3000);

        _registry.Clear();

        Assert.Equal(0, _registry.Count);
        Assert.Empty(_registry.GetAll());
    }

    [Fact]
    public void GetAll_ReturnsCorrectValues()
    {
        _registry.Clear();
        _registry.Add(0xAAAA);
        _registry.Add(0xBBBB);

        var all = _registry.GetAll();

        Assert.Equal(2, all.Length);
        Assert.Contains((nint)0xAAAA, all);
        Assert.Contains((nint)0xBBBB, all);
    }

    [Fact]
    public void Remove_MiddleEntry_UsesSwapRemove()
    {
        _registry.Clear();
        _registry.Add(0x1000);
        _registry.Add(0x2000);
        _registry.Add(0x3000);

        // Remove middle entry — last entry (0x3000) should be swapped in
        _registry.Remove(0x2000);

        Assert.Equal(2, _registry.Count);
        var all = _registry.GetAll();
        Assert.Contains((nint)0x1000, all);
        Assert.Contains((nint)0x3000, all);
        Assert.DoesNotContain((nint)0x2000, all);
    }

    [Fact]
    public void Constants_HaveExpectedValues()
    {
        Assert.Equal(512, HiddenWindowRegistry.MaxEntries);
        Assert.Equal(4, HiddenWindowRegistry.HeaderSize);
        Assert.Equal(8, HiddenWindowRegistry.EntrySize);
        Assert.Equal(4 + 512 * 8, HiddenWindowRegistry.TotalSize);
    }
}
