using StorageManager.Status;

namespace StorageManager.Cli;

/// <summary>
/// Consolidated storage &amp; auth status: <c>mounttool --status [host]</c>.
/// Flags: --user, --paths a,b,c, --mount &lt;local path&gt;, --kinit &lt;principal&gt;
/// (obtain a Kerberos ticket first, prompting for the password). Exit 0 always
/// unless an argument/gather error occurs.
/// </summary>
public static class StatusCli
{
    public static int Run(string[] args)
    {
        try
        {
            var host = args.SkipWhile(a => a != "--status").Skip(1)
                           .FirstOrDefault(a => !a.StartsWith('-')) ?? "lxplus.cern.ch";
            var user = ValueOf(args, "--user") ?? Environment.UserName;
            var mount = ValueOf(args, "--mount");
            var paths = (ValueOf(args, "--paths") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var service = StatusService.CreateDefault();

            if (ValueOf(args, "--kinit") is { } principal)
            {
                Console.Write($"Kerberos password for {principal}: ");
                var password = ReadHidden();
                var after = service.Authenticate(principal, password);
                Console.WriteLine(after.HasValidTicket
                    ? "Ticket obtained."
                    : "Could not obtain a ticket (try running kinit in a terminal).");
            }

            var report = service.GatherAsync(new StatusRequest(host, user, paths, mount))
                                .GetAwaiter().GetResult();

            Console.WriteLine($"\nStorage & Auth status — {user}@{host}\n");

            var k = report.Kerberos;
            Console.WriteLine("Kerberos:");
            if (!k.ToolsAvailable)
                Console.WriteLine("  Kerberos tools not installed.");
            else if (k.HasValidTicket)
                Console.WriteLine($"  Valid ticket — {k.Principal} ({k.Detail})");
            else
                Console.WriteLine("  No valid ticket. Run with --kinit <principal> to obtain one.");

            Console.WriteLine("\nStorage:");
            if (report.Quotas.Count == 0)
                Console.WriteLine("  No usage data (nothing mounted and no reachable remote paths).");
            foreach (var q in report.Quotas)
                Console.WriteLine($"  {q.Label} {q.Path}: {q.Describe()}");

            Console.WriteLine($"\n{Support.Line}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"status: {ex.Message}");
            return 2;
        }
    }

    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string ReadHidden()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
                    sb.Length--;
                else if (!char.IsControl(key.KeyChar))
                    sb.Append(key.KeyChar);
            }
        }
        catch (InvalidOperationException)
        {
            // stdin redirected — fall back to a plain read.
            return Console.ReadLine() ?? "";
        }
        Console.WriteLine();
        return sb.ToString();
    }
}
