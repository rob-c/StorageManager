using System.Diagnostics;
using System.Text;
using StorageManager.Errors;

namespace StorageManager.Mounting;

public abstract class MounterBase(Config config) : IMounter
{
    // Two-factor logins wait on a human answering a PAM challenge.
    private TimeSpan MountTimeout =>
        Config.TwoFactorPam ? TimeSpan.FromSeconds(120) : TimeSpan.FromSeconds(20);

    protected Config Config { get; } = config;
    protected Process? SshfsProcess { get; private set; }

    private Task<string>? _stdout;
    private Task<string>? _stderr;

    public abstract string TargetDescription { get; }

    public bool IsMounted =>
        SshfsProcess is { HasExited: false } && TargetPresent();

    public abstract PreflightResult? Preflight();
    public abstract void OpenInFileManager();

    /// <summary>Full path to the sshfs executable, or null if not installed.</summary>
    protected abstract string? FindSshfs();

    /// <summary>True when the drive letter / mount point is live.</summary>
    protected abstract bool TargetPresent();

    /// <summary>The sshfs mount target argument ("S:" or an absolute directory).</summary>
    protected abstract string MountTarget { get; }

    protected virtual IEnumerable<string> ExtraSshfsArguments => [];
    protected virtual void ConfigureEnvironment(ProcessStartInfo startInfo) { }
    protected virtual void PrepareTarget() { }
    protected virtual void CleanupTarget() { }

    /// <summary>Platform-specific unmount of a live mount; killing the process is handled here.</summary>
    protected virtual Task PlatformUnmountAsync() => Task.CompletedTask;

    public async Task<string?> MountAsync(string username, string password)
    {
        var sshfs = FindSshfs();
        if (sshfs is null)
            return Preflight()?.Message ?? "sshfs was not found.";

        PrepareTarget();

        var startInfo = new ProcessStartInfo
        {
            FileName = sshfs,
            WorkingDirectory = Path.GetDirectoryName(sshfs) ?? "",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in BuildArguments(username))
            startInfo.ArgumentList.Add(arg);

        ConfigureEnvironment(startInfo);

        if (UsesAskpass)
        {
            // ssh re-invokes this executable for every prompt (see Askpass):
            // the password prompt is answered from the environment; PAM
            // challenges (e.g. CERN 2FA) surface as a dialog. Jump mounts also
            // use this so the same password answers both the jump and the target.
            startInfo.EnvironmentVariables[Askpass.ModeVariable] = "1";
            startInfo.EnvironmentVariables[Askpass.PasswordVariable] = password;
            startInfo.EnvironmentVariables["SSH_ASKPASS"] = AskpassExecutablePath();
            startInfo.EnvironmentVariables["SSH_ASKPASS_REQUIRE"] = "force";
            // Older ssh only consults SSH_ASKPASS when DISPLAY is set.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                startInfo.EnvironmentVariables["DISPLAY"] = ":0";
            startInfo.EnvironmentVariables["STORAGEMANAGER_DEBUG"] = "1";
            Askpass.DebugLog($"mount: exe={startInfo.FileName} askpass={AskpassExecutablePath()} " +
                             $"args=[{string.Join(" ", startInfo.ArgumentList)}]");
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("sshfs did not start.");
        }
        catch (Exception ex)
        {
            return $"sshfs could not be started.\n\n{ex.Message}";
        }

        SshfsProcess = process;

        // Drain output immediately so neither pipe can block sshfs.
        _stdout = process.StandardOutput.ReadToEndAsync();
        _stderr = process.StandardError.ReadToEndAsync();

        if (!UsesAskpass)
        {
            // The password travels via stdin only.
            await process.StandardInput.WriteLineAsync(password);
            await process.StandardInput.FlushAsync();
        }
        process.StandardInput.Close();

        var deadline = DateTime.UtcNow + MountTimeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.HasExited)
                break;
            if (TargetPresent())
                return null;
            await Task.Delay(200);
        }

        var exitCode = 1;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
            exitCode = process.ExitCode;
        }
        catch
        {
            // Best effort.
        }

        var diagnostics = await CollectOutputAsync();
        if (UsesAskpass)
            Askpass.DebugLog($"mount failed: exit={exitCode} diagnostics=[{diagnostics}]");
        await UnmountAsync();

        return $"The storage could not be mounted.\n\nsshfs exit code: {exitCode}\n\n{diagnostics}";
    }

    public async Task UnmountAsync()
    {
        var process = SshfsProcess;
        SshfsProcess = null;

        if (process is not null)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited)
                {
                    await PlatformUnmountAsync();
                    process.Refresh();
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
            }
            catch
            {
                // It may already have exited.
            }
            finally
            {
                process.Dispose();
            }
        }

        // Give the FUSE layer a moment to release the target.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (TargetPresent() && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        _stdout = _stderr = null;
        CleanupTarget();
    }

    /// <summary>Password prompts are answered via SSH_ASKPASS rather than stdin: for 2FA
    /// (PAM challenges need a dialog) and for jump mounts (the ProxyJump hop's ssh
    /// cannot read sshfs's stdin, but it inherits the askpass environment).</summary>
    protected bool UsesAskpass => Config.TwoFactorPam || Config.JumpHost is not null;

    /// <summary>The ssh options that route the connection through the jump host. The
    /// default uses OpenSSH's ProxyJump (works where a real /bin/sh runs the proxy);
    /// Windows overrides this because SSHFS-Win's shell can't run ProxyJump's proxy.</summary>
    protected virtual IEnumerable<string> ProxyArguments(string jumpHost, string username) =>
        ["-o", $"ProxyJump={jumpHost}"];

    internal List<string> BuildArguments(string username) =>
    [
        $"{username}@{Config.Gateway}:{Config.RemotePath}",
        MountTarget,
        "-f",
        // Read-only by default (safety): sshfs honours -o ro, so the FUSE mount
        // rejects writes. Read-write is an explicit, opt-in choice in the UI.
        .. Config.ReadOnly ? new[] { "-o", "ro" } : [],
        // Route through the SSH jump/gateway when configured (cplab boxes etc.).
        .. Config.JumpHost is { } jump ? ProxyArguments(jump, username) : [],
        .. AuthArguments(),
        // 2FA needs multiple keyboard-interactive rounds; a jump needs a prompt
        // per hop. Extra prompts are harmless (same answer is redelivered).
        "-o", $"NumberOfPasswordPrompts={(Config.TwoFactorPam ? 6 : Config.JumpHost is not null ? 4 : 1)}",
        // A jump is a double hop (authenticate the gateway, then reach the target
        // through it), so give it much longer to complete the banner exchange.
        "-o", $"ConnectTimeout={(Config.JumpHost is not null ? 45 : 10)}",
        // No "-o reconnect": when the server goes away the ssh transport must
        // exit (after ServerAlive gives up) so the watchdog can unmount cleanly.
        "-o", "StrictHostKeyChecking=accept-new",
        "-o", $"ServerAliveInterval={Config.KeepAliveIntervalSeconds}",
        "-o", $"ServerAliveCountMax={Config.KeepAliveCountMax}",
        .. ExtraSshfsArguments,
    ];

    // No comma in any -o value: Linux FUSE splits comma-separated -o options, so
    // e.g. "keyboard-interactive,password" would leak a bogus "password" option.
    private IEnumerable<string> AuthArguments()
    {
        if (Config.TwoFactorPam)
            return ["-o", "PreferredAuthentications=keyboard-interactive", "-o", "PubkeyAuthentication=no"];

        if (Config.JumpHost is not null)
            return Config.UseGssapi
                // GSSAPI first (ssh's default preference order tries it before
                // password, which askpass then answers as the fallback).
                ? ["-o", "GSSAPIAuthentication=yes", "-o", "GSSAPIDelegateCredentials=yes",
                   "-o", "PubkeyAuthentication=no"]
                : ["-o", "PreferredAuthentications=password", "-o", "PubkeyAuthentication=no"];

        return ["-o", "password_stdin", "-o", "PreferredAuthentications=password",
                "-o", "PubkeyAuthentication=no"];
    }

    private async Task<string> CollectOutputAsync()
    {
        var text = new StringBuilder();
        foreach (var task in new[] { _stderr, _stdout })
        {
            if (task is null)
                continue;
            try
            {
                text.AppendLine((await task).Trim());
            }
            catch
            {
                // Pipe already broken.
            }
        }

        var message = text.ToString().Trim();
        if (message.Length == 0)
            return "sshfs produced no diagnostic output.";
        if (message.Length > 1800)
            message = "[Earlier output omitted]\n\n" + message[^1800..];
        return message;
    }

    private static string AskpassExecutablePath()
    {
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the executable path for SSH_ASKPASS.");
        // SSHFS-Win's cygwin ssh copes better with forward slashes.
        return OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
    }

    protected static string? FindOnPath(string name, params string[] extraDirectories)
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(extraDirectories);

        return directories
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);
    }

    protected static int RunQuiet(string fileName, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(fileName) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in arguments)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            process?.WaitForExit(10000);
            return process?.ExitCode ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    protected static void OpenPath(string opener, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(opener) { UseShellExecute = false };
            foreach (var arg in arguments)
                info.ArgumentList.Add(arg);
            Process.Start(info);
        }
        catch
        {
            // Non-fatal.
        }
    }
}
