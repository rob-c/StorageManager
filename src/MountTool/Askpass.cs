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

        // Two login-prompt shapes get the stored password: password auth
        // ("user@host's password:") and PAM keyboard-interactive, which sends
        // a bare "Password:" optionally prefixed with "(user@host)". Longer
        // texts (e.g. "One-time password (OATH)...") are challenges for the
        // user and go to the dialog.
        var isLoginPasswordPrompt =
            prompt.Contains("'s password", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(
                prompt, @"^\s*(\([^)]*\)\s*)?Password\s*:\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (password is not null && isLoginPasswordPrompt)
        {
            Log($"prompt=[{prompt}] -> silent password len={password.Length} " +
                $"ascii={password.All(char.IsAscii)} encoding={Console.OutputEncoding.WebName}");
            WriteRawUtf8(password);
            return 0;
        }

        Log($"prompt=[{prompt}] -> dialog");
        try
        {
            AskpassApp.Prompt = prompt;
            AppBuilder.Configure<AskpassApp>()
                .UsePlatformDetect()
                .WithInterFont()
                .StartWithClassicDesktopLifetime([]);
        }
        catch (Exception ex)
        {
            Log($"dialog failed: {ex}");
            return 1;
        }

        Log($"dialog result: {(AskpassApp.Response is null ? "cancelled" : $"{AskpassApp.Response.Length} chars")}");

        if (AskpassApp.Response is null)
            return 1;

        WriteRawUtf8(AskpassApp.Response);
        return 0;
    }

    /// <summary>Writes to stdout as raw UTF-8 bytes, immune to console codepage translation.</summary>
    private static void WriteRawUtf8(string text)
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(System.Text.Encoding.UTF8.GetBytes(text));
        stdout.Flush();
    }

    /// <summary>With PPE_DEBUG set, appends askpass activity to ppe-askpass.log in the temp directory.</summary>
    private static void Log(string message)
    {
        if (Environment.GetEnvironmentVariable("PPE_DEBUG") is null)
            return;
        DebugLog(message);
    }

    /// <summary>Unconditionally appends to ppe-askpass.log in the temp directory.</summary>
    public static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "ppe-askpass.log"),
                $"{DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} {message}\n");
        }
        catch
        {
            // Diagnostics must never break authentication.
        }
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
