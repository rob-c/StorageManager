using System.Text.Json;

namespace StorageManager;

/// <summary>RemotePaths overrides the default folder options for this host;
/// templates may use $USER (username) and $USER1 (its first letter).</summary>
public sealed record HostEntry(string Name, bool TwoFactorPam, IReadOnlyList<string>? RemotePaths = null);

public sealed record Config(
    string? Gateway,
    string RemotePath,
    string? MountTarget,
    IReadOnlyList<HostEntry>? Hosts = null,
    bool TwoFactorPam = false,
    int KeepAliveIntervalSeconds = 5,
    int KeepAliveCountMax = 3,
    bool ReadOnly = true,
    IReadOnlyList<string>? JumpHosts = null,
    IReadOnlyDictionary<string, string>? KerberosRealms = null,
    // Route this mount through an SSH jump/gateway host (ssh -o ProxyJump). The
    // askpass mechanism answers the password on both hops. Used for hosts only
    // reachable inside the university network (e.g. cplab boxes via student/staff).
    string? JumpHost = null,
    // Attempt GSSAPI (Kerberos) on the hops, falling back to password prompts.
    bool UseGssapi = false,
    // Resolve symlinks on the server (sshfs -o follow_symlinks) so a mount path
    // (or entries) that is a symlink — e.g. /scratch → local disk on cplab boxes —
    // is followed transparently, even across devices, instead of failing.
    bool FollowSymlinks = true)
{
    public const string FileName = "mount-config.json";

    private static readonly string[] DefaultJumpHosts =
        ["student.ph.ed.ac.uk", "staff.ph.ed.ac.uk"];

    /// <summary>Jump-host options offered in the UI (Edinburgh gateways by default).</summary>
    public IReadOnlyList<string> JumpHostList =>
        JumpHosts is { Count: > 0 } ? JumpHosts : DefaultJumpHosts;

    /// <summary>A realm map with any user-supplied domain→realm overrides folded in.</summary>
    public Auth.RealmMap BuildRealmMap() => new(KerberosRealms);

    public static Config Default { get; } = new(
        null,
        "/home/$USER",
        null,
        [
            new("staff.ph.ed.ac.uk", TwoFactorPam: false),
            new("phcomputeppe01.ph.ed.ac.uk", TwoFactorPam: false),
            new("t3-mw2.ph.ed.ac.uk", TwoFactorPam: false),
            new("lxplus.cern.ch", TwoFactorPam: true, RemotePaths:
            [
                "/afs/cern.ch/user/$USER1/$USER",
                "/afs/cern.ch/work/$USER1/$USER",
                "/eos/user/$USER1/$USER",
                "/eos/home-$USER1/$USER",
                "/eos/experiment/atlas",
                "/eos/experiment/cms",
                "/eos/experiment/lhcb",
                "/eos/experiment/alice",
            ]),
            // Fermilab: direct Kerberos (GSSAPI) to a per-experiment GPVM login node.
            // Swap the experiment (dune) in the host/paths for others (sbnd, icarus, …).
            new("dunegpvm01.fnal.gov", TwoFactorPam: false, RemotePaths:
            [
                "/nashome/$USER1/$USER",
                "/exp/dune/app/users/$USER",
                "/exp/dune/data/users/$USER",
                "/pnfs/dune/scratch/users/$USER",
                "/pnfs/dune/persistent/users/$USER",
            ]),
        ]);

    /// <summary>Selectable hosts; a legacy config with only "gateway" yields a single entry.</summary>
    public IReadOnlyList<HostEntry> HostList =>
        Hosts is { Count: > 0 } ? Hosts
        : Gateway is not null ? [new HostEntry(Gateway, TwoFactorPam)]
        : Default.Hosts!;

    /// <summary>Loads mount-config.json beside the executable if present; throws on malformed content.</summary>
    public static Config Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);

        if (!File.Exists(path))
            return Default;

        var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"{FileName} is empty.");

        if (string.IsNullOrWhiteSpace(loaded.RemotePath))
            throw new InvalidDataException($"{FileName} must set \"remotePath\".");

        if (loaded.Hosts is not { Count: > 0 } && string.IsNullOrWhiteSpace(loaded.Gateway))
            throw new InvalidDataException($"{FileName} must set \"hosts\" or \"gateway\".");

        if (loaded.KeepAliveIntervalSeconds < 1 || loaded.KeepAliveCountMax < 1)
            throw new InvalidDataException($"{FileName} keep-alive settings must be at least 1.");

        return loaded;
    }
}
