using System.Text.RegularExpressions;

namespace MountTool.Doctor;

/// <summary>The effective settings ssh would compute for a host, plus its jump chain.</summary>
public sealed record EffectiveConfig(
    string Host,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<string> JumpChain);

/// <summary>
/// Computes effective settings the way <c>ssh -G</c> would: walk blocks in
/// document order, and for each keyword keep the first value obtained from a
/// block whose pattern matches the host. Follows ProxyJump to build a jump chain.
/// </summary>
public sealed class EffectiveConfigResolver
{
    public EffectiveConfig Resolve(SshConfig cfg, string host)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in cfg.Blocks)
        {
            if (!Matches(block, host))
                continue;
            foreach (var entry in block.Entries)
            {
                // First value wins (OpenSSH precedence).
                values.TryAdd(entry.Keyword, entry.Value);
            }
        }

        var jumpChain = new List<string>();
        if (values.TryGetValue("ProxyJump", out var jump) && !jump.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var hop in jump.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                jumpChain.Add(StripUserAndPort(hop));
        }

        return new EffectiveConfig(host, values, jumpChain);
    }

    private static bool Matches(HostBlock block, string host)
    {
        // Match blocks are not pattern-based; treat them as always-considered
        // (conservative — we can't evaluate Match criteria without a live ssh).
        if (block.IsMatch)
            return true;

        foreach (var pattern in block.Patterns)
        {
            var negate = pattern.StartsWith('!');
            var p = negate ? pattern[1..] : pattern;
            if (GlobToRegex(p).IsMatch(host))
                return !negate;
        }
        return false;
    }

    private static string StripUserAndPort(string hop)
    {
        var at = hop.IndexOf('@');
        if (at >= 0)
            hop = hop[(at + 1)..];
        var colon = hop.LastIndexOf(':');
        if (colon >= 0)
            hop = hop[..colon];
        return hop;
    }

    private static Regex GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder("^");
        foreach (var c in glob)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
    }
}
