using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Harbor.Core.Interop;

/// <summary>
/// Wrapper over DPI and monitor APIs.
/// </summary>
public static class DisplayInterop
{
    public const uint BASE_DPI = 96;

    public static uint GetDpiForWindow(HWND hwnd)
    {
        return PInvoke.GetDpiForWindow(hwnd);
    }

    public static double GetScaleFactorForWindow(HWND hwnd)
    {
        return PInvoke.GetDpiForWindow(hwnd) / (double)BASE_DPI;
    }

    /// <summary>
    /// Converts a physical pixel value to WPF DIPs for the given window's monitor DPI.
    /// </summary>
    public static double PhysicalToDip(int physicalPixels, HWND hwnd)
    {
        var scale = GetScaleFactorForWindow(hwnd);
        return scale > 0 ? physicalPixels / scale : physicalPixels;
    }

    /// <summary>
    /// Converts a physical pixel value to WPF DIPs using a pre-computed scale factor.
    /// </summary>
    public static double PhysicalToDip(int physicalPixels, double scaleFactor)
    {
        return scaleFactor > 0 ? physicalPixels / scaleFactor : physicalPixels;
    }

    /// <summary>
    /// Pure DPI scaling calculation: applies (dpi / 96.0) to a value.
    /// </summary>
    public static double ScaleForDpi(double value, uint dpi)
    {
        return value * (dpi / (double)BASE_DPI);
    }

    /// <summary>
    /// Pure DPI scaling calculation: computes scale factor from DPI.
    /// </summary>
    public static double ComputeScaleFactor(uint dpi)
    {
        return dpi / (double)BASE_DPI;
    }

    /// <summary>
    /// Returns the monitor handle (as IntPtr) for the monitor hosting the given window.
    /// </summary>
    public static IntPtr GetMonitorForWindow(HWND hwnd)
    {
        var hmonitor = WindowInterop.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        unsafe
        {
            return (IntPtr)hmonitor.Value;
        }
    }
}
