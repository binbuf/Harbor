using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Harbor.Core.Interop;

/// <summary>
/// Wrapper over system-level Win32 APIs.
/// </summary>
public static class SystemInterop
{
    // SystemParametersInfo constants
    public const uint SPI_SETCLIENTAREAANIMATION = 0x1043;
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    // GetSystemMetrics constants
    public const int SM_CYCAPTION = 4;

    public static int GetCaptionHeight()
    {
        return PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYCAPTION);
    }

    public static unsafe bool SetClientAreaAnimation(bool enabled)
    {
        BOOL value = enabled;
        return PInvoke.SystemParametersInfo(
            SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETCLIENTAREAANIMATION,
            0,
            &value,
            SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
    }

    public static unsafe bool GetClientAreaAnimation()
    {
        BOOL value = default;
        PInvoke.SystemParametersInfo(
            (SYSTEM_PARAMETERS_INFO_ACTION)SPI_GETCLIENTAREAANIMATION,
            0,
            &value,
            0);
        return value;
    }

    public static uint GetDoubleClickTime()
    {
        return PInvoke.GetDoubleClickTime();
    }
}
