using System.Diagnostics;
using System.IO;

namespace Harbor.Core.Services;

/// <summary>
/// Manages "Open at Login" functionality by creating/removing shortcuts
/// in the user's Startup folder (shell:startup).
/// </summary>
public static class StartupShortcutService
{
    private static string StartupFolder =>
        Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    /// <summary>
    /// Returns true if a startup shortcut exists for the given executable.
    /// </summary>
    public static bool IsOpenAtLogin(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return false;
        var shortcutPath = GetShortcutPath(executablePath);
        return File.Exists(shortcutPath);
    }

    /// <summary>
    /// Creates a startup shortcut for the given executable. Uses a .url file
    /// for simplicity (no COM dependency on IShellLink).
    /// </summary>
    public static void Enable(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return;

        try
        {
            var shortcutPath = GetShortcutPath(executablePath);

            // Write a simple .url shortcut file that launches the executable
            var content = $"""
                [InternetShortcut]
                URL=file:///{executablePath.Replace('\\', '/')}
                IconIndex=0
                IconFile={executablePath}
                """;

            File.WriteAllText(shortcutPath, content);
            Trace.WriteLine($"[Harbor] StartupShortcutService: Created startup shortcut for {executablePath}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] StartupShortcutService: Failed to create shortcut: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the startup shortcut for the given executable.
    /// </summary>
    public static void Disable(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return;

        try
        {
            var shortcutPath = GetShortcutPath(executablePath);
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
                Trace.WriteLine($"[Harbor] StartupShortcutService: Removed startup shortcut for {executablePath}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] StartupShortcutService: Failed to remove shortcut: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggles the startup shortcut state.
    /// </summary>
    public static void Toggle(string executablePath)
    {
        if (IsOpenAtLogin(executablePath))
            Disable(executablePath);
        else
            Enable(executablePath);
    }

    private static string GetShortcutPath(string executablePath)
    {
        var appName = Path.GetFileNameWithoutExtension(executablePath);
        return Path.Combine(StartupFolder, $"{appName}.url");
    }
}
