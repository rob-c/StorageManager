using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StorageManager.Connection;
using StorageManager.Connectivity;
using StorageManager.Errors;
using StorageManager.Gui;
using StorageManager.Mounting;
using StorageManager.Settings;

namespace StorageManager;

public partial class MainWindow : Window
{
    private const string UserToken = "$USER";

    private static readonly IBrush LedIdle = new SolidColorBrush(Color.Parse("#8A8F98"));
    private static readonly IBrush LedBusy = new SolidColorBrush(Color.Parse("#FFB454"));
    private static readonly IBrush LedConnected = new SolidColorBrush(Color.Parse("#2BC5A8"));
    private static readonly IBrush LedError = new SolidColorBrush(Color.Parse("#E05252"));

    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9._-]+$");

    private static readonly string[] RemotePathTemplates =
    [
        $"/home/{UserToken}",
        $"/storage/datastore-personal/{UserToken}",
        "/storage/datastore-group/PPE",
        "/mnt/gridpp/poolhomes/PPEGroup",
        "/localdisk",
    ];

    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly Config? _baseConfig;
    private readonly string? _startupError;
    private readonly List<string> _remoteTemplates = [];
    private readonly SettingsStore _settingsStore = SettingsStore.Default;
    private UserSettings _saved = UserSettings.Empty;
    private IMounter? _mounter;
    private ReconnectContext? _reconnect;
    private bool _connected;
    private bool _closeApproved;
    private bool _suppressOtherHandler;
    private TrayController? _tray;
    private readonly DispatcherTimer _watchdog;
    private DateTime _lastHealthCheck = DateTime.MinValue;
    private string _connectedUser = "";
    private string _connectedTarget = "";
    private bool _connectedReadOnly = true;
    private JumpConnector? _jumpConnector;
    private bool _jumpTicking;
    private (HostEntry Host, string Jump, string User, string RemotePath, string Target, bool ReadOnly, bool UseKerberos)? _jumpReconnect;

    private const string NoJump = "(no jump host)";

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            _baseConfig = Config.Load();
        }
        catch (Exception ex)
        {
            _startupError = ex.Message;
        }

        if (_baseConfig is not null)
        {
            _saved = _settingsStore.Load();

            foreach (var host in _baseConfig.HostList)
                HostBox.Items.Add(host.Name);
            foreach (var custom in _saved.Hosts)
                if (!HostBox.Items.Contains(custom))
                    HostBox.Items.Add(custom);
            HostBox.Items.Add(OtherItem());
            SelectByText(HostBox, _saved.HostName, fallbackIndex: 0);

            RebuildRemoteOptions();
            InitializeMountTarget();
            PrefillFromSettings();

            UsernameBox.TextChanged += (_, _) => RefreshRemoteOptionTexts();
            HostBox.SelectionChanged += OnHostSelectionChanged;
            RemoteBox.SelectionChanged += OnRemoteSelectionChanged;
        }

        Opened += async (_, _) =>
        {
            if (_startupError is not null)
            {
                await Dialogs.ShowMessageAsync(this, "Storage Manager", _startupError);
                Close();
                return;
            }
            (string.IsNullOrEmpty(UsernameBox.Text) ? UsernameBox : PasswordBox).Focus();
        };

        Closing += OnClosing;

        SupportLabel.Text = Support.Line;

        // Optional jump host. Empty ("(no jump host)") gives a normal direct mount;
        // choosing a jump routes the mount through the gateway (needed for hosts
        // internal to the university network, e.g. cplab boxes). On Unix this also
        // keeps a shared control socket open; on Windows it rides SSHFS-Win's
        // ProxyJump with the password answered on both hops.
        if (_baseConfig is not null)
        {
            JumpBox.Items.Add(NoJump);
            foreach (var j in _baseConfig.JumpHostList)
                JumpBox.Items.Add(j);
            JumpBox.SelectedIndex = 0;
            ApplyDefaultJumpForHost();
        }

        // Kerberos sign-in is temporarily disabled: hide the tickbox, force it off,
        // and clear any previously-saved preference so nothing attempts Kerberos.
        // (Re-enable by restoring the settings-bound checkbox below.)
        KerberosCheck.IsChecked = false;
        KerberosCheck.IsVisible = false;
        if (_saved.UseKerberos)
        {
            _saved = _saved with { UseKerberos = false };
            _settingsStore.Save(_saved);
        }

        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _watchdog.Tick += OnWatchdogTick;
    }

    private static ComboBoxItem OtherItem() => new() { Content = "Other…" };

    private void PrefillFromSettings()
    {
        if (!string.IsNullOrEmpty(_saved.Username))
            UsernameBox.Text = _saved.Username;
        if (_saved.MountTarget is { Length: > 0 } target && !IsWindows)
            TargetBox.Text = target;
    }

    private static void SelectByText(ComboBox box, string? text, int fallbackIndex)
    {
        if (text is not null)
        {
            var idx = box.Items.IndexOf(text);
            if (idx >= 0) { box.SelectedIndex = idx; return; }
        }
        box.SelectedIndex = fallbackIndex;
    }

    private async void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (_jumpConnector is not null)
        {
            await JumpTick();
            return;
        }

        if (!_connected || _mounter is not { } mounter || mounter.IsMounted)
        {
            if (_connected)
                UpdateHealth();
            return;
        }

        _watchdog.Stop();
        _connected = false;
        _mounter = null;
        await mounter.UnmountAsync();
        SetDisconnectedUi("Connection was lost.", error: true);
        ReconnectButton.IsVisible = _reconnect is not null;
    }

    private void UpdateHealth()
    {
        if (!_connected || _connectedTarget.Length == 0
            || DateTime.UtcNow - _lastHealthCheck < TimeSpan.FromSeconds(60))
            return;
        _lastHealthCheck = DateTime.UtcNow;
        try
        {
            var root = IsWindows ? _connectedTarget + "\\" : _connectedTarget;
            var info = new DriveInfo(root);
            if (info.IsReady)
                StatusLabel.Text = $"Connected as {_connectedUser} on {_connectedTarget} " +
                                   $"({ModeLabel}) — {Human(info.AvailableFreeSpace)} free of {Human(info.TotalSize)}";
        }
        catch
        {
            // Leave the status line as-is if the mount can't be stat'd.
        }
    }

    private static string Human(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.#} {units[u]}";
    }

    private string ModeLabel => _connectedReadOnly ? "read-only" : "read-write";

    private HostEntry SelectedHost =>
        _baseConfig!.HostList.FirstOrDefault(h => h.Name == HostBox.SelectedItem as string)
            ?? new HostEntry(HostBox.SelectedItem as string ?? _baseConfig.HostList[0].Name, false);

    private async void OnHostSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressOtherHandler)
            return;
        if (HostBox.SelectedItem is ComboBoxItem)
        {
            var value = await Dialogs.PromptAsync(this, "Custom host",
                "Enter the hostname of the server to connect to:", "server.example.ac.uk");
            AddCustomValue(HostBox, value, isHost: true);
            return;
        }
        RebuildRemoteOptions();
    }

    private void OnRemoteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressOtherHandler)
            return;
        if (RemoteBox.SelectedItem is ComboBoxItem)
        {
            _ = PromptForCustomPath();
        }
    }

    private async Task PromptForCustomPath()
    {
        var value = await Dialogs.PromptAsync(this, "Custom folder",
            "Enter the absolute remote path to mount ($USER is substituted):", "/eos/project/…");
        AddCustomValue(RemoteBox, value, isHost: false);
    }

    private void AddCustomValue(ComboBox box, string? value, bool isHost)
    {
        _suppressOtherHandler = true;
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                box.SelectedIndex = 0;
                return;
            }

            var insertAt = box.Items.Count - 1; // before the "Other…" item
            if (!box.Items.Contains(value))
                box.Items.Insert(insertAt, value);
            box.SelectedItem = value;

            if (isHost)
            {
                var hosts = _saved.Hosts.Contains(value) ? _saved.Hosts : [.. _saved.Hosts, value];
                _saved = _saved with { CustomHosts = hosts };
            }
            else
            {
                _remoteTemplates.Add(value);
                var paths = _saved.Paths.Contains(value) ? _saved.Paths : [.. _saved.Paths, value];
                _saved = _saved with { CustomPaths = paths };
            }
            _settingsStore.Save(_saved);
        }
        finally
        {
            _suppressOtherHandler = false;
            if (isHost)
                RebuildRemoteOptions();
        }
    }

    private void RebuildRemoteOptions()
    {
        _suppressOtherHandler = true;
        try
        {
            _remoteTemplates.Clear();
            RemoteBox.Items.Clear();

            if (SelectedHost.RemotePaths is { Count: > 0 } hostPaths)
            {
                _remoteTemplates.AddRange(hostPaths);
            }
            else
            {
                _remoteTemplates.AddRange(RemotePathTemplates);

                var configured = _baseConfig!.RemotePath;
                if (!string.IsNullOrWhiteSpace(configured) && !_remoteTemplates.Contains(configured))
                    _remoteTemplates.Add(configured);
            }

            foreach (var custom in _saved.Paths)
                if (!_remoteTemplates.Contains(custom))
                    _remoteTemplates.Add(custom);

            foreach (var template in _remoteTemplates)
                RemoteBox.Items.Add(template);

            RemoteBox.Items.Add(OtherItem());

            var defaultIndex = _saved.RemotePathTemplate is { } t && _remoteTemplates.Contains(t)
                ? _remoteTemplates.IndexOf(t)
                : _remoteTemplates.IndexOf(_baseConfig!.RemotePath);
            RemoteBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : 0;
        }
        finally
        {
            _suppressOtherHandler = false;
        }
        RefreshRemoteOptionTexts();
        ApplyDefaultJumpForHost();
    }

    // An internal Physics host (*.ph.ed.ac.uk) that isn't itself a gateway can only be
    // reached through one, so default the jump host to student for those; anything else
    // (staff/student themselves, lxplus, Fermilab, custom) defaults to no jump.
    private void ApplyDefaultJumpForHost()
    {
        if (JumpBox.Items.Count == 0)
            return;

        var host = SelectedHost.Name;
        var needsGateway = host.EndsWith(".ph.ed.ac.uk", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("student.ph.ed.ac.uk", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("staff.ph.ed.ac.uk", StringComparison.OrdinalIgnoreCase);

        var target = needsGateway ? "student.ph.ed.ac.uk" : NoJump;
        var idx = JumpBox.Items.IndexOf(target);
        JumpBox.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private static string SubstituteUser(string template, string username) =>
        username.Length == 0 ? template
            : template.Replace($"{UserToken}1", username[..1]).Replace(UserToken, username);

    private void InitializeMountTarget()
    {
        if (IsWindows)
        {
            TargetBox.IsVisible = false;
            DriveBox.IsVisible = true;

            var used = Environment.GetLogicalDrives()
                .Select(d => d.TrimEnd('\\'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var preferred = _saved.MountTarget ?? _baseConfig!.MountTarget ?? "S:";
            string? firstFree = null;

            // A configured/remembered preferred drive outside the D–Z range still
            // offered if it is free (selectable string) or shown greyed if in use.
            if (!IsStandardDriveInRange(preferred))
                AddDrive(preferred, used, ref firstFree);

            foreach (var c in Enumerable.Range('D', 'Z' - 'D' + 1))
                AddDrive($"{(char)c}:", used, ref firstFree);

            // Select the preferred drive if it is free, otherwise the first free one.
            DriveBox.SelectedItem = used.Contains(preferred) ? firstFree : preferred;
        }
        else
        {
            TargetBox.Text = _baseConfig!.MountTarget
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "S");
        }
    }

    // Adds a drive to the picker: a selectable string when free, or a greyed,
    // non-selectable "X: (in use)" entry when the letter is already taken.
    private void AddDrive(string drive, HashSet<string> used, ref string? firstFree)
    {
        if (used.Contains(drive))
        {
            DriveBox.Items.Add(new ComboBoxItem { Content = $"{drive} (in use)", IsEnabled = false });
        }
        else
        {
            DriveBox.Items.Add(drive);
            firstFree ??= drive;
        }
    }

    private static bool IsStandardDriveInRange(string drive) =>
        drive.Length == 2 && drive[1] == ':'
        && char.ToUpperInvariant(drive[0]) is >= 'D' and <= 'Z';

    private void RefreshRemoteOptionTexts()
    {
        var username = UsernameBox.Text?.Trim() ?? "";
        var selected = RemoteBox.SelectedIndex;

        for (var i = 0; i < _remoteTemplates.Count && i < RemoteBox.Items.Count; i++)
        {
            var display = SubstituteUser(_remoteTemplates[i], username);
            if (!display.Equals(RemoteBox.Items[i]))
                RemoteBox.Items[i] = display;
        }

        RemoteBox.SelectedIndex = selected;
    }

    private static IMounter CreateMounter(Config config) =>
        IsWindows ? new WindowsMounter(config) :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new MacMounter(config) :
        new LinuxMounter(config);

    private async void OnConnect(object? sender, RoutedEventArgs e)
    {
        if (_baseConfig is null)
            return;

        var username = UsernameBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";

        if (username.Length == 0 || !UsernamePattern.IsMatch(username))
        {
            await Dialogs.ShowMessageAsync(this, "Storage Manager",
                username.Length == 0 ? "Enter your university username."
                                     : "The username contains unsupported characters.");
            UsernameBox.Focus();
            return;
        }

        if (password.Length == 0)
        {
            await Dialogs.ShowMessageAsync(this, "Storage Manager", "Enter your university password.");
            PasswordBox.Focus();
            return;
        }

        if (RemoteBox.SelectedItem is not string)
        {
            await Dialogs.ShowMessageAsync(this, "Storage Manager", "Choose a remote folder.");
            return;
        }

        var template = _remoteTemplates[Math.Max(0, RemoteBox.SelectedIndex)];
        var remotePath = SubstituteUser(template, username);

        var target = IsWindows
            ? DriveBox.SelectedItem as string
            : TargetBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(target) || (!IsWindows && !Path.IsPathRooted(target)))
        {
            await Dialogs.ShowMessageAsync(this, "Storage Manager",
                IsWindows ? "Choose a drive letter." : "Enter an absolute path for the mount location.");
            return;
        }

        var host = SelectedHost;
        var readOnly = ReadWriteCheck.IsChecked != true;

        if (SelectedJumpHost() is { } jump)
        {
            var useKerberos = KerberosCheck.IsChecked == true;
            if (!IsWindows)
            {
                // Unix: full jump stack (Kerberos option, shared ControlMaster socket).
                await ConnectViaJump(host, jump, username, remotePath, target, readOnly, useKerberos, password);
                return;
            }
            // Windows: SSHFS-Win handles ProxyJump directly; askpass answers both hops.
            await ConnectWith(host, remotePath, template, target, username, password, readOnly,
                jumpHost: jump, useGssapi: useKerberos);
            return;
        }

        await ConnectWith(host, remotePath, template, target, username, password, readOnly);
    }

    private string? SelectedJumpHost()
    {
        var j = JumpBox.SelectedItem as string;
        return string.IsNullOrEmpty(j) || j == NoJump ? null : j;
    }

    private async Task ConnectViaJump(
        HostEntry host, string jump, string user, string remotePath, string target,
        bool readOnly, bool useKerberos, string password)
    {
        var connector = new JumpConnector(_baseConfig!);
        // Kerberos tools are only a prerequisite when Kerberos sign-in is ticked;
        // the default password path needs no kinit/klist at all.
        if (useKerberos && connector.KerberosPreflight() is { } problem)
        {
            await PreflightDialog.ShowAsync(this, problem);
            return;
        }

        SetBusyUi($"Checking {jump}…");
        PasswordBox.Text = "";

        var probe = await GatewayProbe.CheckAsync(jump, 22, TimeSpan.FromSeconds(4));
        if (!probe.Reachable)
        {
            SetDisconnectedUi("Can't reach the jump host.", error: true);
            await Dialogs.ShowMessageAsync(this, "Storage Manager", probe.Message!);
            return;
        }

        SetBusyUi($"Connecting to {host.Name} via {jump}…");
        var outcome = await connector.ConnectAsync(
            host.Name, user, remotePath, target, jump, password, useKerberos, readOnly);

        if (!outcome.Success)
        {
            SetDisconnectedUi("Connection failed.", error: true);
            await Dialogs.ShowMessageAsync(this, "Storage Manager", outcome.Error ?? "Could not connect.");
            PasswordBox.Focus();
            return;
        }

        _mounter = null;
        _jumpConnector = connector;
        _connected = true;
        _connectedUser = user;
        _connectedTarget = target;
        _connectedReadOnly = readOnly;
        _jumpReconnect = (host, jump, user, remotePath, target, readOnly, useKerberos);
        _reconnect = null;

        _saved = _saved with { Username = user, HostName = host.Name, MountTarget = target };
        _settingsStore.Save(_saved);

        SetConnectedUi(user);
        StatusLabel.Text = $"Connected as {user} on {target} via {jump} " +
                           $"({ModeLabel}, {(outcome.UsedKerberos ? "Kerberos" : "password")}) — SSH socket shared.";
        OpenFolder(target);
    }

    private async Task JumpTick()
    {
        if (_jumpTicking || _jumpConnector is null)
            return;
        _jumpTicking = true;
        try
        {
            switch (await _jumpConnector.TickAsync())
            {
                case MonitorTickResult.Healthy:
                    UpdateHealth();
                    break;
                case MonitorTickResult.Watching:
                    StatusDot.Fill = LedBusy;
                    StatusLabel.Text = $"Connection unstable — watching… ({_connectedTarget})";
                    break;
                case MonitorTickResult.Reconnecting:
                    StatusDot.Fill = LedBusy;
                    StatusLabel.Text = "Connection dropped — reconnecting…";
                    break;
                case MonitorTickResult.ReconnectSucceeded:
                    StatusDot.Fill = LedConnected;
                    StatusLabel.Text = $"Reconnected as {_connectedUser} on {_connectedTarget}.";
                    break;
                case MonitorTickResult.NeedsManualReconnect:
                    _watchdog.Stop();
                    _connected = false;
                    await _jumpConnector.DisconnectAsync();
                    _jumpConnector = null;
                    SetDisconnectedUi("Connection lost and the socket was dropped.", error: true);
                    ReconnectButton.IsVisible = _jumpReconnect is not null;
                    break;
            }
        }
        catch
        {
            // A transient watchdog failure shouldn't crash the app; the next tick retries.
        }
        finally
        {
            _jumpTicking = false;
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            var info = OperatingSystem.IsMacOS()
                ? new System.Diagnostics.ProcessStartInfo("/usr/bin/open", path)
                : new System.Diagnostics.ProcessStartInfo("xdg-open", path);
            info.UseShellExecute = false;
            System.Diagnostics.Process.Start(info);
        }
        catch
        {
            // Non-fatal.
        }
    }

    private async Task ConnectWith(
        HostEntry host, string remotePath, string template, string target,
        string username, string password, bool readOnly,
        string? jumpHost = null, bool useGssapi = false)
    {
        var mounter = CreateMounter(_baseConfig! with
        {
            Gateway = host.Name,
            TwoFactorPam = host.TwoFactorPam,
            RemotePath = remotePath,
            MountTarget = target,
            ReadOnly = readOnly,
            JumpHost = jumpHost,
            UseGssapi = useGssapi,
        });

        if (mounter.Preflight() is { } problem)
        {
            if (problem.Blocking)
            {
                await PreflightDialog.ShowAsync(this, problem);
                return;
            }

            var proceed = await Dialogs.ConfirmAsync(this, "Storage Manager",
                $"{problem.Message}\n\nContinue and mount here anyway?");
            if (!proceed)
                return;
        }

        SetBusyUi("Checking connection…");
        PasswordBox.Text = "";

        // A jump target isn't directly reachable by design — probe the gateway instead.
        var probeHost = jumpHost ?? host.Name;
        var probe = await GatewayProbe.CheckAsync(probeHost, 22, TimeSpan.FromSeconds(4));
        if (!probe.Reachable)
        {
            SetDisconnectedUi(jumpHost is null ? "Can't reach the server." : "Can't reach the jump host.", error: true);
            await Dialogs.ShowMessageAsync(this, "Storage Manager", probe.Message!);
            return;
        }

        SetBusyUi("Connecting…");
        _mounter = mounter;

        var error = await mounter.MountAsync(username, password);

        if (error is not null)
        {
            _mounter = null;
            var friendly = ErrorTranslator.Translate(error, 1, host.TwoFactorPam);
            SetDisconnectedUi("Connection failed.", error: true);
            var body = friendly.Guidance.Length > 0
                ? $"{friendly.Headline}\n\n{friendly.Guidance}\n\nDetails:\n{friendly.Raw}"
                : $"{friendly.Headline}\n\nDetails:\n{friendly.Raw}";
            await Dialogs.ShowMessageAsync(this, "Storage Manager", body);
            PasswordBox.Focus();
            return;
        }

        _connected = true;
        _connectedUser = username;
        _connectedTarget = mounter.TargetDescription;
        _connectedReadOnly = readOnly;
        _jumpConnector = null;
        _jumpReconnect = null;
        _reconnect = new ReconnectContext(host, remotePath, target, username, readOnly, jumpHost, useGssapi);
        _saved = _saved with
        {
            Username = username,
            HostName = host.Name,
            RemotePathTemplate = template,
            MountTarget = target,
        };
        _settingsStore.Save(_saved);

        SetConnectedUi(username);
        if (jumpHost is not null)
            StatusLabel.Text = $"Connected as {username} on {_connectedTarget} via {jumpHost} ({ModeLabel})";
        mounter.OpenInFileManager();
    }

    private async void OnReconnect(object? sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Text ?? "";
        if (password.Length == 0)
        {
            await Dialogs.ShowMessageAsync(this, "Storage Manager", "Enter your university password to reconnect.");
            PasswordBox.Focus();
            return;
        }

        if (_jumpReconnect is { } jr)
        {
            ReconnectButton.IsVisible = false;
            await ConnectViaJump(jr.Host, jr.Jump, jr.User, jr.RemotePath, jr.Target, jr.ReadOnly, jr.UseKerberos, password);
            return;
        }

        if (_reconnect is not { } ctx)
            return;

        ReconnectButton.IsVisible = false;
        await ConnectWith(ctx.Host, ctx.RemotePath, _saved.RemotePathTemplate ?? ctx.RemotePath,
            ctx.Target, ctx.Username, password, ctx.ReadOnly, ctx.JumpHost, ctx.UseGssapi);
    }

    private void OnOpenDoctor(object? sender, RoutedEventArgs e) =>
        new DoctorWindow().ShowDialog(this);

    private void OnOpenVsCode(object? sender, RoutedEventArgs e) =>
        new VsCodeWindow().ShowDialog(this);

    private void OnOpenStatus(object? sender, RoutedEventArgs e) =>
        new StatusWindow(SelectedHost.Name, UsernameBox.Text?.Trim(),
            useKerberos: KerberosCheck.IsChecked == true).ShowDialog(this);

    private void OnNewConnection(object? sender, RoutedEventArgs e) =>
        new MainWindow().Show();

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_jumpConnector is not null)
        {
            OpenFolder(_connectedTarget);
            return;
        }

        if (_mounter is null)
            return;

        if (_mounter.IsMounted)
        {
            _mounter.OpenInFileManager();
            return;
        }

        _connected = false;
        await _mounter.UnmountAsync();
        _mounter = null;
        SetDisconnectedUi("Connection was lost.", error: true);
        ReconnectButton.IsVisible = _reconnect is not null;
    }

    private async void OnDisconnect(object? sender, RoutedEventArgs e)
    {
        if (_jumpConnector is { } jump)
        {
            SetBusyUi("Disconnecting...");
            _connected = false;
            await jump.DisconnectAsync();
            _jumpConnector = null;
            _jumpReconnect = null;
            SetDisconnectedUi("Disconnected.");
            return;
        }

        if (_mounter is null)
            return;

        SetBusyUi("Disconnecting...");
        _connected = false;
        await _mounter.UnmountAsync();
        _mounter = null;
        SetDisconnectedUi("Disconnected.");
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved)
            return;

        e.Cancel = true;

        if (_connected && _jumpConnector is { } jump)
        {
            var close = await Dialogs.ConfirmAsync(this, "Disconnect Storage Manager?",
                $"Closing this window will disconnect {_connectedTarget} and drop the SSH socket.\n\n" +
                "Close files opened from the storage before continuing.");
            if (!close)
                return;

            SetBusyUi("Disconnecting...");
            _connected = false;
            await jump.DisconnectAsync();
            _jumpConnector = null;
            _closeApproved = true;
            Close();
            return;
        }

        if (_connected && _mounter is not null)
        {
            var choice = await Dialogs.ChooseCloseAsync(this, "Close Storage Manager?",
                $"{_mounter.TargetDescription} is still connected. You can keep it mounted and " +
                "minimize to the system tray, or disconnect and close.\n\n" +
                "Close files opened from the storage before disconnecting.");

            if (choice == Dialogs.CloseChoice.Cancel)
                return;

            if (choice == Dialogs.CloseChoice.MinimizeToTray)
            {
                _tray ??= new TrayController(this);
                var minimized = _tray.MinimizeToTray(
                    _mounter.TargetDescription,
                    onRestore: RestoreFromTray,
                    onDisconnect: async () => { await DisconnectFromTray(); },
                    onQuit: async () => { await DisconnectFromTray(); ForceClose(); });
                if (minimized)
                    return; // stay resident in the tray
                // Tray unavailable: fall through to disconnect-and-close.
            }

            SetBusyUi("Disconnecting...");
            _connected = false;
            await _mounter.UnmountAsync();
            _mounter = null;
        }

        _closeApproved = true;
        Close();
    }

    private void RestoreFromTray()
    {
        _tray?.Remove();
        Show();
        Activate();
    }

    private async Task DisconnectFromTray()
    {
        _tray?.Remove();
        Show();
        if (_mounter is not null)
        {
            _connected = false;
            await _mounter.UnmountAsync();
            _mounter = null;
            SetDisconnectedUi("Disconnected.");
        }
    }

    private void ForceClose()
    {
        _closeApproved = true;
        Close();
    }

    private void SetFormEnabled(bool enabled)
    {
        UsernameBox.IsEnabled = enabled;
        PasswordBox.IsEnabled = enabled;
        HostBox.IsEnabled = enabled;
        RemoteBox.IsEnabled = enabled;
        TargetBox.IsEnabled = enabled;
        DriveBox.IsEnabled = enabled;
        JumpBox.IsEnabled = enabled;
        ReadWriteCheck.IsEnabled = enabled;
        KerberosCheck.IsEnabled = enabled;
    }

    private void SetBusyUi(string status)
    {
        SetFormEnabled(false);
        ConnectButton.IsEnabled = false;
        ReconnectButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        StatusLabel.Text = status;
        StatusDot.Fill = LedBusy;
        _watchdog.Stop();
    }

    private void SetConnectedUi(string username)
    {
        SetFormEnabled(false);
        ConnectButton.IsEnabled = false;
        ReconnectButton.IsVisible = false;
        OpenButton.IsEnabled = true;
        DisconnectButton.IsEnabled = true;
        OpenButton.Content = $"Open {_connectedTarget}";
        StatusLabel.Text = $"Connected as {username} on {_connectedTarget} ({ModeLabel})";
        StatusDot.Fill = LedConnected;
        PasswordBox.Text = "";
        _lastHealthCheck = DateTime.MinValue;
        _watchdog.Start();
    }

    private void SetDisconnectedUi(string status, bool error = false)
    {
        SetFormEnabled(true);
        ConnectButton.IsEnabled = true;
        ReconnectButton.IsEnabled = true;
        OpenButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        StatusLabel.Text = status;
        StatusDot.Fill = error ? LedError : LedIdle;
        PasswordBox.Text = "";
        _watchdog.Stop();
    }
}
