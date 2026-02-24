using System.Windows.Media;

namespace Harbor.Core.Models;

/// <summary>
/// Represents a discovered installed application.
/// ExecutablePath contains a shell:AppsFolder launch URI that works for both Win32 and UWP apps.
/// </summary>
public class AppInfo
{
    public required string DisplayName { get; set; }
    public required string ExecutablePath { get; set; }
    public string? LaunchArguments { get; set; }
    public ImageSource? Icon { get; set; }
}
