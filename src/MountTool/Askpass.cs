using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace MountTool;

/// <summary>
/// SSH_ASKPASS handler: ssh invokes this executable once per authentication
/// prompt. The initial "user@host's password:" prompt is answered silently
/// with the password handed over by the main process; anything else (e.g. a
/// PAM two-factor challenge) is shown to the user in a dialog.
/// </summary>
public static class Askpass
{
    public const string ModeVariable = "PPE_ASKPASS_MODE";
    public const string PasswordVariable = "PPE_ASKPASS_PASSWORD";

    public static int Run(string prompt)
    {
        var password = Environment.GetEnvironmentVariable(PasswordVariable);

        if (password is not null && prompt.Contains("'s password", StringComparison.OrdinalIgnoreCase))
        {
            Console.Out.Write(password);
            Console.Out.Flush();
            return 0;
        }

        AskpassApp.Prompt = prompt;
        AppBuilder.Configure<AskpassApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime([]);

        if (AskpassApp.Response is null)
            return 1;

        Console.Out.Write(AskpassApp.Response);
        Console.Out.Flush();
        return 0;
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
            Title = "PPE Storage — authentication",
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
