using System.Reflection;
using Avalonia;
using StorageManager.Cli;
using StorageManager.Tui;

namespace StorageManager;

internal static class Program
{
    /// <summary>Product version, from the assembly (stamped by CI to the release tag).</summary>
    public static string Version
    {
        get
        {
            var info = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            // InformationalVersion may carry +<commit> build metadata — trim it.
            var trimmed = info?.Split('+')[0];
            return string.IsNullOrWhiteSpace(trimmed)
                ? typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"
                : trimmed;
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        // When ssh (spawned by our sshfs child with SSH_ASKPASS pointing back
        // at this executable) invokes us for an authentication prompt, the
        // marker variable it inherited routes us into askpass mode. This must
        // stay first: the child re-invokes this same binary.
        if (Environment.GetEnvironmentVariable(Askpass.ModeVariable) == "1")
        {
            // A TUI parent marks the environment so 2FA challenges use the
            // terminal instead of trying to pop an Avalonia dialog on a headless box.
            if (Environment.GetEnvironmentVariable("STORAGEMANAGER_ASKPASS_UI") != "tui")
                Askpass.ChallengeHandler = Gui.AskpassDialog.Prompt;
            return Askpass.Run(args.FirstOrDefault() ?? "");
        }

        return ResolveMode(args) switch
        {
            LaunchMode.Version => PrintVersion(),
            LaunchMode.Doctor => DoctorCli.Run(args),
            LaunchMode.VsCode => VsCodeCliCommand.Run(args),
            LaunchMode.Status => StatusCli.Run(args),
            LaunchMode.Diagnostics => PrintDiagnostics(),
            LaunchMode.Tui => TerminalApp.Run(),
            LaunchMode.Help => PrintHelp(),
            _ => RunGui(args),
        };
    }

    private enum LaunchMode { Gui, Tui, Doctor, VsCode, Status, Diagnostics, Help, Version }

    private static LaunchMode ResolveMode(string[] args)
    {
        if (args.Contains("--version") || args.Contains("-V")) return LaunchMode.Version;
        if (args.Contains("--help") || args.Contains("-h")) return LaunchMode.Help;
        if (args.Contains("--doctor")) return LaunchMode.Doctor;
        if (args.Contains("--vscode")) return LaunchMode.VsCode;
        if (args.Contains("--status")) return LaunchMode.Status;
        if (args.Contains("--diagnostics")) return LaunchMode.Diagnostics;
        if (args.Contains("--gui")) return LaunchMode.Gui;
        if (args.Contains("--tui")) return LaunchMode.Tui;

        // No explicit flag. Windows always uses the GUI (the binary has no
        // console subsystem). On Unix, an interactive terminal gets the TUI.
        if (OperatingSystem.IsWindows())
            return LaunchMode.Gui;
        return !Console.IsOutputRedirected && !Console.IsInputRedirected
            ? LaunchMode.Tui
            : LaunchMode.Gui;
    }

    private static int RunGui(string[] args)
    {
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static int PrintDiagnostics()
    {
        Console.WriteLine(Diagnostics.DiagnosticsLog.Instance.BuildBundle());
        return 0;
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"Storage Manager {Version}");
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine($"""
            Storage Manager {Version}

            Usage:
              StorageManager                 Launch the GUI (or the terminal UI in a console on macOS/Linux)
              StorageManager --gui           Force the graphical interface
              StorageManager --tui           Force the terminal interface
              StorageManager --doctor [host] Audit ~/.ssh/config (add --json, --fix, --dry-run, --probe)
              StorageManager --vscode [alias] Verify VS Code remote setup (add --setup to configure;
                                        --host, --user, --jump to describe the target)
              StorageManager --status [host]  Kerberos ticket + storage quota/usage (add --kinit
                                        <principal>, --user, --paths a,b,c, --mount <path>)
              StorageManager --diagnostics   Print the diagnostics bundle
              StorageManager --version       Print the version
              StorageManager --help          Show this help

            """ + Support.Line);
        return 0;
    }
}
