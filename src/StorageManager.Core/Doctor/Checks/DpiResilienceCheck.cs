namespace StorageManager.Doctor.Checks;

/// <summary>
/// Flags options known to interact badly with deep-packet-inspection boxes and
/// middleboxes: compression, and IPQoS markings that some networks drop or mangle.
/// </summary>
public sealed class DpiResilienceCheck : IConfigCheck
{
    public const string Id = "dpi";

    public IEnumerable<Finding> Run(DoctorContext ctx)
    {
        var host = ctx.Effective.Host;
        var v = ctx.Effective.Values;

        if (v.TryGetValue("Compression", out var comp) && comp.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Finding(
                Id, Severity.Info,
                "Compression is enabled",
                "SSH compression rarely helps on modern links and can confuse some DPI/middlebox " +
                "setups. Disabling it is usually the more robust choice.",
                EffectiveValue: comp,
                Fix: new SuggestedFix(
                    $"Set Compression no for {host}",
                    "Compression", "no", host, FixKind.SetOrReplace));
        }

        if (v.TryGetValue("IPQoS", out var qos)
            && (qos.Contains("lowdelay", StringComparison.OrdinalIgnoreCase)
                || qos.Contains("throughput", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new Finding(
                Id, Severity.Info,
                "IPQoS sets DSCP markings",
                "Some middleboxes drop or reset SSH packets carrying non-default DSCP markings. " +
                "If connections stall on this network, try 'IPQoS none'.",
                EffectiveValue: qos,
                Fix: new SuggestedFix(
                    $"Set IPQoS none for {host}",
                    "IPQoS", "none", host, FixKind.SetOrReplace));
        }
    }
}
