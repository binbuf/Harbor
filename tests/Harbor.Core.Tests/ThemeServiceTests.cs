using Harbor.Core.Services;

namespace Harbor.Core.Tests;

public class ThemeServiceTests : IDisposable
{
    private readonly ThemeService _service = new();

    public void Dispose()
    {
        _service.Dispose();
    }

    // --- Registry reading ---

    [Fact]
    public void ReadThemeFromRegistry_ReturnsDarkOrLight()
    {
        var theme = ThemeService.ReadThemeFromRegistry();
        Assert.True(theme is AppTheme.Dark or AppTheme.Light);
    }

    [Fact]
    public void CurrentTheme_MatchesRegistryValue()
    {
        var expected = ThemeService.ReadThemeFromRegistry();
        Assert.Equal(expected, _service.CurrentTheme);
    }

    [Fact]
    public void IsDarkMode_ConsistentWithCurrentTheme()
    {
        Assert.Equal(_service.CurrentTheme == AppTheme.Dark, _service.IsDarkMode);
    }

    // --- Enum coverage ---

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    public void AppTheme_AllValuesAreDefined(AppTheme theme)
    {
        Assert.True(Enum.IsDefined(theme));
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = new ThemeService();
        service.Dispose();
        service.Dispose(); // should not throw
    }

    // --- PropertyChanged ---

    [Fact]
    public void CurrentTheme_PropertyChangedEventExists()
    {
        // Verify the service implements INotifyPropertyChanged
        Assert.IsAssignableFrom<System.ComponentModel.INotifyPropertyChanged>(_service);
    }

    // --- ThemeChanged event ---

    [Fact]
    public void ThemeChanged_EventCanBeSubscribed()
    {
        var fired = false;
        _service.ThemeChanged += _ => fired = true;

        // We can't easily trigger a real theme change in a test,
        // but we can verify the event subscription doesn't throw
        Assert.False(fired);
    }
}
