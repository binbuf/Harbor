using System.Windows;

namespace Harbor.Core.Services;

/// <summary>
/// A UI element that can be hidden/shown during fullscreen retreat.
/// </summary>
public interface IRetreatable
{
    Visibility Visibility { get; set; }
    void UpdatePosition();
}
