using System.Globalization;
using System.Text.RegularExpressions;

namespace StorageManager.Storage;

/// <summary>A usage/quota reading for one path from one source.</summary>
public sealed record QuotaInfo(string Label, string Path, long? UsedBytes, long? LimitBytes)
{
    /// <summary>Percentage used, or null when the limit is unknown.</summary>
    public double? Percent =>
        LimitBytes is > 0 && UsedBytes is not null ? 100.0 * UsedBytes.Value / LimitBytes.Value : null;

    public string Describe()
    {
        var used = UsedBytes is { } u ? Format.Bytes(u) : "?";
        var limit = LimitBytes is { } l ? Format.Bytes(l) : "?";
        var pct = Percent is { } p ? $" ({p:0.#}%)" : "";
        return $"{used} of {limit}{pct}";
    }
}

/// <summary>Result of a remote command execution.</summary>
public sealed record RemoteResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs a command on a remote host over SSH.</summary>
public interface IRemoteExec
{
    Task<RemoteResult> RunAsync(string host, string user, string command, CancellationToken ct = default);
}

/// <summary>
/// Pure parsers turning the output of common quota commands into
/// <see cref="QuotaInfo"/>. Each returns null when the output can't be parsed.
/// </summary>
public static class QuotaParsers
{
    /// <summary>Parses `df -P &lt;path&gt;` (POSIX): blocks are 1024 bytes.</summary>
    public static QuotaInfo? ParseDf(string output, string path, string label = "Filesystem")
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return null;
        var cols = lines[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Filesystem  1024-blocks  Used  Available  Capacity  Mounted-on
        if (cols.Length < 6)
            return null;
        if (!long.TryParse(cols[1], out var total) || !long.TryParse(cols[2], out var used))
            return null;
        return new QuotaInfo(label, path, used * 1024, total * 1024);
    }

    /// <summary>Parses AFS `fs listquota &lt;path&gt;`: quota/used are in KiB.</summary>
    public static QuotaInfo? ParseAfsListQuota(string output, string path, string label = "AFS")
    {
        // Volume Name   Quota    Used   %Used   Partition
        // user.jbloggs  5000000  1234567  25%   50%
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return null;
        var cols = lines[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (cols.Length < 3)
            return null;
        if (!long.TryParse(cols[1], out var quota) || !long.TryParse(cols[2], out var used))
            return null;
        return new QuotaInfo(label, path, used * 1024, quota * 1024);
    }

    /// <summary>Parses EOS `eos quota ls -m` (monitoring key=value format).</summary>
    public static QuotaInfo? ParseEosQuotaMonitoring(string output, string path, string label = "EOS")
    {
        var usedMatch = Regex.Match(output, @"usedbytes=(\d+)");
        var maxMatch = Regex.Match(output, @"maxbytes=(\d+)");
        if (!usedMatch.Success)
            return null;
        var used = long.Parse(usedMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        long? max = maxMatch.Success && long.Parse(maxMatch.Groups[1].Value) > 0
            ? long.Parse(maxMatch.Groups[1].Value, CultureInfo.InvariantCulture)
            : null;
        return new QuotaInfo(label, path, used, max);
    }

    /// <summary>Parses `quota -s` blocks line for the user's space usage (best-effort).</summary>
    public static QuotaInfo? ParseQuotaDashS(string output, string path, string label = "User quota")
    {
        foreach (var line in output.Split('\n'))
        {
            // A filesystem line looks like:  /dev/sdaN  12.3G*  20G  25G  ...
            var cols = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cols.Length < 3 || !cols[0].StartsWith('/'))
                continue;
            var used = ParseHumanSize(cols[1].TrimEnd('*'));
            var limit = ParseHumanSize(cols[2]);
            if (used is not null)
                return new QuotaInfo(label, path, used, limit);
        }
        return null;
    }

    private static long? ParseHumanSize(string token)
    {
        var m = Regex.Match(token, @"^(?<n>[\d.]+)(?<u>[KMGTP]?)$", RegexOptions.IgnoreCase);
        if (!m.Success || !double.TryParse(m.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            return null;
        var mult = char.ToUpperInvariant(m.Groups["u"].Value.FirstOrDefault()) switch
        {
            'K' => 1024L,
            'M' => 1024L * 1024,
            'G' => 1024L * 1024 * 1024,
            'T' => 1024L * 1024 * 1024 * 1024,
            'P' => 1024L * 1024 * 1024 * 1024 * 1024,
            _ => 1L,
        };
        return (long)(n * mult);
    }
}
