using Avalonia.Controls;
using Avalonia.Platform;

namespace StorageManager.Gui;

/// <summary>
/// Manages an optional system-tray presence for a connected window. Tray
/// support varies by desktop (notably absent on some Linux setups), so every
/// operation is best-effort and failure silently degrades to no tray.
/// </summary>
public sealed class TrayController
{
    private readonly Window _window;
    private TrayIcon? _icon;

    public TrayController(Window window) => _window = window;

    /// <summary>Shows the tray icon and hides the window. Returns false if the tray is unavailable.</summary>
    public bool MinimizeToTray(string targetDescription, Action onRestore, Action onDisconnect, Action onQuit)
    {
        try
        {
            var restore = new NativeMenuItem("Open Storage Manager");
            restore.Click += (_, _) => onRestore();
            var disconnect = new NativeMenuItem($"Disconnect {targetDescription}");
            disconnect.Click += (_, _) => onDisconnect();
            var quit = new NativeMenuItem("Quit");
            quit.Click += (_, _) => onQuit();

            _icon = new TrayIcon
            {
                ToolTipText = $"Storage Manager — {targetDescription}",
                Icon = LoadIcon(),
                IsVisible = true,
                Menu = new NativeMenu { Items = { restore, new NativeMenuItemSeparator(), disconnect, quit } },
            };
            _icon.Clicked += (_, _) => onRestore();

            _window.Hide();
            return true;
        }
        catch
        {
            Remove();
            return false;
        }
    }

    public void Remove()
    {
        try
        {
            if (_icon is not null)
            {
                _icon.IsVisible = false;
                _icon.Dispose();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _icon = null;
        }
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://StorageManager/Assets/logo.png"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }
}
