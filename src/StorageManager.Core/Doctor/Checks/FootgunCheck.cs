namespace StorageManager.Doctor.Checks;

/// <summary>
/// Catches common security and correctness foot-guns: disabled host-key
/// checking, discarded known_hosts, duplicate host blocks, and misspelled
/// keywords that ssh silently ignores.
/// </summary>
public sealed class FootgunCheck : IConfigCheck
{
    public const string Id = "footgun";

    // A pragmatic subset of common OpenSSH client keywords, lower-cased.
    private static readonly HashSet<string> KnownKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Match", "HostName", "User", "Port", "IdentityFile", "IdentitiesOnly",
        "ProxyJump", "ProxyCommand", "ForwardAgent", "ForwardX11", "Compression",
        "ServerAliveInterval", "ServerAliveCountMax", "TCPKeepAlive", "StrictHostKeyChecking",
        "UserKnownHostsFile", "PreferredAuthentications", "PubkeyAuthentication",
        "PasswordAuthentication", "KbdInteractiveAuthentication", "NumberOfPasswordPrompts",
        "ConnectTimeout", "ConnectionAttempts", "RekeyLimit", "IPQoS", "AddKeysToAgent",
        "ControlMaster", "ControlPath", "ControlPersist", "LogLevel", "Include", "Ciphers",
        "MACs", "KexAlgorithms", "HostKeyAlgorithms", "RequestTTY", "SendEnv", "SetEnv",
    };

    public IEnumerable<Finding> Run(DoctorContext ctx)
    {
        var v = ctx.Effective.Values;

        if (v.TryGetValue("StrictHostKeyChecking", out var strict)
            && strict.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Finding(
                Id, Severity.Warning,
                "StrictHostKeyChecking is disabled",
                "'StrictHostKeyChecking no' makes ssh accept any host key silently, defeating " +
                "protection against man-in-the-middle attacks. Prefer 'accept-new', which trusts " +
                "first use but still warns on changes.",
                EffectiveValue: strict,
                Fix: new SuggestedFix(
                    $"Set StrictHostKeyChecking accept-new for {ctx.Effective.Host}",
                    "StrictHostKeyChecking", "accept-new", ctx.Effective.Host, FixKind.SetOrReplace));
        }

        if (v.TryGetValue("UserKnownHostsFile", out var khf)
            && khf.Replace("\"", "").Split(' ').Any(p => p is "/dev/null"))
        {
            yield return new Finding(
                Id, Severity.Warning,
                "known_hosts is discarded (/dev/null)",
                "Pointing UserKnownHostsFile at /dev/null throws away host-key memory, so ssh can " +
                "never detect a changed key. Remove this unless you have a specific throwaway use.",
                EffectiveValue: khf);
        }

        // Duplicate Host blocks (same exact pattern in more than one block).
        var dupes = ctx.Config.Blocks
            .Where(b => !b.IsMatch && b.Pattern != "*")
            .GroupBy(b => b.Pattern, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var g in dupes)
        {
            yield return new Finding(
                Id, Severity.Warning,
                $"Duplicate Host block for '{g.Key}'",
                $"'{g.Key}' appears in more than one Host block. Later duplicates don't override earlier " +
                "ones the way you might expect — ssh keeps the first value per keyword. Merge them to " +
                "avoid confusion.");
        }

        // Misspelled keywords ssh would ignore.
        foreach (var entry in ctx.Config.AllEntries.DistinctBy(e => e.Keyword, StringComparer.OrdinalIgnoreCase))
        {
            if (KnownKeywords.Contains(entry.Keyword))
                continue;
            var suggestion = KnownKeywords.FirstOrDefault(k => Levenshtein(k, entry.Keyword) <= 2);
            if (suggestion is not null)
            {
                yield return new Finding(
                    Id, Severity.Info,
                    $"Unknown keyword '{entry.Keyword}'",
                    $"ssh doesn't recognise '{entry.Keyword}' and silently ignores it. Did you mean " +
                    $"'{suggestion}'?",
                    EffectiveValue: entry.Value);
            }
        }
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }
}
