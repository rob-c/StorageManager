namespace StorageManager.Auth;

/// <summary>
/// Maps a hostname to its Kerberos realm by domain suffix, so a password can be
/// turned into the right principal (<c>user@REALM</c>). Defaults cover Edinburgh
/// and CERN; more can be supplied via config (e.g. Fermilab) without code changes.
/// </summary>
public sealed class RealmMap
{
    // Ordered most-specific suffix first so 'ph.ed.ac.uk' could override 'ed.ac.uk'.
    private readonly IReadOnlyList<(string Suffix, string Realm)> _entries;

    public RealmMap(IReadOnlyDictionary<string, string>? overrides = null)
    {
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ed.ac.uk"] = "ED.AC.UK",
            ["cern.ch"] = "CERN.CH",
            ["fnal.gov"] = "FNAL.GOV",
        };
        if (overrides is not null)
            foreach (var (k, v) in overrides)
                entries[k.TrimStart('.').ToLowerInvariant()] = v;

        // Longer suffixes first.
        _entries = entries
            .Select(kv => (Suffix: kv.Key, Realm: kv.Value))
            .OrderByDescending(e => e.Suffix.Length)
            .ToList();
    }

    public static RealmMap Default { get; } = new();

    /// <summary>The realm for a host, or null if no configured domain matches.</summary>
    public string? RealmFor(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var (suffix, realm) in _entries)
            if (h == suffix || h.EndsWith("." + suffix, StringComparison.Ordinal))
                return realm;
        return null;
    }

    /// <summary>The Kerberos principal <c>user@REALM</c> for a host, or null when the realm is unknown.</summary>
    public string? PrincipalFor(string user, string host)
    {
        var realm = RealmFor(host);
        return realm is null ? null : $"{user}@{realm}";
    }
}
