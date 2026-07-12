namespace StorageManager.Doctor.Checks;

/// <summary>
/// Validates ProxyJump/ProxyCommand usage: unresolvable jump hosts, and mixing
/// a manual ProxyCommand with ProxyJump (they conflict).
/// </summary>
public sealed class JumpHostCheck : IConfigCheck
{
    public const string Id = "jumphost";

    public IEnumerable<Finding> Run(DoctorContext ctx)
    {
        var v = ctx.Effective.Values;

        var hasJump = v.ContainsKey("ProxyJump")
                      && !v["ProxyJump"].Equals("none", StringComparison.OrdinalIgnoreCase);
        var hasCommand = v.ContainsKey("ProxyCommand");

        if (hasJump && hasCommand)
        {
            yield return new Finding(
                Id, Severity.Warning,
                "Both ProxyJump and ProxyCommand are set",
                "ProxyJump and a manual ProxyCommand conflict — ssh uses one and ignores the other, " +
                "which is confusing. Keep ProxyJump (simpler) and remove the ProxyCommand unless it " +
                "does something ProxyJump can't.");
        }

        foreach (var hop in ctx.Effective.JumpChain)
        {
            var isFqdnOrIp = hop.Contains('.');
            var hasBlock = ctx.Config.Blocks.Any(b =>
                !b.IsMatch && b.Patterns.Any(p => p.Equals(hop, StringComparison.OrdinalIgnoreCase)));

            if (!isFqdnOrIp && !hasBlock)
            {
                yield return new Finding(
                    Id, Severity.Error,
                    $"Jump host '{hop}' is not configured",
                    $"ProxyJump references '{hop}', but there is no Host block defining it and it isn't a " +
                    "fully-qualified name. ssh will fail to resolve the jump. Add a Host block with its " +
                    "HostName/User, or use a full hostname.",
                    EffectiveValue: v.GetValueOrDefault("ProxyJump"));
            }
        }
    }
}
