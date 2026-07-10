using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MountTool.Mounting;

namespace MountTool;

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
    ];

    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly Config? _baseConfig;
    private readonly string? _startupError;
    private readonly List<string> _remoteTemplates = [];
    private IMounter? _mounter;
    private bool _connected;
    private bool _closeApproved;

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
            InitializeRemoteOptions();
            InitializeMountTarget();
            UsernameBox.TextChanged += (_, _) => RefreshRemoteOptionTexts();
        }

        Opened += async (_, _) =>
        {
            if (_startupError is not null)
            {
                await Dialogs.ShowMessageAsync(this, "PPE Storage", _startupError);
                Close();
                return;
            }
            UsernameBox.Focus();
        };

        Closing += OnClosing;
    }

    private void InitializeRemoteOptions()
    {
        _remoteTemplates.AddRange(RemotePathTemplates);

        // A remotePath from mount-config.json outside the standard list becomes
        // an extra option and the default selection.
        var configured = _baseConfig!.RemotePath;
        if (!string.IsNullOrWhiteSpace(configured) && !_remoteTemplates.Contains(configured))
            _remoteTemplates.Add(configured);

        foreach (var template in _remoteTemplates)
            RemoteBox.Items.Add(template);

        RemoteBox.Items.Add(new ComboBoxItem { Content = "Other…", IsEnabled = false });

        var defaultIndex = _remoteTemplates.IndexOf(configured);
        RemoteBox.SelectedIndex = defaultIndex >= 0 ? defaultIndex : _remoteTemplates.Count - 1;
    }

    private void InitializeMountTarget()
    {
        if (IsWindows)
        {
            TargetBox.IsVisible = false;
            DriveBox.IsVisible = true;

            var used = Environment.GetLogicalDrives()
                .Select(d => d.TrimEnd('\\'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var free = Enumerable.Range('D', 'Z' - 'D' + 1)
                .Select(c => $"{(char)c}:")
                .Where(d => !used.Contains(d))
                .ToList();

            var preferred = _baseConfig!.MountTarget ?? "S:";
            if (!free.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                free.Insert(0, preferred);

            foreach (var drive in free)
                DriveBox.Items.Add(drive);

            DriveBox.SelectedItem = free.First(d => d.Equals(preferred, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            TargetBox.Text = _baseConfig!.MountTarget
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "S");
        }
    }

    private void RefreshRemoteOptionTexts()
    {
        var username = UsernameBox.Text?.Trim() ?? "";
        var selected = RemoteBox.SelectedIndex;

        for (var i = 0; i < _remoteTemplates.Count; i++)
        {
            var display = username.Length == 0
                ? _remoteTemplates[i]
                : _remoteTemplates[i].Replace(UserToken, username);
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
            await Dialogs.ShowMessageAsync(this, "PPE Storage",
                username.Length == 0 ? "Enter your university username."
                                     : "The username contains unsupported characters.");
            UsernameBox.Focus();
            return;
        }

        if (password.Length == 0)
        {
            await Dialogs.ShowMessageAsync(this, "PPE Storage", "Enter your university password.");
            PasswordBox.Focus();
            return;
        }

        var remotePath = _remoteTemplates[Math.Max(0, RemoteBox.SelectedIndex)]
            .Replace(UserToken, username);

        var target = IsWindows
            ? DriveBox.SelectedItem as string
            : TargetBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(target) || (!IsWindows && !Path.IsPathRooted(target)))
        {
            await Dialogs.ShowMessageAsync(this, "PPE Storage",
                IsWindows ? "Choose a drive letter." : "Enter an absolute path for the mount location.");
            return;
        }

        var mounter = CreateMounter(_baseConfig with { RemotePath = remotePath, MountTarget = target });

        if (mounter.Preflight() is { } problem)
        {
            await Dialogs.ShowMessageAsync(this, "PPE Storage", problem);
            return;
        }

        SetBusyUi("Connecting...");
        PasswordBox.Text = "";
        _mounter = mounter;

        var error = await mounter.MountAsync(username, password);

        if (error is not null)
        {
            _mounter = null;
            SetDisconnectedUi("Connection failed.", error: true);
            await Dialogs.ShowMessageAsync(this, "PPE Storage", error);
            PasswordBox.Focus();
            return;
        }

        _connected = true;
        SetConnectedUi(username);
        mounter.OpenInFileManager();
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
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
    }

    private async void OnDisconnect(object? sender, RoutedEventArgs e)
    {
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

        if (_connected && _mounter is not null)
        {
            var close = await Dialogs.ConfirmAsync(this, "Disconnect PPE Storage?",
                $"Closing this window will disconnect {_mounter.TargetDescription}.\n\n" +
                "Close files opened from the storage before continuing.");
            if (!close)
                return;

            SetBusyUi("Disconnecting...");
            _connected = false;
            await _mounter.UnmountAsync();
            _mounter = null;
        }

        _closeApproved = true;
        Close();
    }

    private void SetFormEnabled(bool enabled)
    {
        UsernameBox.IsEnabled = enabled;
        PasswordBox.IsEnabled = enabled;
        RemoteBox.IsEnabled = enabled;
        TargetBox.IsEnabled = enabled;
        DriveBox.IsEnabled = enabled;
    }

    private void SetBusyUi(string status)
    {
        SetFormEnabled(false);
        ConnectButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        StatusLabel.Text = status;
        StatusDot.Fill = LedBusy;
    }

    private void SetConnectedUi(string username)
    {
        SetFormEnabled(false);
        ConnectButton.IsEnabled = false;
        OpenButton.IsEnabled = true;
        DisconnectButton.IsEnabled = true;
        OpenButton.Content = $"Open {_mounter!.TargetDescription}";
        StatusLabel.Text = $"Connected as {username} on {_mounter.TargetDescription}";
        StatusDot.Fill = LedConnected;
        PasswordBox.Text = "";
    }

    private void SetDisconnectedUi(string status, bool error = false)
    {
        SetFormEnabled(true);
        ConnectButton.IsEnabled = true;
        OpenButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        StatusLabel.Text = status;
        StatusDot.Fill = error ? LedError : LedIdle;
        PasswordBox.Text = "";
    }
}
