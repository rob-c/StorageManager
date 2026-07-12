using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StorageManager.VsCode;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace StorageManager.Gui;

/// <summary>
/// GUI for VS Code remote setup: describe the target (alias, host, user, optional
/// jump host), then "Set up" (install Remote-SSH, write ssh_config + settings) or
/// "Verify". Reuses the tested <see cref="VsCodeSetup"/> engine.
/// </summary>
public sealed class VsCodeWindow : Window
{
    private readonly string _sshConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
    private readonly string _settings = VsCodeSettings.DefaultPath;

    private readonly TextBox _alias = new() { Text = "lxplus", Width = 240 };
    private readonly TextBox _host = new() { Text = "lxplus.cern.ch", Width = 240 };
    private readonly TextBox _user = new() { Text = Environment.UserName, Width = 240 };
    private readonly TextBox _jump = new() { Watermark = "optional jump host (e.g. bastion)", Width = 240 };
    private readonly StackPanel _results = new() { Spacing = 6 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

    public VsCodeWindow()
    {
        Title = "VS Code remote setup";
        Width = 520;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("AppWindowBackground"));

        var setupButton = new Button { Content = "Set up", MinWidth = 100, Classes = { "accent" } };
        setupButton.Click += async (_, _) => await RunAsync(doSetup: true);
        var verifyButton = new Button { Content = "Verify", MinWidth = 100 };
        verifyButton.Click += async (_, _) => await RunAsync(doSetup: false);

        Content = new DockPanel { Margin = new Thickness(20) }
            .With(top: new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Field("Alias (name in ssh_config)", _alias),
                    Field("Host name", _host),
                    Field("Username", _user),
                    Field("Jump host", _jump),
                },
            })
            .WithBottom(new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _status,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { verifyButton, setupButton },
                    },
                    new TextBlock
                    {
                        Text = Support.Line, FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#8A8F98")),
                    },
                },
            })
            .WithCenter(new ScrollViewer { Content = _results, Margin = new Thickness(0, 14, 0, 14) });
    }

    private static Control Field(string label, Control input) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
            input,
        },
    };

    private async Task RunAsync(bool doSetup)
    {
        _results.Children.Clear();
        _status.Text = doSetup ? "Setting up…" : "Verifying…";

        var target = new VsCodeTarget(
            _alias.Text?.Trim() ?? "lxplus",
            _host.Text?.Trim() ?? "",
            _user.Text?.Trim() ?? "",
            string.IsNullOrWhiteSpace(_jump.Text) ? null : _jump.Text!.Trim());

        var setup = new VsCodeSetup(new VsCodeCli());

        if (doSetup)
        {
            var result = await Task.Run(() => setup.Setup(target, _sshConfig, _settings));
            if (result.Error is not null)
            {
                _status.Text = result.Error;
                return;
            }
            var installed = result.InstalledExtensions.Count == 0
                ? "extensions already present"
                : "installed " + string.Join(", ", result.InstalledExtensions);
            _status.Text = $"Done — {installed}. Backups: " +
                           $"{result.SshBackupPath ?? "ssh(new)"}, {result.SettingsBackupPath ?? "settings(new)"}.";
        }

        var status = setup.Verify(target, _sshConfig, _settings);
        foreach (var c in status.Checks)
        {
            var colour = c.Ok ? Color.Parse("#2BC5A8") : Color.Parse("#E05252");
            _results.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(colour),
                                  VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = $"{c.Label} — {c.Detail}", TextWrapping = TextWrapping.Wrap },
                },
            });
        }

        if (!doSetup)
            _status.Text = status.AllOk
                ? "All good — open VS Code and pick this host from Remote-SSH."
                : "Some items need attention. Click \"Set up\" to fix them.";
    }
}
