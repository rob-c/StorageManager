using System.Text.Json;
using StorageManager.Doctor;

namespace StorageManager.Cli;

/// <summary>
/// Non-interactive SSH Doctor entry point: <c>mounttool --doctor [host]</c>.
/// Flags: --json (machine output), --probe (active network tests),
/// --fix (apply suggested fixes), --dry-run (show the diff without writing).
/// Exit codes: 0 clean, 1 findings present, 2 error.
/// </summary>
public static class DoctorCli
{
    public static int Run(string[] args)
    {
        try
        {
            var json = args.Contains("--json");
            var probe = args.Contains("--probe");
            var fix = args.Contains("--fix");
            var dryRun = args.Contains("--dry-run");
            var host = args.SkipWhile(a => a != "--doctor").Skip(1)
                           .FirstOrDefault(a => !a.StartsWith('-'))
                       ?? "staff.ph.ed.ac.uk";

            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

            var report = SshDoctor.CreateDefault()
                .RunAsync(configPath, host, runProbes: probe).GetAwaiter().GetResult();

            if (json)
                Console.WriteLine(JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }));
            else
                PrintHuman(report, configPath);

            if ((fix || dryRun) && report.Findings.Any(f => f.Fix is not null))
                ApplyFixes(report, configPath, dryRun);

            return report.HasFindings ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ssh-doctor: {ex.Message}");
            return 2;
        }
    }

    private static void PrintHuman(DoctorReport report, string configPath)
    {
        Console.WriteLine($"SSH Doctor — host '{report.Host}' ({configPath})");
        if (!report.HasFindings)
        {
            Console.WriteLine("  No problems found.");
            return;
        }
        foreach (var f in report.Findings)
        {
            Console.WriteLine($"\n[{f.Severity.ToString().ToUpperInvariant()}] {f.Title}");
            if (f.EffectiveValue is not null)
                Console.WriteLine($"  effective: {f.EffectiveValue}");
            Console.WriteLine($"  {f.Explanation}");
            if (f.Fix is { } fx)
                Console.WriteLine($"  fix: {fx.Description}");
        }
    }

    private static void ApplyFixes(DoctorReport report, string configPath, bool dryRun)
    {
        var fixes = report.Findings.Where(f => f.Fix is not null).Select(f => f.Fix!).ToList();
        var outcome = new ConfigFixer().Apply(configPath, fixes, dryRun);

        Console.WriteLine();
        if (dryRun)
        {
            Console.WriteLine("Proposed changes (dry run):");
            Console.WriteLine(outcome.UnifiedDiff);
        }
        else if (outcome.Applied)
        {
            Console.WriteLine($"Applied {fixes.Count} fix(es). Backup: {outcome.BackupPath}");
        }
        else
        {
            Console.WriteLine("No changes were necessary.");
        }
    }
}
