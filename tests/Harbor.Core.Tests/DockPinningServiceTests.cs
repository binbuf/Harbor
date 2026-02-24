using System.Text.Json;
using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class DockPinningServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public DockPinningServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Harbor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "dock-pins.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void NewService_HasNoPins()
    {
        using var svc = new DockPinningService(_configPath);
        Assert.Empty(svc.Pins);
    }

    [Fact]
    public void Pin_AddsApp()
    {
        using var svc = new DockPinningService(_configPath);

        svc.Pin(@"C:\app\test.exe", "Test App");

        Assert.Single(svc.Pins);
        Assert.Equal(@"C:\app\test.exe", svc.Pins[0].ExecutablePath);
        Assert.Equal("Test App", svc.Pins[0].DisplayName);
    }

    [Fact]
    public void Pin_SameAppTwice_NoDuplicate()
    {
        using var svc = new DockPinningService(_configPath);

        svc.Pin(@"C:\app\test.exe");
        svc.Pin(@"C:\app\test.exe");

        Assert.Single(svc.Pins);
    }

    [Fact]
    public void Pin_CaseInsensitive()
    {
        using var svc = new DockPinningService(_configPath);

        svc.Pin(@"C:\App\Test.EXE");

        Assert.True(svc.IsPinned(@"c:\app\test.exe"));
    }

    [Fact]
    public void Unpin_RemovesApp()
    {
        using var svc = new DockPinningService(_configPath);

        svc.Pin(@"C:\app\test.exe");
        svc.Unpin(@"C:\app\test.exe");

        Assert.Empty(svc.Pins);
        Assert.False(svc.IsPinned(@"C:\app\test.exe"));
    }

    [Fact]
    public void Unpin_NonExistent_NoOp()
    {
        using var svc = new DockPinningService(_configPath);

        svc.Unpin(@"C:\app\nonexistent.exe"); // should not throw
        Assert.Empty(svc.Pins);
    }

    [Fact]
    public void IsPinned_ReturnsFalseForEmpty()
    {
        using var svc = new DockPinningService(_configPath);
        Assert.False(svc.IsPinned(""));
        Assert.False(svc.IsPinned(null!));
    }

    [Fact]
    public void Pin_PersistsToFile()
    {
        using (var svc = new DockPinningService(_configPath))
        {
            svc.Pin(@"C:\app\notepad.exe", "Notepad");
            svc.Pin(@"C:\app\calc.exe", "Calculator");
        }

        Assert.True(File.Exists(_configPath));

        // Load a new instance and verify persistence
        using var svc2 = new DockPinningService(_configPath);
        Assert.Equal(2, svc2.Pins.Count);
        Assert.True(svc2.IsPinned(@"C:\app\notepad.exe"));
        Assert.True(svc2.IsPinned(@"C:\app\calc.exe"));
    }

    [Fact]
    public void Unpin_PersistsRemoval()
    {
        using (var svc = new DockPinningService(_configPath))
        {
            svc.Pin(@"C:\app\notepad.exe", "Notepad");
            svc.Pin(@"C:\app\calc.exe", "Calculator");
            svc.Unpin(@"C:\app\notepad.exe");
        }

        using var svc2 = new DockPinningService(_configPath);
        Assert.Single(svc2.Pins);
        Assert.True(svc2.IsPinned(@"C:\app\calc.exe"));
        Assert.False(svc2.IsPinned(@"C:\app\notepad.exe"));
    }

    [Fact]
    public void Pin_PreservesOrder()
    {
        using var svc = new DockPinningService(_configPath);

        svc.Pin(@"C:\app\a.exe", "A");
        svc.Pin(@"C:\app\b.exe", "B");
        svc.Pin(@"C:\app\c.exe", "C");

        Assert.Equal("A", svc.Pins[0].DisplayName);
        Assert.Equal("B", svc.Pins[1].DisplayName);
        Assert.Equal("C", svc.Pins[2].DisplayName);
    }

    [Fact]
    public void PinsChanged_FiresOnPin()
    {
        using var svc = new DockPinningService(_configPath);
        var fired = false;
        svc.PinsChanged += (_, _) => fired = true;

        svc.Pin(@"C:\app\test.exe");

        Assert.True(fired);
    }

    [Fact]
    public void PinsChanged_FiresOnUnpin()
    {
        using var svc = new DockPinningService(_configPath);
        svc.Pin(@"C:\app\test.exe");

        var fired = false;
        svc.PinsChanged += (_, _) => fired = true;

        svc.Unpin(@"C:\app\test.exe");

        Assert.True(fired);
    }

    [Fact]
    public void PinsChanged_DoesNotFireOnDuplicatePin()
    {
        using var svc = new DockPinningService(_configPath);
        svc.Pin(@"C:\app\test.exe");

        var fired = false;
        svc.PinsChanged += (_, _) => fired = true;

        svc.Pin(@"C:\app\test.exe"); // duplicate

        Assert.False(fired);
    }

    [Fact]
    public void Pin_WithNullDisplayName_UsesFileName()
    {
        using var svc = new DockPinningService(_configPath);

        svc.Pin(@"C:\app\myapp.exe");

        Assert.Equal("Myapp", svc.Pins[0].DisplayName);
    }

    [Fact]
    public void Load_CorruptFile_StartsEmpty()
    {
        File.WriteAllText(_configPath, "not valid json!!!");

        using var svc = new DockPinningService(_configPath);
        Assert.Empty(svc.Pins);
    }

    [Fact]
    public void Pin_CreatesDirectoryIfNeeded()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "dir", "dock-pins.json");

        using var svc = new DockPinningService(nestedPath);
        svc.Pin(@"C:\app\test.exe");

        Assert.True(File.Exists(nestedPath));
    }
}
