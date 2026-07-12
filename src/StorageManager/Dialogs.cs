using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace StorageManager;

public static class Dialogs
{
    public static Task ShowMessageAsync(Window owner, string title, string text) =>
        ShowAsync(owner, title, text, confirm: false);

    public static Task<bool> ConfirmAsync(Window owner, string title, string text) =>
        ShowAsync(owner, title, text, confirm: true);

    public enum CloseChoice { Cancel, MinimizeToTray, Disconnect }

    /// <summary>Offers minimize-to-tray, disconnect-and-close, or cancel when closing a live mount.</summary>
    public static async Task<CloseChoice> ChooseCloseAsync(Window owner, string title, string text)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 560,
        };
        dialog.Bind(Window.BackgroundProperty, dialog.GetResourceObservable("AppWindowBackground"));

        var result = CloseChoice.Cancel;
        var tray = new Button { Content = "Minimize to tray", MinWidth = 130, Classes = { "accent" } };
        var disconnect = new Button { Content = "Disconnect & close", MinWidth = 130 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        tray.Click += (_, _) => { result = CloseChoice.MinimizeToTray; dialog.Close(); };
        disconnect.Click += (_, _) => { result = CloseChoice.Disconnect; dialog.Close(); };
        cancel.Click += (_, _) => { result = CloseChoice.Cancel; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { tray, disconnect, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>Prompts for a single line of text; returns null if cancelled or empty.</summary>
    public static async Task<string?> PromptAsync(Window owner, string title, string label, string watermark)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MinWidth = 380,
            MaxWidth = 560,
        };
        dialog.Bind(Window.BackgroundProperty, dialog.GetResourceObservable("AppWindowBackground"));

        var input = new TextBox { Watermark = watermark };
        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = input.Text?.Trim(); dialog.Close(); };
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = label, FontSize = 12 },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { ok, cancel },
                },
            },
        };
        dialog.Opened += (_, _) => input.Focus();
        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static Task<bool> ShowAsync(Window owner, string title, string text, bool confirm)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 560,
        };
        dialog.Bind(Window.BackgroundProperty, dialog.GetResourceObservable("AppWindowBackground"));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };

        if (confirm)
        {
            var yes = new Button { Content = "Yes", MinWidth = 90 };
            var no = new Button { Content = "No", MinWidth = 90, IsDefault = true, IsCancel = true };
            yes.Click += (_, _) => dialog.Close(true);
            no.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true, IsCancel = true };
            ok.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(ok);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                buttons,
            },
        };

        return dialog.ShowDialog<bool>(owner);
    }
}
