namespace StorageManager.Doctor.Checks;

/// <summary>
/// Flags keepalive settings that lead to silent idle disconnects. When a probe
/// observed an idle reset, the recommendation is tuned to fire before it.
/// </summary>
public sealed class KeepaliveCheck : IConfigCheck
{
    public const string Id = "keepalive";
    private const int RecommendedInterval = 30;
    private const int MaxHealthyInterval = 60;

    public IEnumerable<Finding> Run(DoctorContext ctx)
    {
        var host = ctx.Effective.Host;
        ctx.Effective.Values.TryGetValue("ServerAliveInterval", out var raw);

        var observedReset = ctx.Probes
            .Where(p => p.IdleResetAfter is not null)
            .Select(p => p.IdleResetAfter!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();

        var recommended = RecommendedInterval;
        string? evidence = null;
        if (observedReset > TimeSpan.Zero)
        {
            recommended = Math.Max(15, (int)(observedReset.TotalSeconds / 2));
            evidence = $"This network reset an idle connection after about " +
                       $"{(int)observedReset.TotalSeconds}s.";
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            yield return new Finding(
                Id, Severity.Warning,
                "No ServerAliveInterval set",
                (evidence is null ? "" : evidence + " ") +
                "Without keepalives, an idle SSH session can be dropped by a firewall or " +
                "middlebox with no warning. Set ServerAliveInterval so ssh nudges the server periodically.",
                EffectiveValue: "(unset)",
                Fix: new SuggestedFix(
                    $"Set ServerAliveInterval {recommended} for {host}",
                    "ServerAliveInterval", recommended.ToString(), host, FixKind.SetOrReplace));
            yield break;
        }

        if (int.TryParse(raw, out var seconds) && seconds > MaxHealthyInterval)
        {
            yield return new Finding(
                Id, Severity.Warning,
                $"ServerAliveInterval is high ({seconds}s)",
                (evidence is null ? "" : evidence + " ") +
                $"An interval above {MaxHealthyInterval}s can be too slow to keep a session alive " +
                "through an aggressive firewall. Consider lowering it.",
                EffectiveValue: raw,
                Fix: new SuggestedFix(
                    $"Set ServerAliveInterval {recommended} for {host}",
                    "ServerAliveInterval", recommended.ToString(), host, FixKind.SetOrReplace));
        }
    }
}
