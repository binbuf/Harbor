using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;

namespace Harbor.Shell;

/// <summary>
/// Creates a system tray (notification area) icon for Harbor using WinForms NotifyIcon.
/// Provides a context menu with Settings and Exit options.
/// </summary>
public sealed class HarborTrayIcon : IDisposable
{
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _disposed;

    public HarborTrayIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Harbor",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };

        Trace.WriteLine("[Harbor] HarborTrayIcon: Created.");
    }

    private static Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Harbor] HarborTrayIcon: Failed to load icon: {ex.Message}");
        }

        // Fallback to embedded application icon
        return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
               ?? SystemIcons.Application;
    }

    private static System.Windows.Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) =>
        {
            Trace.WriteLine("[Harbor] HarborTrayIcon: Settings clicked (placeholder).");
        };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            Trace.WriteLine("[Harbor] HarborTrayIcon: Exit clicked.");
            Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        Trace.WriteLine("[Harbor] HarborTrayIcon: Disposed.");
    }
}
