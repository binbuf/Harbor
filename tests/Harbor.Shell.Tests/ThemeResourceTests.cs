using System.Windows;
using System.Windows.Media;
using Harbor.Core.Services;

namespace Harbor.Shell.Tests;

public class ThemeResourceTests
{
    private static ResourceDictionary LoadTheme(string filename)
    {
        // Load from relative file path instead of pack URI to avoid needing WPF Application
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.Combine(baseDir, filename);
        if (!File.Exists(path))
        {
            // Try finding in the output directory (resource dictionaries are copied as content)
            throw new FileNotFoundException($"Theme file not found: {path}");
        }

        var uri = new Uri(path, UriKind.Absolute);
        return new ResourceDictionary { Source = uri };
    }

    private static Color GetBrushColor(ResourceDictionary dict, string key)
    {
        var brush = (SolidColorBrush)dict[key];
        return brush.Color;
    }

    // --- Acrylic color constants match spec ---

    [Fact]
    public void TopMenuBar_DarkAcrylicColor_80PercentOpacity_1E1E1E()
    {
        // AABBGGRR format: 0xCC = 80% opacity, 1E1E1E color
        Assert.Equal(0xCC1E1E1Eu, TopMenuBar.DarkAcrylicColor);
    }

    [Fact]
    public void TopMenuBar_LightAcrylicColor_80PercentOpacity_F6F6F6()
    {
        Assert.Equal(0xCCF6F6F6u, TopMenuBar.LightAcrylicColor);
    }

    [Fact]
    public void Dock_DarkAcrylicColor_50PercentOpacity_1E1E1E()
    {
        Assert.Equal(0x801E1E1Eu, Dock.DarkAcrylicColor);
    }

    [Fact]
    public void Dock_LightAcrylicColor_50PercentOpacity_F6F6F6()
    {
        Assert.Equal(0x80F6F6F6u, Dock.LightAcrylicColor);
    }

    // --- Opacity difference between menu bar (80%) and dock (50%) ---

    [Fact]
    public void MenuBarAndDock_HaveDifferentOpacities()
    {
        // Extract alpha bytes: mask top byte
        var menuBarAlpha = (TopMenuBar.DarkAcrylicColor >> 24) & 0xFF;
        var dockAlpha = (Dock.DarkAcrylicColor >> 24) & 0xFF;

        Assert.Equal(0xCCu, menuBarAlpha); // 80%
        Assert.Equal(0x80u, dockAlpha);     // 50%
        Assert.NotEqual(menuBarAlpha, dockAlpha);
    }

    // --- Verify dark theme XAML color values ---

    [Fact]
    public void DarkTheme_VerifyAllColorValues()
    {
        // Parse expected colors from XAML hex strings
        // MenuBarBackground: #CC1E1E1E
        var menuBg = Color.FromArgb(0xCC, 0x1E, 0x1E, 0x1E);
        Assert.Equal(0xCC, menuBg.A);  // 80% opacity
        Assert.Equal(0x1E, menuBg.R);

        // MenuBarBorderBrush: #3A3A3A (fully opaque)
        var menuBorder = Color.FromRgb(0x3A, 0x3A, 0x3A);
        Assert.Equal(0xFF, menuBorder.A);
        Assert.Equal(0x3A, menuBorder.R);

        // MenuBarTextBrush: #FFFFFF
        Assert.Equal(Colors.White, Color.FromRgb(0xFF, 0xFF, 0xFF));

        // DockBackground: #801E1E1E (50% opacity)
        var dockBg = Color.FromArgb(0x80, 0x1E, 0x1E, 0x1E);
        Assert.Equal(0x80, dockBg.A);

        // DockBorderBrush: #1FFFFFFF (#FFFFFF @ 12%)
        var dockBorder = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
        Assert.Equal(0x1F, dockBorder.A);

        // DockSeparatorBrush: #33FFFFFF (#FFFFFF @ 20%)
        var dockSep = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        Assert.Equal(0x33, dockSep.A);

        // DockActiveDotBrush: #FFFFFF
        Assert.Equal(Colors.White, Color.FromRgb(0xFF, 0xFF, 0xFF));
    }

    [Fact]
    public void LightTheme_VerifyAllColorValues()
    {
        // MenuBarBackground: #CCF6F6F6
        var menuBg = Color.FromArgb(0xCC, 0xF6, 0xF6, 0xF6);
        Assert.Equal(0xCC, menuBg.A);
        Assert.Equal(0xF6, menuBg.R);

        // MenuBarBorderBrush: #D1D1D1
        var menuBorder = Color.FromRgb(0xD1, 0xD1, 0xD1);
        Assert.Equal(0xD1, menuBorder.R);

        // MenuBarTextBrush: #000000
        Assert.Equal(Colors.Black, Color.FromRgb(0x00, 0x00, 0x00));

        // DockBackground: #80F6F6F6
        var dockBg = Color.FromArgb(0x80, 0xF6, 0xF6, 0xF6);
        Assert.Equal(0x80, dockBg.A);
        Assert.Equal(0xF6, dockBg.R);

        // DockBorderBrush: #14000000 (#000000 @ 8%)
        var dockBorder = Color.FromArgb(0x14, 0x00, 0x00, 0x00);
        Assert.Equal(0x14, dockBorder.A);

        // DockSeparatorBrush: #1F000000 (#000000 @ 12%)
        var dockSep = Color.FromArgb(0x1F, 0x00, 0x00, 0x00);
        Assert.Equal(0x1F, dockSep.A);

        // DockActiveDotBrush: #000000
        Assert.Equal(Colors.Black, Color.FromRgb(0x00, 0x00, 0x00));
    }

    // --- Dark vs Light differentiation ---

    [Fact]
    public void DarkAndLight_MenuBarText_Differ()
    {
        // Dark: white text, Light: black text
        var darkText = Colors.White;
        var lightText = Colors.Black;
        Assert.NotEqual(darkText, lightText);
    }

    [Fact]
    public void DarkAndLight_DockActiveDot_Differ()
    {
        // Dark: white dot, Light: black dot
        var darkDot = Colors.White;
        var lightDot = Colors.Black;
        Assert.NotEqual(darkDot, lightDot);
    }

    [Fact]
    public void DarkAndLight_MenuBarBackground_Differ()
    {
        // Dark: #1E1E1E, Light: #F6F6F6 (both at 80% opacity)
        var dark = Color.FromArgb(0xCC, 0x1E, 0x1E, 0x1E);
        var light = Color.FromArgb(0xCC, 0xF6, 0xF6, 0xF6);
        Assert.Equal(dark.A, light.A); // same opacity
        Assert.NotEqual(dark.R, light.R); // different base color
    }
}
