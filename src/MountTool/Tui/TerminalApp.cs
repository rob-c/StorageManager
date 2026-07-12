using System.Runtime.InteropServices;
using MountTool.Connectivity;
using MountTool.Doctor;
using MountTool.Errors;
using MountTool.Mounting;
using MountTool.Settings;
using Spectre.Console;

namespace MountTool.Tui;

/// <summary>
/// Terminal front-end (macOS/Linux) built on Spectre.Console. Shares the entire
/// Core stack with the GUI; this is presentation only. Launched by running
/// <c>mounttool</c> in an interactive terminal.
/// </summary>
public static class TerminalApp
{
    public static int Run()
    {
        // Tell any askpass child (spawned for 2FA) to use the terminal, not a dialog.
        Environment.SetEnvironmentVariable("PPE_ASKPASS_UI", "tui");

        AnsiConsole.Write(new FigletText("PPE Storage").Color(Color.Teal));
        AnsiConsole.MarkupLine("[grey]University datastore mounter — terminal mode[/]");
        AnsiConsole.MarkupLineInterpolated($"[grey]{Support.Line}[/]\n");

        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices("Connect storage", "Storage & Auth status", "SSH Doctor",
                            "VS Code remote setup", "Diagnostics", "Quit"));

            switch (choice)
            {
                case "Connect storage": Connect(); break;
                case "Storage & Auth status": RunStatus(); break;
                case "SSH Doctor": RunDoctor(); break;
                case "VS Code remote setup": RunVsCode(); break;
                case "Diagnostics":
                    AnsiConsole.WriteLine(Diagnostics.DiagnosticsLog.Instance.BuildBundle());
                    break;
                case "Quit": return 0;
            }
        }
    }

    private static void Connect()
    {
        Config config;
        try { config = Config.Load(); }
        catch (Exception ex) { AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]"); return; }

        var store = SettingsStore.Default;
        var saved = store.Load();

        var host = AnsiConsole.Prompt(new SelectionPrompt<HostEntry>()
            .Title("Remote [green]host[/]")
            .UseConverter(h => h.Name)
            .AddChoices(config.HostList));

        var username = AnsiConsole.Prompt(new TextPrompt<string>("University [green]username[/]:")
            .DefaultValue(saved.Username ?? "").ShowDefaultValue(saved.Username is not null));

        var templates = (host.RemotePaths is { Count: > 0 } p ? p : [config.RemotePath]).ToList();
        var template = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Remote [green]folder[/]")
            .AddChoices(templates));
        var remotePath = SubstituteUser(template, username);

        var defaultTarget = saved.MountTarget
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "S");
        var target = AnsiConsole.Prompt(new TextPrompt<string>("Mount [green]location[/]:")
            .DefaultValue(defaultTarget));

        var password = AnsiConsole.Prompt(new TextPrompt<string>("University [green]password[/]:").Secret());

        var mounter = CreateMounter(config with
        {
            Gateway = host.Name,
            TwoFactorPam = host.TwoFactorPam,
            RemotePath = remotePath,
            MountTarget = target,
        });

        if (mounter.Preflight() is { } problem)
        {
            if (problem.Blocking)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{problem.Message}[/]");
                if (problem.Fix is { Kind: FixKindUi.CopyCommand } fix)
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Run:[/] {fix.Payload}");
                return;
            }

            AnsiConsole.MarkupLineInterpolated($"[yellow]{problem.Message}[/]");
            if (!AnsiConsole.Confirm("Mount here anyway?", defaultValue: false))
                return;
        }

        var probe = GatewayProbe.CheckAsync(host.Name, 22, TimeSpan.FromSeconds(4))
            .GetAwaiter().GetResult();
        if (!probe.Reachable)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]{probe.Message}[/]");
            return;
        }

        string? error = null;
        AnsiConsole.Status().Start("Connecting…", _ =>
        {
            error = mounter.MountAsync(username, password).GetAwaiter().GetResult();
        });

        if (error is not null)
        {
            var friendly = ErrorTranslator.Translate(error, 1, host.TwoFactorPam);
            AnsiConsole.MarkupLineInterpolated($"[red]{friendly.Headline}[/]");
            if (friendly.Guidance.Length > 0)
                AnsiConsole.WriteLine(friendly.Guidance);
            return;
        }

        store.Save(saved with
        {
            Username = username,
            HostName = host.Name,
            RemotePathTemplate = template,
            MountTarget = target,
        });

        AnsiConsole.MarkupLineInterpolated($"[green]Connected[/] as {username} on {mounter.TargetDescription}");
        SessionMenu(mounter);
    }

    private static void SessionMenu(IMounter mounter)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title($"Mounted at [green]{mounter.TargetDescription}[/]")
                .AddChoices("Open in file manager", "Disconnect"));

            if (choice == "Open in file manager")
            {
                mounter.OpenInFileManager();
                continue;
            }

            AnsiConsole.Status().Start("Disconnecting…", _ =>
                mounter.UnmountAsync().GetAwaiter().GetResult());
            AnsiConsole.MarkupLine("[grey]Disconnected.[/]");
            return;
        }
    }

    private static void RunDoctor()
    {
        var host = AnsiConsole.Prompt(new TextPrompt<string>("Host to check:")
            .DefaultValue("staff.ph.ed.ac.uk"));
        var probe = AnsiConsole.Confirm("Run active network tests?", defaultValue: false);

        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

        DoctorReport report = default!;
        AnsiConsole.Status().Start("Auditing ssh_config…", _ =>
            report = SshDoctor.CreateDefault().RunAsync(configPath, host, probe).GetAwaiter().GetResult());

        if (!report.HasFindings)
        {
            AnsiConsole.MarkupLine("[green]No problems found.[/]");
            return;
        }

        var tree = new Tree($"[bold]{report.Findings.Count} finding(s) for {host}[/]");
        foreach (var f in report.Findings)
        {
            var colour = f.Severity switch
            {
                Severity.Error => "red",
                Severity.Warning => "yellow",
                _ => "grey",
            };
            var node = tree.AddNode($"[{colour}]{f.Severity}[/] {Markup.Escape(f.Title)}");
            node.AddNode(Markup.Escape(f.Explanation));
            if (f.Fix is { } fx)
                node.AddNode($"[green]fix:[/] {Markup.Escape(fx.Description)}");
        }
        AnsiConsole.Write(tree);

        var fixes = report.Findings.Where(f => f.Fix is not null).Select(f => f.Fix!).ToList();
        if (fixes.Count > 0 && AnsiConsole.Confirm($"Apply {fixes.Count} fix(es)? A backup is written first.", false))
        {
            var outcome = new ConfigFixer().Apply(configPath, fixes, dryRun: false);
            AnsiConsole.MarkupLineInterpolated(
                $"[green]Applied.[/] Backup: {outcome.BackupPath ?? "(none)"}");
        }
    }

    private static void RunStatus()
    {
        var host = AnsiConsole.Prompt(new TextPrompt<string>("Host:").DefaultValue("lxplus.cern.ch"));
        var user = AnsiConsole.Prompt(new TextPrompt<string>("Username:").DefaultValue(Environment.UserName));
        var pathsRaw = AnsiConsole.Prompt(new TextPrompt<string>("Remote paths (comma-separated):")
            .DefaultValue("/afs/cern.ch/user,/eos/user"));
        var paths = pathsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var service = MountTool.Status.StatusService.CreateDefault();

        if (AnsiConsole.Confirm("Get a Kerberos ticket first (kinit)?", false))
        {
            var principal = AnsiConsole.Prompt(new TextPrompt<string>("Principal:").DefaultValue($"{user}@CERN.CH"));
            var password = AnsiConsole.Prompt(new TextPrompt<string>("Kerberos password:").Secret());
            var after = service.Authenticate(principal, password);
            AnsiConsole.MarkupLine(after.HasValidTicket
                ? "[green]Ticket obtained.[/]"
                : "[red]Could not obtain a ticket — try kinit in a terminal.[/]");
        }

        var report = service.GatherAsync(new MountTool.Status.StatusRequest(host, user, paths))
                            .GetAwaiter().GetResult();

        var k = report.Kerberos;
        AnsiConsole.MarkupLineInterpolated($"\n[bold]Kerberos[/]: {(
            !k.ToolsAvailable ? "[grey]tools not installed[/]" :
            k.HasValidTicket ? $"[green]valid[/] — {k.Principal}" : "[red]no ticket[/]")}");

        var table = new Table().AddColumns("Source", "Path", "Usage");
        foreach (var q in report.Quotas)
            table.AddRow(q.Label, q.Path, q.Describe());
        if (report.Quotas.Count == 0)
            table.AddRow("—", "—", "no usage data");
        AnsiConsole.Write(table);
    }

    private static void RunVsCode()
    {
        var alias = AnsiConsole.Prompt(new TextPrompt<string>("ssh_config [green]alias[/]:").DefaultValue("lxplus"));
        var host = AnsiConsole.Prompt(new TextPrompt<string>("[green]Host name[/]:").DefaultValue($"{alias}.cern.ch"));
        var user = AnsiConsole.Prompt(new TextPrompt<string>("[green]Username[/]:").DefaultValue(Environment.UserName));
        var jump = AnsiConsole.Prompt(new TextPrompt<string>("[green]Jump host[/] (blank for none):")
            .AllowEmpty());

        var target = new MountTool.VsCode.VsCodeTarget(
            alias, host, user, string.IsNullOrWhiteSpace(jump) ? null : jump);
        var setup = new MountTool.VsCode.VsCodeSetup(new MountTool.VsCode.VsCodeCli());
        var sshConfig = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        var settings = MountTool.VsCode.VsCodeSettings.DefaultPath;

        if (AnsiConsole.Confirm("Set up now (install extensions + write config)?", false))
        {
            var result = setup.Setup(target, sshConfig, settings);
            if (result.Error is not null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{result.Error}[/]");
                AnsiConsole.MarkupLineInterpolated($"[grey]{Support.Line}[/]");
                return;
            }
        }

        foreach (var c in setup.Verify(target, sshConfig, settings).Checks)
            AnsiConsole.MarkupLineInterpolated(
                $"{(c.Ok ? "[green]OK[/]" : "[red]!![/]")} {c.Label} — {c.Detail}");
    }

    private static string SubstituteUser(string template, string username) =>
        username.Length == 0 ? template
            : template.Replace("$USER1", username[..1]).Replace("$USER", username);

    private static IMounter CreateMounter(Config config) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsMounter(config) :
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new MacMounter(config) :
        new LinuxMounter(config);
}
