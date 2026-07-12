using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MountTool.Errors;

namespace MountTool.Gui;

/// <summary>
/// Shows a preflight problem and, when the mounter offered one, an actionable
/// remediation button: install prerequisites via winget, copy an install
/// command, or open a download page.
/// </summary>
public static class PreflightDialog
{
    public static async Task ShowAsync(Window owner, PreflightResult result)
    {
        var dialog = new Window
        {
            Title = "PPE Storage",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 560,
        };
        dialog.Bind(Window.BackgroundProperty, dialog.GetResourceObservable("AppWindowBackground"));

        var status = new TextBlock { Text = "", FontSize = 12, TextWrapping = TextWrapping.Wrap };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
        };

        if (result.Fix is { } fix)
        {
            var action = new Button { Content = fix.Label, MinWidth = 120, Classes = { "accent" } };
            action.Click += async (_, _) => await RunFixAsync(fix, action, status, owner);
            buttons.Children.Add(action);
        }

        var ok = new Button { Content = "Close", MinWidth = 90, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => dialog.Close();
        buttons.Children.Add(ok);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = result.Message, TextWrapping = TextWrapping.Wrap },
                status,
                buttons,
            },
        };

        await dialog.ShowDialog(owner);
    }

    private static async Task RunFixAsync(FixAction fix, Button action, TextBlock status, Window owner)
    {
        switch (fix.Kind)
        {
            case FixKindUi.OpenUrl:
                OpenUrl(fix.Payload);
                break;

            case FixKindUi.CopyCommand:
                if (owner.Clipboard is { } clip)
                    await clip.SetTextAsync(fix.Payload);
                status.Text = $"Copied: {fix.Payload}\nPaste it into a terminal and run it, then try again.";
                break;

            case FixKindUi.WingetInstall:
                action.IsEnabled = false;
                status.Text = "Installing prerequisites… a Windows permission prompt may appear.";
                var ok = await Task.Run(() => RunWinget(fix.Payload));
                status.Text = ok
                    ? "Prerequisites installed. Close this dialog and click Connect again."
                    : "Automatic install failed. Please install WinFsp and SSHFS-Win manually.";
                action.IsEnabled = !ok;
                break;
        }
    }

    private static bool RunWinget(string packageList)
    {
        try
        {
            foreach (var id in packageList.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var info = new ProcessStartInfo("winget")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                foreach (var arg in new[] { "install", "-e", "--id", id, "--accept-package-agreements", "--accept-source-agreements" })
                    info.ArgumentList.Add(arg);
                using var p = Process.Start(info);
                if (p is null) return false;
                p.WaitForExit(600_000);
                if (p.ExitCode != 0) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Non-fatal.
        }
    }
}
