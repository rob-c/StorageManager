using StorageManager.Errors;

namespace StorageManager.Mounting;

public interface IMounter
{
    /// <summary>User-facing name of the mount target, e.g. "S:" or "/home/alice/S".</summary>
    string TargetDescription { get; }

    /// <summary>True while the sshfs process is alive and the target is present.</summary>
    bool IsMounted { get; }

    /// <summary>Returns null when ready to mount, otherwise a problem plus optional remediation.</summary>
    PreflightResult? Preflight();

    /// <summary>Returns null on success, otherwise a user-facing error message.</summary>
    Task<string?> MountAsync(string username, string password);

    Task UnmountAsync();

    void OpenInFileManager();
}
