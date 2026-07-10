using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MountTool;

public static class Dialogs
{
    public static Task ShowMessageAsync(Window owner, string title, string text) =>
        ShowAsync(owner, title, text, confirm: false);

    public static Task<bool> ConfirmAsync(Window owner, string title, string text) =>
        ShowAsync(owner, title, text, confirm: true);

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
