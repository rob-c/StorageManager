using MountTool.VsCode;

namespace MountTool.Cli;

/// <summary>
/// Non-interactive VS Code remote setup: <c>mounttool --vscode [alias]</c>.
/// Verifies by default; <c>--setup</c> installs extensions and writes config.
/// Args: --host, --user, --jump to describe the target. Exit 0 all-ok, 1 issues.
/// </summary>
public static class VsCodeCliCommand
{
    public static int Run(string[] args)
    {
        try
        {
            var doSetup = args.Contains("--setup");
            var alias = args.SkipWhile(a => a != "--vscode").Skip(1)
                            .FirstOrDefault(a => !a.StartsWith('-')) ?? "lxplus";
            var host = ValueOf(args, "--host") ?? $"{alias}.cern.ch";
            var user = ValueOf(args, "--user") ?? Environment.UserName;
            var jump = ValueOf(args, "--jump");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshConfig = Path.Combine(home, ".ssh", "config");
            var settings = VsCodeSettings.DefaultPath;

            var target = new VsCodeTarget(alias, host, user, jump);
            var setup = new VsCodeSetup(new VsCodeCli());

            if (doSetup)
            {
                var result = setup.Setup(target, sshConfig, settings);
                if (result.Error is not null)
                {
                    Console.Error.WriteLine(result.Error);
                    Console.Error.WriteLine(Support.Line);
                    return 1;
                }
                Console.WriteLine($"Installed: {(result.InstalledExtensions.Count == 0 ? "(already present)" : string.Join(", ", result.InstalledExtensions))}");
                if (result.SshBackupPath is not null)
                    Console.WriteLine($"ssh_config backup: {result.SshBackupPath}");
                if (result.SettingsBackupPath is not null)
                    Console.WriteLine($"settings.json backup: {result.SettingsBackupPath}");
            }

            var status = setup.Verify(target, sshConfig, settings);
            Console.WriteLine($"\nVS Code remote setup for '{alias}':");
            foreach (var c in status.Checks)
                Console.WriteLine($"  [{(c.Ok ? "OK " : "!! ")}] {c.Label}: {c.Detail}");

            if (!status.AllOk)
            {
                Console.WriteLine($"\nSome checks failed. Run with --setup to fix, or {Support.Line}");
                return 1;
            }
            Console.WriteLine("\nAll good — open VS Code and pick this host from Remote-SSH.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"vscode-setup: {ex.Message}");
            return 2;
        }
    }

    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
