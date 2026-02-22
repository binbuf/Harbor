using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace Harbor.Core.Services;

/// <summary>
/// Represents the current system color theme.
/// </summary>
public enum AppTheme
{
    Dark,
    Light,
}

/// <summary>
/// Detects the Windows system dark/light mode preference by reading the registry key
/// HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme.
/// Subscribes to registry changes for real-time theme switching.
/// </summary>
public sealed class ThemeService : INotifyPropertyChanged, IDisposable
{
    private const string PersonalizeKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private AppTheme _currentTheme;
    private bool _disposed;

    /// <summary>
    /// Fired when the theme changes (Dark ↔ Light).
    /// </summary>
    public event Action<AppTheme>? ThemeChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The current system theme.
    /// </summary>
    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (_currentTheme == value) return;
            _currentTheme = value;
            OnPropertyChanged();
            ThemeChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// True if the current theme is dark mode.
    /// </summary>
    public bool IsDarkMode => _currentTheme == AppTheme.Dark;

    public ThemeService()
    {
        _currentTheme = ReadThemeFromRegistry();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Trace.WriteLine($"[Harbor] ThemeService: Initialized. Current theme: {_currentTheme}");
    }

    /// <summary>
    /// Reads the AppsUseLightTheme registry value.
    /// 0 = dark mode, 1 = light mode. Defaults to dark if the key is missing.
    /// </summary>
    public static AppTheme ReadThemeFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            var value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int intValue)
                return intValue == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] ThemeService: Failed to read registry: {ex.Message}");
        }

        // Default to dark mode if registry key is missing
        return AppTheme.Dark;
    }

    /// <summary>
    /// Handles UserPreferenceChanged events, which fire when the user toggles dark/light mode.
    /// </summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed) return;

        // Theme changes fire as UserPreferenceCategory.General
        if (e.Category != UserPreferenceCategory.General) return;

        var newTheme = ReadThemeFromRegistry();
        if (newTheme != _currentTheme)
        {
            Trace.WriteLine($"[Harbor] ThemeService: Theme changed from {_currentTheme} to {newTheme}");
            CurrentTheme = newTheme;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Trace.WriteLine("[Harbor] ThemeService: Disposed.");
    }
}
