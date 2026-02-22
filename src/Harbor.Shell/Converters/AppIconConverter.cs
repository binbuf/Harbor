using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Harbor.Core.Services;
using ManagedShell.WindowsTasks;

namespace Harbor.Shell.Converters;

/// <summary>
/// Converts an ApplicationWindow to a high-resolution icon using IconExtractionService.
/// Falls back to the ApplicationWindow's built-in Icon if extraction fails.
/// </summary>
[ValueConversion(typeof(ApplicationWindow), typeof(ImageSource))]
public class AppIconConverter : IValueConverter
{
    public IconExtractionService? IconService { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ApplicationWindow appWindow)
            return null;

        if (IconService is null)
            return appWindow.Icon;

        var exePath = appWindow.WinFileName;
        if (string.IsNullOrWhiteSpace(exePath))
            return appWindow.Icon;

        var icon = IconService.GetIcon(exePath);
        return icon;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
