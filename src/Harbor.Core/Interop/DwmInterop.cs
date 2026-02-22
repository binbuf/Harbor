using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace Harbor.Core.Interop;

/// <summary>
/// Wrapper over Desktop Window Manager (DWM) APIs.
/// </summary>
public static class DwmInterop
{
    // DWMWA constants
    public const uint DWMWA_CAPTION_BUTTON_BOUNDS = 5;
    public const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    // DWMSBT constants
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    // DWMWCP constants
    public const int DWMWCP_ROUND = 2;

    public static HRESULT RegisterThumbnail(HWND destination, HWND source, out nint thumbnailId)
    {
        thumbnailId = 0;
        var hr = PInvoke.DwmRegisterThumbnail(destination, source, out var id);
        thumbnailId = id;
        return hr;
    }

    public static HRESULT UnregisterThumbnail(nint thumbnailId)
    {
        return PInvoke.DwmUnregisterThumbnail(thumbnailId);
    }

    public static unsafe HRESULT UpdateThumbnailProperties(nint thumbnailId, in DWM_THUMBNAIL_PROPERTIES properties)
    {
        return PInvoke.DwmUpdateThumbnailProperties(thumbnailId, in properties);
    }

    public static HRESULT Flush()
    {
        return PInvoke.DwmFlush();
    }

    public static HRESULT GetColorizationColor(out uint color, out bool opaqueBlend)
    {
        var hr = PInvoke.DwmGetColorizationColor(out color, out var blend);
        opaqueBlend = blend;
        return hr;
    }

    public static unsafe HRESULT GetWindowAttribute<T>(HWND hwnd, DWMWINDOWATTRIBUTE attribute, out T value) where T : unmanaged
    {
        value = default;
        fixed (T* ptr = &value)
        {
            return PInvoke.DwmGetWindowAttribute(hwnd, attribute, ptr, (uint)Marshal.SizeOf<T>());
        }
    }

    public static unsafe HRESULT SetWindowAttribute<T>(HWND hwnd, DWMWINDOWATTRIBUTE attribute, in T value) where T : unmanaged
    {
        fixed (T* ptr = &value)
        {
            return PInvoke.DwmSetWindowAttribute(hwnd, attribute, ptr, (uint)Marshal.SizeOf<T>());
        }
    }
}
