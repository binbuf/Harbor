// Resolve ambiguities between WPF and WinForms namespaces.
// WPF types take priority since this is a WPF application.
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using Image = System.Windows.Controls.Image;
global using MenuItem = System.Windows.Controls.MenuItem;
global using MessageBox = System.Windows.MessageBox;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using NotifyIcon = ManagedShell.WindowsTray.NotifyIcon;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using UserControl = System.Windows.Controls.UserControl;
