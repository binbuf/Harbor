using System.Runtime.InteropServices;
using System.Text;

namespace Harbor.Core.Interop;

/// <summary>
/// Minimal managed wrapper for reading .lnk shortcut files via COM IShellLink.
/// </summary>
public static class ShellLinkInterop
{
    /// <summary>
    /// Information extracted from a .lnk shortcut file.
    /// </summary>
    public record ShellLinkInfo(
        string TargetPath,
        string Arguments,
        string Description,
        string IconLocation,
        int IconIndex);

    /// <summary>
    /// Reads a .lnk shortcut file and returns target path, arguments, and icon info.
    /// Returns null if the shortcut cannot be read.
    /// </summary>
    public static ShellLinkInfo? ReadShortcut(string lnkPath)
    {
        try
        {
            var shellLink = (IShellLinkW)new ShellLinkCoClass();
            var persistFile = (IPersistFile)shellLink;

            persistFile.Load(lnkPath, STGM_READ);

            var targetPath = new StringBuilder(260);
            var findData = new WIN32_FIND_DATAW();
            shellLink.GetPath(targetPath, targetPath.Capacity, ref findData, SLGP_RAWPATH);

            var arguments = new StringBuilder(1024);
            shellLink.GetArguments(arguments, arguments.Capacity);

            var description = new StringBuilder(1024);
            shellLink.GetDescription(description, description.Capacity);

            var iconLocation = new StringBuilder(260);
            shellLink.GetIconLocation(iconLocation, iconLocation.Capacity, out int iconIndex);

            Marshal.FinalReleaseComObject(shellLink);

            return new ShellLinkInfo(
                targetPath.ToString(),
                arguments.ToString(),
                description.ToString(),
                iconLocation.ToString(),
                iconIndex);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a ms-resource:// or @{...} indirect string reference to its display value.
    /// Returns the original string if resolution fails.
    /// </summary>
    public static string ResolveIndirectString(string indirectString)
    {
        if (string.IsNullOrEmpty(indirectString))
            return indirectString;

        // Only attempt resolution for resource references
        if (!indirectString.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) &&
            !indirectString.StartsWith("@{", StringComparison.Ordinal) &&
            !indirectString.StartsWith("@", StringComparison.Ordinal))
            return indirectString;

        var buffer = new StringBuilder(1024);
        var hr = SHLoadIndirectString(indirectString, buffer, (uint)buffer.Capacity, IntPtr.Zero);

        return hr == 0 ? buffer.ToString() : indirectString;
    }

    private const int STGM_READ = 0x00000000;
    private const int SLGP_RAWPATH = 0x4;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCoClass { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void QueryInterface([In] ref Guid riid, out IntPtr ppvObject);
        uint AddRef();
        uint Release();

        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch,
            ref WIN32_FIND_DATAW pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
            [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        uint cchOutBuf,
        IntPtr ppvReserved);
}
