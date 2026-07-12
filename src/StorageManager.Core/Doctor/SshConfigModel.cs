namespace StorageManager.Doctor;

/// <summary>A line range in a source file, used to make surgical, comment-preserving edits.</summary>
public sealed record SourceSpan(string File, int StartLine, int EndLine);

/// <summary>One keyword/value directive and where it came from.</summary>
public sealed record ConfigEntry(string Keyword, string Value, SourceSpan Span);

/// <summary>
/// A Host (or Match) block: its pattern(s) and the directives within it. The
/// implicit block for directives that appear before any Host uses pattern "*".
/// </summary>
public sealed record HostBlock(
    string Pattern,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<ConfigEntry> Entries,
    SourceSpan Span,
    bool IsMatch = false);

/// <summary>A parsed ssh_config, including any files pulled in via Include.</summary>
public sealed record SshConfig(IReadOnlyList<HostBlock> Blocks, IReadOnlyList<string> Files)
{
    public IEnumerable<ConfigEntry> AllEntries => Blocks.SelectMany(b => b.Entries);
}
