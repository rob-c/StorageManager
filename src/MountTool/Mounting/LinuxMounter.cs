namespace MountTool.Mounting;

public sealed class LinuxMounter(Config config) : UnixMounterBase(config)
{
    protected override string InstallGuidance =>
        "Install it with your package manager, e.g.:\n" +
        "  sudo apt install sshfs      (Debian/Ubuntu)\n" +
        "  sudo dnf install fuse-sshfs (Fedora/RHEL)";

    protected override string? FindSshfs() => FindOnPath("sshfs", "/usr/bin", "/usr/local/bin");

    protected override bool IsMountPoint()
    {
        try
        {
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Target));
            return File.ReadLines("/proc/mounts")
                .Select(line => line.Split(' '))
                .Any(fields => fields.Length > 1 && UnescapeMountPath(fields[1]) == target);
        }
        catch
        {
            return false;
        }
    }

    // /proc/mounts octal-escapes spaces and other specials, e.g. "\040" for space.
    private static string UnescapeMountPath(string path) =>
        path.Replace(@"\040", " ").Replace(@"\011", "\t").Replace(@"\012", "\n").Replace(@"\134", @"\");

    protected override Task PlatformUnmountAsync() => Task.Run(() =>
    {
        if (RunQuiet("fusermount", "-u", Target) == 0)
            return;
        if (RunQuiet("fusermount3", "-u", Target) == 0)
            return;
        RunQuiet("fusermount", "-uz", Target);
    });

    public override void OpenInFileManager() => OpenPath("xdg-open", Target);
}
