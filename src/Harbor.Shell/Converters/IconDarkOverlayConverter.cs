using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Harbor.Shell.Converters;

/// <summary>
/// Converts an icon ImageSource into a pixel-exact black overlay bitmap that preserves
/// only the icon's alpha channel. This ensures the dark press/drag overlay perfectly
/// matches any icon shape — circle, rounded rect, custom silhouette, etc.
/// </summary>
[ValueConversion(typeof(ImageSource), typeof(ImageSource))]
public class IconDarkOverlayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not BitmapSource source)
            return null;

        try
        {
            // Normalise to Bgra32 so we always have a consistent 4-byte-per-pixel layout
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[height * stride];
            bgra.CopyPixels(pixels, stride, 0);

            // Zero out RGB, keep alpha — result is pure black with the icon's silhouette
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i]     = 0; // B
                pixels[i + 1] = 0; // G
                pixels[i + 2] = 0; // R
                // pixels[i + 3] = alpha (unchanged)
            }

            var overlay = BitmapSource.Create(
                width, height,
                source.DpiX, source.DpiY,
                PixelFormats.Bgra32,
                null, pixels, stride);
            overlay.Freeze();
            return overlay;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
