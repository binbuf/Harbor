using Windows.Win32;
using Windows.Win32.Foundation;

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
}
