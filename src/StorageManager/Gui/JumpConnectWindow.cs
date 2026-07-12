using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using StorageManager.Connection;
using StorageManager.Errors;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace StorageManager.Gui;

/// <summary>
/// Connect to a final target through a jump host, with Kerberos-delegated auth and
/// a shared control socket. Kept separate from the direct-mount window so the two
/// connection models don't entangle. Unix-only (SSHFS jump mounts ride the Unix
/// ssh/ControlMaster stack). Polls the 15s watchdog and reflects its state.
/// </summary>
public sealed class JumpConnectWindow : Window
{
    private static readonly IBrush LedIdle = new SolidColorBrush(Color.Parse("#8A8F98"));
    private static readonly IBrush LedBusy = new SolidColorBrush(Color.Parse("#FFB454"));
    private static readonly IBrush LedOk = new SolidColorBrush(Color.Parse("#2BC5A8"));
    private static readonly IBrush LedErr = new SolidColorBrush(Color.Parse("#E05252"));

    private readonly Config _config;
    private readonly JumpConnector _connector;
    private readonly DispatcherTimer _timer;

    private readonly TextBox _target = new() { Watermark = "cplab175.ph.ed.ac.uk", Width = 260 };
    private readonly ComboBox _jump = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _user = new() { Text = Environment.UserName, Width = 260 };
    private readonly TextBox _mount = new() { Width = 260 };
    private readonly TextBox _password = new() { PasswordChar = '●', Width = 260 };
    private readonly Ellipse _led = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Text = "Not connected.", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button _connect = new() { Content = "Connect", MinWidth = 110, Classes = { "accent" } };
    private readonly Button _disconnect = new() { Content = "Disconnect", MinWidth = 110, IsEnabled = false };

    private bool _connected;
    private bool _ticking;
    private bool _closing;

    public JumpConnectWindow(Config config)
    {
        _config = config;
        _connector = new JumpConnector(config);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => _ = OnTick();

        Title = "Jump-host connect";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("AppWindowBackground"));

        foreach (var j in config.JumpHostList)
            _jump.Items.Add(j);
        _jump.SelectedIndex = 0;
        _mount.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "S-remote");

        _connect.Click += async (_, _) => await OnConnect();
        _disconnect.Click += async (_, _) => await OnDisconnect();

        Content = new StackPanel
        {
            Margin = new Thickness(22),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Connect through a jump host", FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "Signs in with Kerberos, keeps a shared SSH socket open (your own ssh reuses it), " +
                           "and reconnects automatically while your ticket is valid.",
                    FontSize = 12, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#9AA3AE")),
                },
                Row("Final target", _target),
                Row("Jump host", _jump),
                Row("Username", _user),
                Row("Mount location", _mount),
                Row("Password", _password),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 9, Margin = new Thickness(0, 6, 0, 0),
                    Children = { _led, _status },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 0),
                    Children = { _connect, _disconnect },
                },
                new TextBlock
                {
                    Text = Support.Line, FontSize = 11, Margin = new Thickness(0, 6, 0, 0),
                    Foreground = new SolidColorBrush(Color.Parse("#8A8F98")),
                },
            },
        };

        _led.Fill = LedIdle;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Don't let the window close out from under an in-flight teardown; cancel,
        // finish unmount + socket exit, then close for real.
        if (!_connected || _closing)
            return;
        e.Cancel = true;
        _closing = true;
        _ = TeardownThenClose();
    }

    private async Task TeardownThenClose()
    {
        _timer.Stop();
        try { await _connector.DisconnectAsync(); } catch { /* best effort */ }
        _connected = false;
        Close();
    }

    private static Control Row(string label, Control input) => new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
            input,
        },
    };

    private void SetForm(bool enabled)
    {
        _target.IsEnabled = _jump.IsEnabled = _user.IsEnabled = _mount.IsEnabled = _password.IsEnabled = enabled;
        _connect.IsEnabled = enabled;
        _disconnect.IsEnabled = !enabled && _connected;
    }

    private async Task OnConnect()
    {
        var target = _target.Text?.Trim() ?? "";
        var user = _user.Text?.Trim() ?? "";
        var mount = _mount.Text?.Trim() ?? "";
        var jump = _jump.SelectedItem as string ?? "";
        var password = _password.Text ?? "";

        if (target.Length == 0 || user.Length == 0 || mount.Length == 0 || password.Length == 0)
        {
            Set(LedErr, "Fill in the target, username, mount location, and password.");
            return;
        }

        if (_connector.KerberosPreflight() is { } problem)
        {
            await PreflightDialog.ShowAsync(this, problem);
            return;
        }

        SetForm(false);
        Set(LedBusy, "Connecting through " + jump + "…");
        _password.Text = "";

        // The remote path is the target's home; users can mount a subfolder by
        // typing a fuller target path is out of scope here — default to home.
        var remotePath = ".";
        var outcome = await _connector.ConnectAsync(target, user, remotePath, mount, jump, password);

        if (!outcome.Success)
        {
            _connected = false;
            SetForm(true);
            Set(LedErr, outcome.Error ?? "Connection failed.");
            return;
        }

        _connected = true;
        _timer.Start();
        SetForm(false);
        _disconnect.IsEnabled = true;
        Set(LedOk, $"Connected as {user} on {mount} " +
                   $"({(outcome.UsedKerberos ? "Kerberos" : "password")}) — SSH socket shared with your shell.");
    }

    private async Task OnDisconnect()
    {
        _timer.Stop();
        Set(LedBusy, "Disconnecting…");
        await _connector.DisconnectAsync();
        _connected = false;
        SetForm(true);
        Set(LedIdle, "Disconnected.");
    }

    private async Task OnTick()
    {
        if (!_connected || _ticking)
            return; // never let a slow tick overlap the next one
        _ticking = true;
        try
        {
            await RunTick();
        }
        catch
        {
            // A transient tick failure shouldn't crash the app; the next tick retries.
        }
        finally
        {
            _ticking = false;
        }
    }

    private async Task RunTick()
    {
        var result = await _connector.TickAsync();
        switch (result)
        {
            case MonitorTickResult.Watching:
                Set(LedBusy, "Connection unstable — watching…");
                break;
            case MonitorTickResult.Reconnecting:
                Set(LedBusy, "Connection dropped — reconnecting…");
                break;
            case MonitorTickResult.ReconnectSucceeded:
                Set(LedOk, "Reconnected.");
                break;
            case MonitorTickResult.NeedsManualReconnect:
                _timer.Stop();
                _connected = false;
                SetForm(true);
                Set(LedErr, "Connection lost and the socket was dropped. Enter your password and click Connect.");
                break;
        }
    }

    private void Set(IBrush led, string text)
    {
        _led.Fill = led;
        _status.Text = text;
    }
}
