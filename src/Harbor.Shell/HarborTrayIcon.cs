using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using Harbor.Core.Services;

namespace Harbor.Shell;

/// <summary>
/// Creates a system tray (notification area) icon for Harbor using WinForms NotifyIcon.
/// Provides a context menu with Settings and Exit options.
/// </summary>
public sealed class HarborTrayIcon : IDisposable
{
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private readonly DockSettingsService? _dockSettings;
    private readonly ShellSettingsService? _shellSettings;
    private bool _disposed;

    public HarborTrayIcon(DockSettingsService dockSettings, ShellSettingsService shellSettings)
    {
        _dockSettings = dockSettings;
        _shellSettings = shellSettings;

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

    private System.Windows.Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();

        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) =>
        {
            Trace.WriteLine("[Harbor] HarborTrayIcon: Settings clicked.");
            if (_dockSettings is not null && _shellSettings is not null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    SettingsWindow.ShowSingleton(_dockSettings, _shellSettings));
            }
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
