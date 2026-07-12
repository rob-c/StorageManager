using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using StorageManager.Status;
using StorageManager.Storage;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace StorageManager.Gui;

/// <summary>
/// The consolidated "Storage &amp; Auth status" view: Kerberos ticket state with a
/// one-click "Get ticket" (kinit + aklog), and per-path usage/quota bars for the
/// mounted volume and remote AFS/EOS paths. Backed by the tested Core services.
/// </summary>
public sealed class StatusWindow : Window
{
    private readonly StatusService _service;

    private readonly TextBox _host = new() { Text = "lxplus.cern.ch", Width = 220 };
    private readonly TextBox _user = new() { Text = Environment.UserName, Width = 220 };
    private readonly TextBox _principal = new() { Watermark = "you@CERN.CH", Width = 220 };
    private readonly TextBox _password = new() { PasswordChar = '●', Width = 220 };
    private readonly TextBox _paths = new()
    {
        Text = "/afs/cern.ch/user,/eos/user",
        Width = 460,
        Watermark = "comma-separated remote paths",
    };

    private readonly TextBlock _kerberos = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Ellipse _kerberosDot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _quotas = new() { Spacing = 10 };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

    public StatusWindow(string? host = null, string? user = null, bool useKerberos = false)
    {
        _service = StatusService.CreateDefault(useKerberos);
        if (host is not null) _host.Text = host;
        if (user is not null) _user.Text = user;

        Title = "Storage & Auth status";
        Width = 560;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("AppWindowBackground"));

        var refresh = new Button { Content = "Refresh", MinWidth = 100, Classes = { "accent" } };
        refresh.Click += async (_, _) => await RefreshAsync();
        var getTicket = new Button { Content = "Get ticket", MinWidth = 100 };
        getTicket.Click += async (_, _) => await GetTicketAsync();

        // The app-wide Kerberos switch lives in the main window; when off, the
        // ticket controls here are inert and say so.
        if (!useKerberos)
        {
            _principal.IsEnabled = false;
            _password.IsEnabled = false;
            getTicket.IsEnabled = false;
        }

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Section("Host"));
        body.Children.Add(Row("Host", _host));
        body.Children.Add(Row("Username", _user));
        body.Children.Add(Row("Remote paths", _paths));

        body.Children.Add(Section("Kerberos"));
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { _kerberosDot, _kerberos },
        });
        body.Children.Add(Row("Principal", _principal));
        body.Children.Add(Row("Password", _password));
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Left,
            Children = { getTicket },
        });

        body.Children.Add(Section("Storage usage"));
        body.Children.Add(_quotas);

        Content = new DockPanel { Margin = new Thickness(20) }
            .With(top: new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { refresh },
            })
            .WithBottom(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _status,
                    new TextBlock
                    {
                        Text = Support.Line, FontSize = 11,
                        Foreground = new SolidColorBrush(Color.Parse("#8A8F98")),
                    },
                },
            })
            .WithCenter(new ScrollViewer { Content = body, Margin = new Thickness(0, 12, 0, 12) });

        Opened += async (_, _) => await RefreshAsync();
    }

    private static TextBlock Section(string title) => new()
    {
        Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, Margin = new Thickness(0, 8, 0, 0),
    };

    private static Control Row(string label, Control input) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
            input,
        },
    };

    private async Task GetTicketAsync()
    {
        var principal = _principal.Text?.Trim() ?? "";
        var password = _password.Text ?? "";
        if (principal.Length == 0 || password.Length == 0)
        {
            _status.Text = "Enter a principal and password to get a ticket.";
            return;
        }
        _status.Text = "Requesting Kerberos ticket…";
        var result = await Task.Run(() => _service.Authenticate(principal, password));
        _password.Text = "";
        _status.Text = result.HasValidTicket
            ? "Ticket obtained."
            : "Could not obtain a ticket. Check the principal/password, or run kinit in a terminal.";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _status.Text = "Refreshing…";
        var request = new StatusRequest(
            _host.Text?.Trim() ?? "",
            _user.Text?.Trim() ?? "",
            (_paths.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        StatusReport report;
        try
        {
            report = await _service.GatherAsync(request);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            return;
        }

        var k = report.Kerberos;
        _kerberosDot.Fill = new SolidColorBrush(
            !_service.KerberosEnabled || !k.ToolsAvailable ? Color.Parse("#8A8F98") :
            k.HasValidTicket ? Color.Parse("#2BC5A8") : Color.Parse("#E05252"));
        _kerberos.Text = !_service.KerberosEnabled
            ? "Kerberos sign-in is turned off — enable it in the main window to use tickets."
            : !k.ToolsAvailable ? "Kerberos tools not installed."
            : k.HasValidTicket ? $"Valid ticket — {k.Principal}  ({k.Detail})"
            : "No valid ticket. Enter a principal and click Get ticket.";

        _quotas.Children.Clear();
        if (report.Quotas.Count == 0)
            _quotas.Children.Add(new TextBlock
            {
                Text = "No usage data yet — mount a volume or ensure the remote paths are reachable "
                       + "(a Kerberos ticket lets remote quota commands run without a password).",
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
            });
        foreach (var q in report.Quotas)
            _quotas.Children.Add(QuotaBar(q));

        _status.Text = "Up to date.";
    }

    private static Control QuotaBar(QuotaInfo q)
    {
        var pct = q.Percent ?? 0;
        var colour = pct >= 90 ? Color.Parse("#E05252") : pct >= 75 ? Color.Parse("#FFB454") : Color.Parse("#2BC5A8");
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = $"{q.Label} — {q.Path}", FontSize = 12, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = q.Describe(), FontSize = 12 },
                new ProgressBar { Minimum = 0, Maximum = 100, Value = pct, Height = 8,
                                  Foreground = new SolidColorBrush(colour) },
            },
        };
    }
}
