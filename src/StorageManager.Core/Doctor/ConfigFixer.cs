using System.Text;

namespace StorageManager.Doctor;

/// <summary>The result of applying (or previewing) fixes.</summary>
public sealed record FixOutcome(string UnifiedDiff, string? BackupPath, bool Applied);

/// <summary>
/// Applies <see cref="SuggestedFix"/>es to an ssh_config with surgical,
/// span-scoped edits: only the affected keyword lines change, comments and
/// formatting are preserved. The original is backed up before any write, and
/// <c>dryRun</c> produces a unified diff without touching disk.
/// </summary>
public sealed class ConfigFixer
{
    private readonly IClock _clock;
    private readonly SshConfigParser _parser = new();

    public ConfigFixer(IClock? clock = null) => _clock = clock ?? SystemClock.Instance;

    public FixOutcome Apply(string path, IReadOnlyList<SuggestedFix> fixes, bool dryRun)
    {
        var original = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
        var current = original;

        foreach (var fix in fixes)
            current = ApplyOne(current, fix);

        var diff = UnifiedDiff(original, current, Path.GetFileName(path));

        if (dryRun || current == original)
            return new FixOutcome(diff, null, Applied: false);

        string? backup = null;
        if (File.Exists(path))
        {
            backup = path + ".bak-" + _clock.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(path, backup, overwrite: true);
        }
        File.WriteAllText(path, current);
        return new FixOutcome(diff, backup, Applied: true);
    }

    private string ApplyOne(string text, SuggestedFix fix)
    {
        var lines = text.Length == 0 ? new List<string>() : text.Split('\n').ToList();
        var parsed = _parser.ParseText(text.Length == 0 ? "" : text);
        var block = parsed.Blocks.FirstOrDefault(b =>
            !b.IsMatch && b.Patterns.Any(p => p.Equals(fix.TargetHostPattern, StringComparison.OrdinalIgnoreCase)));

        var existing = block?.Entries
            .Where(e => e.Keyword.Equals(fix.Keyword, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        var newLine = $"{IndentFor(block, lines)}{fix.Keyword} {fix.NewValue}";

        switch (fix.Kind)
        {
            case FixKind.RemoveLine:
                foreach (var e in existing.OrderByDescending(e => e.Span.StartLine))
                    lines.RemoveAt(e.Span.StartLine - 1);
                break;

            case FixKind.SetOrReplace when existing.Count > 0:
                // Replace the first occurrence in place; drop any duplicates.
                var first = existing.OrderBy(e => e.Span.StartLine).First();
                if (lines[first.Span.StartLine - 1].Trim() != $"{fix.Keyword} {fix.NewValue}")
                    lines[first.Span.StartLine - 1] = newLine;
                foreach (var e in existing.Where(e => e != first).OrderByDescending(e => e.Span.StartLine))
                    lines.RemoveAt(e.Span.StartLine - 1);
                break;

            case FixKind.SetOrReplace:
            case FixKind.AppendToHost:
                if (block is null)
                {
                    if (lines.Count > 0 && lines[^1].Trim().Length != 0)
                        lines.Add("");
                    lines.Add($"Host {fix.TargetHostPattern}");
                    lines.Add($"    {fix.Keyword} {fix.NewValue}");
                }
                else
                {
                    var insertAt = block.Entries.Count > 0
                        ? block.Entries.Max(e => e.Span.EndLine)   // after last entry
                        : block.Span.StartLine;                    // after the Host line
                    lines.Insert(insertAt, newLine);
                }
                break;
        }

        return string.Join("\n", lines);
    }

    private static string IndentFor(HostBlock? block, List<string> lines)
    {
        if (block is { Entries.Count: > 0 })
        {
            var sample = lines[block.Entries[0].Span.StartLine - 1];
            var ws = sample[..(sample.Length - sample.TrimStart().Length)];
            if (ws.Length > 0) return ws;
        }
        return "    ";
    }

    /// <summary>A minimal LCS-based unified diff, adequate for small config files.</summary>
    public static string UnifiedDiff(string before, string after, string label)
    {
        var a = before.Length == 0 ? [] : before.Split('\n');
        var b = after.Length == 0 ? [] : after.Split('\n');

        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{label}");
        sb.AppendLine($"+++ b/{label}");
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { sb.AppendLine($" {a[x]}"); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { sb.AppendLine($"-{a[x]}"); x++; }
            else { sb.AppendLine($"+{b[y]}"); y++; }
        }
        while (x < a.Length) { sb.AppendLine($"-{a[x]}"); x++; }
        while (y < b.Length) { sb.AppendLine($"+{b[y]}"); y++; }
        return sb.ToString();
    }
}
