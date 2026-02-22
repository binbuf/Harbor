using Harbor.Core.Services;
using Color = System.Windows.Media.Color;

namespace Harbor.Core.Tests;

public class ColorOverrideServiceTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ColorOverrideService _service;

    public ColorOverrideServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"harbor-color-test-{Guid.NewGuid()}.json");
        _service = new ColorOverrideService(_tempPath);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (File.Exists(_tempPath))
            File.Delete(_tempPath);
    }

    [Fact]
    public void InitiallyEmpty()
    {
        Assert.Equal(0, _service.Count);
    }

    [Fact]
    public void GetOverride_ReturnsNull_ForUnknownProcess()
    {
        Assert.Null(_service.GetOverride("nonexistent"));
    }

    [Fact]
    public void GetOverride_ReturnsNull_ForNullOrEmpty()
    {
        Assert.Null(_service.GetOverride(null!));
        Assert.Null(_service.GetOverride(""));
    }

    [Fact]
    public void SetOverride_StoresColor()
    {
        _service.SetOverride("notepad", Color.FromRgb(255, 0, 0));

        var result = _service.GetOverride("notepad");
        Assert.NotNull(result);
        Assert.Equal(255, result.Value.R);
        Assert.Equal(0, result.Value.G);
        Assert.Equal(0, result.Value.B);
    }

    [Fact]
    public void SetOverride_IsCaseInsensitive()
    {
        _service.SetOverride("Notepad", Color.FromRgb(0, 255, 0));

        var result = _service.GetOverride("notepad");
        Assert.NotNull(result);
        Assert.Equal(0, result.Value.R);
        Assert.Equal(255, result.Value.G);
    }

    [Fact]
    public void SetOverride_OverwritesExisting()
    {
        _service.SetOverride("app", Color.FromRgb(100, 100, 100));
        _service.SetOverride("app", Color.FromRgb(200, 200, 200));

        var result = _service.GetOverride("app");
        Assert.NotNull(result);
        Assert.Equal(200, result.Value.R);
        Assert.Equal(1, _service.Count);
    }

    [Fact]
    public void RemoveOverride_RemovesEntry()
    {
        _service.SetOverride("app", Color.FromRgb(100, 100, 100));
        Assert.Equal(1, _service.Count);

        _service.RemoveOverride("app");
        Assert.Equal(0, _service.Count);
        Assert.Null(_service.GetOverride("app"));
    }

    [Fact]
    public void RemoveOverride_NoOp_ForUnknownProcess()
    {
        _service.RemoveOverride("nonexistent"); // should not throw
    }

    [Fact]
    public void GetAll_ReturnsAllOverrides()
    {
        _service.SetOverride("app1", Color.FromRgb(10, 20, 30));
        _service.SetOverride("app2", Color.FromRgb(40, 50, 60));

        var all = _service.GetAll();
        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey("app1"));
        Assert.True(all.ContainsKey("app2"));
    }

    [Fact]
    public void PersistsToFile()
    {
        _service.SetOverride("app", Color.FromRgb(1, 2, 3));

        // Create a new service from the same file
        using var service2 = new ColorOverrideService(_tempPath);
        var result = service2.GetOverride("app");
        Assert.NotNull(result);
        Assert.Equal(1, result.Value.R);
        Assert.Equal(2, result.Value.G);
        Assert.Equal(3, result.Value.B);
    }

    [Fact]
    public void ParseHexColor_ValidColors()
    {
        var red = ColorOverrideService.ParseHexColor("#FF0000");
        Assert.NotNull(red);
        Assert.Equal(255, red.Value.R);
        Assert.Equal(0, red.Value.G);
        Assert.Equal(0, red.Value.B);

        var white = ColorOverrideService.ParseHexColor("#FFFFFF");
        Assert.NotNull(white);
        Assert.Equal(255, white.Value.R);
        Assert.Equal(255, white.Value.G);
        Assert.Equal(255, white.Value.B);
    }

    [Fact]
    public void ParseHexColor_InvalidInput_ReturnsNull()
    {
        Assert.Null(ColorOverrideService.ParseHexColor(null!));
        Assert.Null(ColorOverrideService.ParseHexColor(""));
        Assert.Null(ColorOverrideService.ParseHexColor("not-a-color"));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new ColorOverrideService(_tempPath);
        service.Dispose();
        service.Dispose(); // should not throw
    }

    [Fact]
    public void Constructor_HandlesNonexistentDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"harbor-{Guid.NewGuid()}", "sub", "overrides.json");
        using var service = new ColorOverrideService(path);
        Assert.Equal(0, service.Count);

        // Setting an override should create the directory
        service.SetOverride("app", Color.FromRgb(1, 2, 3));
        Assert.True(File.Exists(path));

        // Clean up
        var dir = Path.GetDirectoryName(path)!;
        Directory.Delete(Path.GetDirectoryName(dir)!, true);
    }
}
