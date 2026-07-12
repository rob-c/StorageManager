namespace StorageManager.Gui;

/// <summary>
/// Everything needed to re-establish the last successful mount without asking
/// the user to re-enter anything except the password.
/// </summary>
public sealed record ReconnectContext(
    HostEntry Host,
    string RemotePath,
    string Target,
    string Username,
    bool ReadOnly,
    string? JumpHost = null,
    bool UseGssapi = false);
