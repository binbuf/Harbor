using System.Windows.Media;

namespace Harbor.Core.Models;

/// <summary>
/// Represents a discovered installed application (Win32 or UWP/Store).
/// </summary>
public class AppInfo
{
    public required string DisplayName { get; set; }
    public required string ExecutablePath { get; set; }
    public string? LaunchArguments { get; set; }
    public bool IsStoreApp { get; set; }
    public string? PackageFamilyName { get; set; }
    public ImageSource? Icon { get; set; }
    public string? Category { get; set; }
}
