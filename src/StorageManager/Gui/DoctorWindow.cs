using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StorageManager.Doctor;

namespace StorageManager.Gui;

/// <summary>
/// GUI front-end for the SSH Doctor: runs the audit, lists findings with
/// per-finding checkboxes, previews the unified diff, and applies selected
/// fixes (writing a backup first). Reuses the tested Core engine.
/// </summary>
public sealed class DoctorWindow : Window
{
    private readonly string _configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    private readonly TextBox _hostBox = new() { Text = "staff.ph.ed.ac.uk", Width = 220 };
    private readonly CheckBox _probeBox = new() { Content = "Run network tests", IsChecked = false };
    private readonly StackPanel _findings = new() { Spacing = 10 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly List<(Finding Finding, CheckBox Box)> _rows = [];

    public DoctorWindow()
    {
        Title = "SSH Doctor";
        Width = 560;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("AppWindowBackground"));

        var runButton = new Button { Content = "Run", MinWidth = 90, Classes = { "accent" } };
        runButton.Click += async (_, _) => await RunAsync();

        var previewButton = new Button { Content = "Preview changes", MinWidth = 120 };
        previewButton.Click += async (_, _) => await ApplyAsync(dryRun: true);

        var applyButton = new Button { Content = "Apply selected", MinWidth = 120 };
        applyButton.Click += async (_, _) => await ApplyAsync(dryRun: false);

        Content = new DockPanel { Margin = new Thickness(20) }
            .With(top: new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { new TextBlock { Text = "Host:", VerticalAlignment = VerticalAlignment.Center },
                             _hostBox, _probeBox, runButton },
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
                        Children = { previewButton, applyButton },
                    },
                },
            })
            .WithCenter(new ScrollViewer { Content = _findings, Margin = new Thickness(0, 14, 0, 14) });
    }

    private async Task RunAsync()
    {
        _findings.Children.Clear();
        _rows.Clear();
        _status.Text = "Auditing…";

        var host = _hostBox.Text?.Trim() ?? "staff.ph.ed.ac.uk";
        var probe = _probeBox.IsChecked == true;
        DoctorReport report;
        try
        {
            report = await SshDoctor.CreateDefault().RunAsync(_configPath, host, probe);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            return;
        }

        if (!report.HasFindings)
        {
            _status.Text = "No problems found.";
            return;
        }

        foreach (var f in report.Findings)
        {
            var colour = f.Severity switch
            {
                Severity.Error => Color.Parse("#E05252"),
                Severity.Warning => Color.Parse("#FFB454"),
                _ => Color.Parse("#8A8F98"),
            };
            var box = new CheckBox { IsChecked = f.Fix is not null, IsEnabled = f.Fix is not null };
            _rows.Add((f, box));

            _findings.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(colour),
                BorderThickness = new Thickness(0, 0, 0, 0),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal, Spacing = 8,
                            Children =
                            {
                                box,
                                new TextBlock
                                {
                                    Text = $"[{f.Severity}] {f.Title}",
                                    FontWeight = FontWeight.SemiBold,
                                    Foreground = new SolidColorBrush(colour),
                                    TextWrapping = TextWrapping.Wrap,
                                },
                            },
                        },
                        new TextBlock { Text = f.Explanation, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = f.Fix?.Description ?? "(no automatic fix)",
                            FontSize = 12, FontStyle = FontStyle.Italic,
                            Foreground = new SolidColorBrush(Color.Parse("#8A8F98")),
                        },
                    },
                },
            });
        }
        _status.Text = $"{report.Findings.Count} finding(s). Tick the ones to fix, then Preview or Apply.";
    }

    private async Task ApplyAsync(bool dryRun)
    {
        var fixes = _rows.Where(r => r.Box.IsChecked == true && r.Finding.Fix is not null)
                         .Select(r => r.Finding.Fix!).ToList();
        if (fixes.Count == 0)
        {
            _status.Text = "No fixes selected.";
            return;
        }

        var outcome = new ConfigFixer().Apply(_configPath, fixes, dryRun);
        if (dryRun)
        {
            await Dialogs.ShowMessageAsync(this, "Proposed changes", outcome.UnifiedDiff);
        }
        else if (outcome.Applied)
        {
            _status.Text = $"Applied {fixes.Count} fix(es). Backup: {outcome.BackupPath}";
            await RunAsync();
        }
        else
        {
            _status.Text = "No changes were necessary.";
        }
    }
}

/// <summary>Small DockPanel layout helpers to keep the window body readable.</summary>
internal static class DockExtensions
{
    public static DockPanel With(this DockPanel panel, Control top)
    {
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        return panel;
    }

    public static DockPanel WithBottom(this DockPanel panel, Control bottom)
    {
        DockPanel.SetDock(bottom, Dock.Bottom);
        panel.Children.Add(bottom);
        return panel;
    }

    public static DockPanel WithCenter(this DockPanel panel, Control center)
    {
        panel.Children.Add(center);
        return panel;
    }
}
