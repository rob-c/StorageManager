namespace MountTool.Mounting;

public abstract class UnixMounterBase(Config config) : MounterBase(config)
{
    private bool _createdTarget;

    protected string Target => Config.MountTarget
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "S");

    public override string TargetDescription => Target;

    protected override string MountTarget => Target;

    protected abstract string InstallGuidance { get; }

    /// <summary>True when the target directory is currently a mount point.</summary>
    protected abstract bool IsMountPoint();

    public override string? Preflight()
    {
        if (FindSshfs() is null)
            return $"sshfs was not found.\n\n{InstallGuidance}";

        if (IsMountPoint())
            return $"{Target} is already a mount point. Unmount it first.";

        if (Directory.Exists(Target) && Directory.EnumerateFileSystemEntries(Target).Any())
            return $"{Target} exists and is not empty. Move its contents aside first.";

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
