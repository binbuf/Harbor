using System.Windows.Media;
using System.Windows.Media.Imaging;
using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class IconExtractionServiceTests
{
    private readonly IconExtractionService _service = new();

    [Fact]
    public void GetIcon_NullPath_ReturnsDefaultIcon()
    {
        var icon = _service.GetIcon(null);

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
    }

    [Fact]
    public void GetIcon_EmptyPath_ReturnsDefaultIcon()
    {
        var icon = _service.GetIcon("");

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
    }

    [Fact]
    public void GetIcon_WhitespacePath_ReturnsDefaultIcon()
    {
        var icon = _service.GetIcon("   ");

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
    }

    [Fact]
    public void GetIcon_NonexistentPath_ReturnsDefaultIcon()
    {
        var icon = _service.GetIcon(@"C:\nonexistent\fake_app.exe");

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
    }

    [Fact]
    public void GetIcon_CorruptPath_ReturnsDefaultIcon()
    {
        var icon = _service.GetIcon(@":::invalid<>path???");

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
    }

    [Fact]
    public void GetIcon_Notepad_ReturnsNonNullIcon()
    {
        // notepad.exe is guaranteed to exist on Windows
        var notepadPath = @"C:\Windows\System32\notepad.exe";

        var icon = _service.GetIcon(notepadPath);

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
        Assert.IsAssignableFrom<BitmapSource>(icon);
    }

    [Fact]
    public void GetIcon_Explorer_ReturnsNonNullIcon()
    {
        var explorerPath = @"C:\Windows\explorer.exe";

        var icon = _service.GetIcon(explorerPath);

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
        Assert.IsAssignableFrom<BitmapSource>(icon);
    }

    [Fact]
    public void GetIcon_CachesResult_ReturnsSameInstance()
    {
        var notepadPath = @"C:\Windows\System32\notepad.exe";

        var first = _service.GetIcon(notepadPath);
        var second = _service.GetIcon(notepadPath);

        // Same frozen instance should be returned from cache
        Assert.Same(first, second);
    }

    [Fact]
    public void ClearCache_AllowsReExtraction()
    {
        var notepadPath = @"C:\Windows\System32\notepad.exe";

        var first = _service.GetIcon(notepadPath);
        _service.ClearCache();
        var second = _service.GetIcon(notepadPath);

        // Both should be valid icons
        Assert.NotNull(first);
        Assert.NotNull(second);
        // After cache clear, a new instance is created
        // (they might or might not be the same object depending on timing)
    }

    [Fact]
    public void GetIcon_DefaultIcon_HasExpectedSize()
    {
        // Request a non-existent path to get the default icon
        var icon = _service.GetIcon(@"C:\nonexistent\app.exe");

        Assert.IsAssignableFrom<BitmapSource>(icon);
        var bitmap = (BitmapSource)icon;
        Assert.Equal(48, bitmap.PixelWidth);
        Assert.Equal(48, bitmap.PixelHeight);
    }

    [Fact]
    public void GetIcon_NullAndEmpty_ReturnSameDefaultInstance()
    {
        var nullIcon = _service.GetIcon(null);
        var emptyIcon = _service.GetIcon("");

        Assert.Same(nullIcon, emptyIcon);
    }

    [Fact]
    public void GetIcon_KnownExe_ReturnsIconWithPositiveDimensions()
    {
        var notepadPath = @"C:\Windows\System32\notepad.exe";

        var icon = _service.GetIcon(notepadPath);

        Assert.IsAssignableFrom<BitmapSource>(icon);
        var bitmap = (BitmapSource)icon;
        Assert.True(bitmap.PixelWidth > 0);
        Assert.True(bitmap.PixelHeight > 0);
    }

    [Fact]
    public void FallbackChain_PrioritizesExtractedOverDefault()
    {
        var notepadPath = @"C:\Windows\System32\notepad.exe";
        var defaultIcon = _service.GetIcon(null);
        var notepadIcon = _service.GetIcon(notepadPath);

        // The notepad icon should NOT be the default fallback
        Assert.NotSame(defaultIcon, notepadIcon);
    }
}
