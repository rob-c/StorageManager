namespace MountTool.Mounting;

public sealed class LinuxMounter(Config config) : UnixMounterBase(config)
{
    protected override string InstallGuidance =>
        "Install it with your package manager, e.g.:\n" +
        "  sudo apt install sshfs      (Debian/Ubuntu)\n" +
        "  sudo dnf install fuse-sshfs (Fedora/RHEL)";

    protected override string? InstallCommand => "sudo apt install sshfs";

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

    private static readonly string[] KnownFileManagers =
        ["dolphin", "nautilus", "thunar", "nemo", "pcmanfm", "caja"];

    public override void OpenInFileManager()
    {
        // The desktop's inode/directory association can point at a text
        // editor (e.g. kate), so only honour it when it names a real file
        // manager; otherwise fall back through known managers.
        var handler = QueryDefaultDirectoryHandler();
        if (handler is not null
            && KnownFileManagers.Any(m => handler.Contains(m, StringComparison.OrdinalIgnoreCase))
            && FindOnPath("gtk-launch") is not null)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("gtk-launch") { UseShellExecute = false };
                info.ArgumentList.Add(handler);
                info.ArgumentList.Add(Target);
                System.Diagnostics.Process.Start(info);
                return;
            }
            catch
            {
                // Fall through to the known managers.
            }
        }

        foreach (var manager in KnownFileManagers)
        {
            if (FindOnPath(manager) is { } path)
            {
                OpenPath(path, Target);
                return;
            }
        }

        // Desktop's default folder opener. Never Windows explorer.exe, even
        // under WSL — this is a Linux desktop tool.
        if (FindOnPath("gio") is { } gio)
        {
            OpenPath(gio, "open", Target);
            return;
        }

        OpenPath("xdg-open", Target);
    }

    private static string? QueryDefaultDirectoryHandler()
    {
        var output = QueryProcessOutput("xdg-mime", "query", "default", "inode/directory");
        return output is not null && output.EndsWith(".desktop") ? output : null;
    }

    private static string? QueryProcessOutput(string fileName, params string[] arguments)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            foreach (var arg in arguments)
                info.ArgumentList.Add(arg);

            using var process = System.Diagnostics.Process.Start(info);
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return output;
        }
        catch
        {
            return null;
        }
    }
}
