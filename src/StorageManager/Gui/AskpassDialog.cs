using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace StorageManager.Gui;

/// <summary>
/// Avalonia implementation of the askpass challenge dialog. Shown when ssh
/// issues a non-password prompt (e.g. a CERN 2FA one-time-code challenge).
/// Registered as <see cref="Askpass.ChallengeHandler"/> in the GUI entry path.
/// </summary>
public static class AskpassDialog
{
    public static string? Prompt(string prompt)
    {
        AskpassApp.Prompt = prompt;
        AppBuilder.Configure<AskpassApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime([]);
        return AskpassApp.Response;
    }
}

public class AskpassApp : Application
{
    public static string Prompt { get; set; } = "";
    public static string? Response { get; set; }

    public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = BuildWindow();

        base.OnFrameworkInitializationCompleted();
    }

    private static Window BuildWindow()
    {
        var window = new Window
        {
            Title = "Storage Manager — authentication",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            MaxWidth = 560,
            Topmost = true,
        };

        var input = new TextBox { Watermark = "response" };

        var ok = new Button { Content = "OK", MinWidth = 90, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) => { Response = input.Text ?? ""; window.Close(); };
        cancel.Click += (_, _) => { Response = null; window.Close(); };

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            MinWidth = 380,
            Children =
            {
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(Prompt) ? "The server requests a response:" : Prompt,
                    TextWrapping = TextWrapping.Wrap,
                },
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

        window.Opened += (_, _) => input.Focus();
        return window;
    }
}
