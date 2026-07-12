using System.Text.RegularExpressions;

namespace StorageManager.Auth;

/// <summary>Current Kerberos ticket state for display.</summary>
public sealed record KerberosStatus(
    bool ToolsAvailable,
    bool HasValidTicket,
    string? Principal,
    string? Realm,
    string? Detail)
{
    public static KerberosStatus NoTools { get; } =
        new(false, false, null, null, "Kerberos tools (kinit/klist) were not found.");

    /// <summary>Kerberos is switched off app-wide (the UI tickbox is unticked).</summary>
    public static KerberosStatus Off { get; } =
        new(false, false, null, null, "Kerberos sign-in is turned off.");
}

/// <summary>Abstracts the Kerberos command-line tools so the helper is testable.</summary>
public interface IKerberosCli
{
    bool ToolsAvailable { get; }
    bool HasAklog { get; }

    /// <summary>True when a valid TGT exists (klist -s exit 0).</summary>
    bool HasValidTicket();

    /// <summary>Raw `klist` output, or null if unavailable.</summary>
    string? GetKlistOutput();

    /// <summary>Runs kinit for the principal, feeding the password; best-effort.
    /// When <paramref name="forwardable"/>, requests a forwardable TGT (kinit -f) so
    /// GSSAPIDelegateCredentials can carry the ticket through a jump host. When
    /// <paramref name="addressless"/>, requests an addressless TGT (kinit -A) that
    /// survives NAT/firewall traversal for off-site clients.</summary>
    bool Kinit(string principal, string password, bool forwardable = false, bool addressless = false);

    /// <summary>Obtains an AFS token from the current ticket.</summary>
    bool Aklog();

    /// <summary>Destroys the current ticket cache.</summary>
    bool Kdestroy();
}

/// <summary>Parses `klist` output into a principal/realm/expiry summary.</summary>
public static class KlistParser
{
    public static (string? Principal, string? Realm, string? Detail) Parse(string? klistOutput)
    {
        if (string.IsNullOrWhiteSpace(klistOutput))
            return (null, null, null);

        string? principal = null;
        foreach (var line in klistOutput.Split('\n'))
        {
            var m = Regex.Match(line.Trim(),
                @"^(Default principal|Principal)\s*:\s*(?<p>\S+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                principal = m.Groups["p"].Value;
                break;
            }
        }

        string? realm = null;
        if (principal is { } p && p.Contains('@'))
            realm = p[(p.IndexOf('@') + 1)..];

        // The krbtgt line carries the expiry; present it verbatim as the detail.
        var tgtLine = klistOutput.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Contains("krbtgt/", StringComparison.OrdinalIgnoreCase));

        return (principal, realm, tgtLine);
    }
}

/// <summary>
/// Standalone Kerberos ticket helper: inspect, obtain, renew, and destroy tickets
/// (and fetch an AFS token). Obtaining a ticket is verified by re-reading the
/// cache afterwards, so success is never reported unless a valid ticket exists.
/// </summary>
public sealed class KerberosHelper(IKerberosCli cli)
{
    public KerberosStatus Status()
    {
        if (!cli.ToolsAvailable)
            return KerberosStatus.NoTools;

        var valid = cli.HasValidTicket();
        var (principal, realm, detail) = KlistParser.Parse(cli.GetKlistOutput());
        return new KerberosStatus(
            ToolsAvailable: true,
            HasValidTicket: valid,
            Principal: principal,
            Realm: realm,
            Detail: detail ?? (valid ? "Ticket present." : "No current ticket."));
    }

    /// <summary>
    /// Runs kinit, then re-checks the cache. Returns the resulting status; callers
    /// check <see cref="KerberosStatus.HasValidTicket"/> rather than trusting kinit's
    /// exit code, so a failed password never looks like success.
    /// </summary>
    public KerberosStatus Authenticate(
        string principal, string password,
        bool alsoAklog = true, bool forwardable = false, bool addressless = false)
    {
        if (!cli.ToolsAvailable)
            return KerberosStatus.NoTools;

        cli.Kinit(principal, password, forwardable, addressless);
        if (alsoAklog && cli.HasValidTicket() && cli.HasAklog)
            cli.Aklog();

        return Status();
    }

    public KerberosStatus Destroy()
    {
        if (cli.ToolsAvailable)
            cli.Kdestroy();
        return Status();
    }
}
