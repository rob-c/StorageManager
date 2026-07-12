using Avalonia;
using MountTool.Cli;
using MountTool.Tui;

namespace MountTool;

internal static class Program
{
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
            if (Environment.GetEnvironmentVariable("PPE_ASKPASS_UI") != "tui")
                Askpass.ChallengeHandler = Gui.AskpassDialog.Prompt;
            return Askpass.Run(args.FirstOrDefault() ?? "");
        }

        return ResolveMode(args) switch
        {
            LaunchMode.Doctor => DoctorCli.Run(args),
            LaunchMode.VsCode => VsCodeCliCommand.Run(args),
            LaunchMode.Diagnostics => PrintDiagnostics(),
            LaunchMode.Tui => TerminalApp.Run(),
            LaunchMode.Help => PrintHelp(),
            _ => RunGui(args),
        };
    }

    private enum LaunchMode { Gui, Tui, Doctor, VsCode, Diagnostics, Help }

    private static LaunchMode ResolveMode(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h")) return LaunchMode.Help;
        if (args.Contains("--doctor")) return LaunchMode.Doctor;
        if (args.Contains("--vscode")) return LaunchMode.VsCode;
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

    private static int PrintHelp()
    {
        Console.WriteLine("""
            PPE Storage Mounter

            Usage:
              mounttool                 Launch the GUI (or the terminal UI in a console on macOS/Linux)
              mounttool --gui           Force the graphical interface
              mounttool --tui           Force the terminal interface
              mounttool --doctor [host] Audit ~/.ssh/config (add --json, --fix, --dry-run, --probe)
              mounttool --vscode [alias] Verify VS Code remote setup (add --setup to configure;
                                        --host, --user, --jump to describe the target)
              mounttool --diagnostics   Print the diagnostics bundle
              mounttool --help          Show this help

            """ + Support.Line);
        return 0;
    }
}
