using System.Text.Json;
using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class DockSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public DockSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Harbor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "dock-settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void NewService_HasDefaultSettings()
    {
        using var svc = new DockSettingsService(_configPath);

        Assert.Equal(52, svc.IconSize);
        Assert.False(svc.FullWidthDock);
    }

    [Fact]
    public void Load_ValidJson_AppliesSettings()
    {
        File.WriteAllText(_configPath, """{"iconSize": 64, "fullWidthDock": true}""");

        using var svc = new DockSettingsService(_configPath);

        Assert.Equal(64, svc.IconSize);
        Assert.True(svc.FullWidthDock);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(_configPath, "not valid json!!!");

        using var svc = new DockSettingsService(_configPath);

        Assert.Equal(52, svc.IconSize);
        Assert.False(svc.FullWidthDock);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        // Don't create the file
        using var svc = new DockSettingsService(_configPath);

        Assert.Equal(52, svc.IconSize);
        Assert.False(svc.FullWidthDock);
    }

    [Fact]
    public void IconSize_ClampedToMinimum()
    {
        using var svc = new DockSettingsService(_configPath);

        svc.IconSize = 10;

        Assert.Equal(32, svc.IconSize);
    }

    [Fact]
    public void IconSize_ClampedToMaximum()
    {
        using var svc = new DockSettingsService(_configPath);

        svc.IconSize = 200;

        Assert.Equal(128, svc.IconSize);
    }

    [Fact]
    public void IconSize_ValidRange_Accepted()
    {
        using var svc = new DockSettingsService(_configPath);

        svc.IconSize = 96;

        Assert.Equal(96, svc.IconSize);
    }

    [Fact]
    public void Load_OutOfRangeIconSize_ClampedOnLoad()
    {
        File.WriteAllText(_configPath, """{"iconSize": 256, "fullWidthDock": false}""");

        using var svc = new DockSettingsService(_configPath);

        Assert.Equal(128, svc.IconSize);
    }

    [Fact]
    public void SaveLoad_RoundTrip()
    {
        using (var svc = new DockSettingsService(_configPath))
        {
            svc.IconSize = 72;
            svc.FullWidthDock = true;
        }

        Assert.True(File.Exists(_configPath));

        using var svc2 = new DockSettingsService(_configPath);
        Assert.Equal(72, svc2.IconSize);
        Assert.True(svc2.FullWidthDock);
    }

    [Fact]
    public void SettingsChanged_FiresOnIconSizeChange()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.IconSize = 64;

        Assert.True(fired);
    }

    [Fact]
    public void SettingsChanged_FiresOnFullWidthDockChange()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.FullWidthDock = true;

        Assert.True(fired);
    }

    [Fact]
    public void SettingsChanged_DoesNotFireWhenSameIconSize()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.IconSize = 52; // same as default

        Assert.False(fired);
    }

    [Fact]
    public void SettingsChanged_DoesNotFireWhenSameFullWidthDock()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.FullWidthDock = false; // same as default

        Assert.False(fired);
    }

    [Fact]
    public void Save_CreatesDirectoryIfNeeded()
    {
        var nestedPath = Path.Combine(_tempDir, "sub", "dir", "dock-settings.json");

        using var svc = new DockSettingsService(nestedPath);
        svc.IconSize = 64;

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void IconSize_BoundaryValues_Accepted()
    {
        using var svc = new DockSettingsService(_configPath);

        svc.IconSize = 32;
        Assert.Equal(32, svc.IconSize);

        svc.IconSize = 128;
        Assert.Equal(128, svc.IconSize);
    }

    [Fact]
    public void NewService_HasDefaultAutoHideMode()
    {
        using var svc = new DockSettingsService(_configPath);
        Assert.Equal(DockAutoHideMode.Never, svc.AutoHideMode);
    }

    [Fact]
    public void NewService_HasDefaultMagnificationEnabled()
    {
        using var svc = new DockSettingsService(_configPath);
        Assert.False(svc.MagnificationEnabled);
    }

    [Fact]
    public void SaveLoad_RoundTrip_AutoHideMode()
    {
        using (var svc = new DockSettingsService(_configPath))
        {
            svc.AutoHideMode = DockAutoHideMode.Always;
        }

        using var svc2 = new DockSettingsService(_configPath);
        Assert.Equal(DockAutoHideMode.Always, svc2.AutoHideMode);
    }

    [Fact]
    public void SaveLoad_RoundTrip_MagnificationEnabled()
    {
        using (var svc = new DockSettingsService(_configPath))
        {
            svc.MagnificationEnabled = true;
        }

        using var svc2 = new DockSettingsService(_configPath);
        Assert.True(svc2.MagnificationEnabled);
    }

    [Fact]
    public void SettingsChanged_FiresOnAutoHideModeChange()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.AutoHideMode = DockAutoHideMode.WhenOverlapped;

        Assert.True(fired);
    }

    [Fact]
    public void SettingsChanged_DoesNotFireWhenSameAutoHideMode()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.AutoHideMode = DockAutoHideMode.Never; // same as default

        Assert.False(fired);
    }

    [Fact]
    public void SettingsChanged_FiresOnMagnificationEnabledChange()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.MagnificationEnabled = true;

        Assert.True(fired);
    }

    [Fact]
    public void SettingsChanged_DoesNotFireWhenSameMagnificationEnabled()
    {
        using var svc = new DockSettingsService(_configPath);
        var fired = false;
        svc.SettingsChanged += (_, _) => fired = true;

        svc.MagnificationEnabled = false; // same as default

        Assert.False(fired);
    }
}
