using System.Runtime.InteropServices;
using Windows.Win32.Foundation;

namespace Harbor.Core.Interop;

/// <summary>
/// Manual P/Invoke for SetWindowCompositionAttribute — an undocumented API
/// used to enable Acrylic blur behind windows. Not available via CsWin32.
/// </summary>
public static partial class CompositionInterop
{
    public enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor; // AABBGGRR
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    // WCA_ACCENT_POLICY attribute constant
    public const int WCA_ACCENT_POLICY = 19;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    public static unsafe bool EnableAcrylic(HWND hwnd, uint gradientColor = 0x01000000)
    {
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = gradientColor
        };

        var data = new WindowCompositionAttributeData
        {
            Attribute = WCA_ACCENT_POLICY,
            Data = new IntPtr(&accent),
            SizeOfData = Marshal.SizeOf<AccentPolicy>()
        };

        return SetWindowCompositionAttribute((nint)hwnd.Value, ref data);
    }

    public static unsafe bool DisableComposition(HWND hwnd)
    {
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_DISABLED
        };

        var data = new WindowCompositionAttributeData
        {
            Attribute = WCA_ACCENT_POLICY,
            Data = new IntPtr(&accent),
            SizeOfData = Marshal.SizeOf<AccentPolicy>()
        };

        return SetWindowCompositionAttribute((nint)hwnd.Value, ref data);
    }
}
