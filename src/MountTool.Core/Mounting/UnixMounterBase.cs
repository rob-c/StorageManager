using MountTool.Errors;

namespace MountTool.Mounting;

public abstract class UnixMounterBase(Config config) : MounterBase(config)
{
    private bool _createdTarget;

    protected string Target => Config.MountTarget
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "S");

    public override string TargetDescription => Target;

    protected override string MountTarget => Target;

    protected abstract string InstallGuidance { get; }

    /// <summary>A single shell command the user can copy to install sshfs, if one applies.</summary>
    protected virtual string? InstallCommand => null;

    /// <summary>True when the target directory is currently a mount point.</summary>
    protected abstract bool IsMountPoint();

    public override PreflightResult? Preflight()
    {
        if (FindSshfs() is null)
        {
            var fix = InstallCommand is { } cmd
                ? new FixAction("Copy install command", FixKindUi.CopyCommand, cmd)
                : null;
            return new PreflightResult($"sshfs was not found.\n\n{InstallGuidance}", fix);
        }

        if (IsMountPoint())
            return new PreflightResult($"{Target} is already a mount point. Unmount it first.");

        if (Directory.Exists(Target) && Directory.EnumerateFileSystemEntries(Target).Any())
            return new PreflightResult($"{Target} exists and is not empty. Move its contents aside first.");

        return null;
    }

    protected override void PrepareTarget()
    {
        _createdTarget = !Directory.Exists(Target);
        Directory.CreateDirectory(Target);
    }

    protected override void CleanupTarget()
    {
        try
        {
            if (_createdTarget && Directory.Exists(Target) && !IsMountPoint()
                && !Directory.EnumerateFileSystemEntries(Target).Any())
                Directory.Delete(Target);
        }
        catch
        {
            // Leave the empty directory in place.
        }

        _createdTarget = false;
    }

    protected override bool TargetPresent() => IsMountPoint();
}
