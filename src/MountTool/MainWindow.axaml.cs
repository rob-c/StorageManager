using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MountTool.Mounting;

namespace MountTool;

public partial class MainWindow : Window
{
    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9._-]+$");

    private readonly IMounter? _mounter;
    private readonly string? _startupError;
    private bool _connected;
    private bool _closeApproved;

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            var config = Config.Load();
            _mounter =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsMounter(config) :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new MacMounter(config) :
                new LinuxMounter(config);

            OpenButton.Content = $"Open {_mounter.TargetDescription}";
        }
        catch (Exception ex)
        {
            _startupError = ex.Message;
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

    private async void OnConnect(object? sender, RoutedEventArgs e)
    {
        if (_mounter is null)
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

        if (_mounter.Preflight() is { } problem)
        {
            await Dialogs.ShowMessageAsync(this, "PPE Storage", problem);
            return;
        }

        SetBusyUi("Connecting...");
        PasswordBox.Text = "";

        var error = await _mounter.MountAsync(username, password);

        if (error is not null)
        {
            SetDisconnectedUi("Connection failed.");
            await Dialogs.ShowMessageAsync(this, "PPE Storage", error);
            PasswordBox.Focus();
            return;
        }

        _connected = true;
        SetConnectedUi(username);
        _mounter.OpenInFileManager();
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
        SetDisconnectedUi("Connection was lost.");
    }

    private async void OnDisconnect(object? sender, RoutedEventArgs e)
    {
        if (_mounter is null)
            return;

        SetBusyUi("Disconnecting...");
        _connected = false;
        await _mounter.UnmountAsync();
        SetDisconnectedUi("Disconnected.");
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || _mounter is null)
            return;

        e.Cancel = true;

        if (_connected)
        {
            var close = await Dialogs.ConfirmAsync(this, "Disconnect PPE Storage?",
                $"Closing this window will disconnect {_mounter.TargetDescription}.\n\n" +
                "Close files opened from the storage before continuing.");
            if (!close)
                return;
        }

        SetBusyUi("Disconnecting...");
        _connected = false;
        await _mounter.UnmountAsync();
        _closeApproved = true;
        Close();
    }

    private void SetBusyUi(string status)
    {
        UsernameBox.IsEnabled = false;
        PasswordBox.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        OpenButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        StatusLabel.Text = status;
    }

    private void SetConnectedUi(string username)
    {
        UsernameBox.IsEnabled = false;
        PasswordBox.IsEnabled = false;
        ConnectButton.IsEnabled = false;
        OpenButton.IsEnabled = true;
        DisconnectButton.IsEnabled = true;
        StatusLabel.Text = $"Connected as {username} on {_mounter!.TargetDescription}";
        PasswordBox.Text = "";
    }

    private void SetDisconnectedUi(string status)
    {
        UsernameBox.IsEnabled = true;
        PasswordBox.IsEnabled = true;
        ConnectButton.IsEnabled = true;
        OpenButton.IsEnabled = false;
        DisconnectButton.IsEnabled = false;
        StatusLabel.Text = status;
        PasswordBox.Text = "";
    }
}
