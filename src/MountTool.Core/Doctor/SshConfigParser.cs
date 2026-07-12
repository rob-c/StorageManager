namespace MountTool.Doctor;

/// <summary>
/// Parses an ssh_config into ordered <see cref="HostBlock"/>s, preserving source
/// spans so a fixer can edit individual lines without regenerating the file.
/// Handles <c>Include</c> (with globbing) up to a bounded depth, and models
/// OpenSSH's document order so precedence (first value wins) can be resolved.
/// </summary>
public sealed class SshConfigParser
{
    private const int MaxIncludeDepth = 16;

    public SshConfig Parse(string path)
    {
        var blocks = new List<HostBlock>();
        var files = new List<string>();
        ParseFile(path, blocks, files, depth: 0);
        return new SshConfig(blocks, files);
    }

    public SshConfig ParseText(string text, string file = "<memory>")
    {
        var blocks = new List<HostBlock>();
        ParseLines(text.Replace("\r\n", "\n").Split('\n'), file, blocks,
            includeFiles: null, includeCollector: new List<string>(), depth: 0);
        return new SshConfig(blocks, [file]);
    }

    private void ParseFile(string path, List<HostBlock> blocks, List<string> files, int depth)
    {
        if (depth > MaxIncludeDepth || !File.Exists(path))
            return;
        files.Add(path);
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        ParseLines(lines, path, blocks, files, files, depth);
    }

    private void ParseLines(
        string[] lines, string file, List<HostBlock> blocks,
        List<string>? includeFiles, List<string> includeCollector, int depth)
    {
        List<ConfigEntry> current = [];
        var currentPatterns = new List<string> { "*" };
        var isMatch = false;
        var blockStart = 1;
        var lastContentLine = 1;

        void Flush(int endLine)
        {
            blocks.Add(new HostBlock(
                currentPatterns.FirstOrDefault() ?? "*",
                currentPatterns.ToList(),
                current,
                new SourceSpan(file, blockStart, endLine),
                isMatch));
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var raw = lines[i];
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var (keyword, value) = SplitDirective(trimmed);
            if (keyword.Length == 0)
                continue;

            if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase)
                || keyword.Equals("Match", StringComparison.OrdinalIgnoreCase))
            {
                Flush(lastContentLine);
                current = [];
                isMatch = keyword.Equals("Match", StringComparison.OrdinalIgnoreCase);
                currentPatterns = isMatch
                    ? [value]
                    : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (currentPatterns.Count == 0)
                    currentPatterns.Add("*");
                blockStart = lineNo;
                lastContentLine = lineNo;
                continue;
            }

            if (keyword.Equals("Include", StringComparison.OrdinalIgnoreCase) && includeFiles is not null)
            {
                foreach (var included in ResolveIncludes(value, file))
                    ParseFile(included, blocks, includeCollector, depth + 1);
                lastContentLine = lineNo;
                continue;
            }

            current.Add(new ConfigEntry(keyword, value, new SourceSpan(file, lineNo, lineNo)));
            lastContentLine = lineNo;
        }

        Flush(lastContentLine);
    }

    /// <summary>Splits "Keyword value" or "Keyword=value"; value keeps internal spaces.</summary>
    public static (string Keyword, string Value) SplitDirective(string line)
    {
        var eq = line.IndexOf('=');
        var space = FirstWhitespace(line);
        int cut;
        if (eq >= 0 && (space < 0 || eq < space))
            cut = eq;
        else
            cut = space;

        if (cut < 0)
            return (line, "");
        var keyword = line[..cut].Trim();
        var value = line[(cut + 1)..].Trim();
        return (keyword, value);
    }

    private static int FirstWhitespace(string s)
    {
        for (var i = 0; i < s.Length; i++)
            if (char.IsWhiteSpace(s[i]))
                return i;
        return -1;
    }

    private static IEnumerable<string> ResolveIncludes(string pattern, string sourceFile)
    {
        var expanded = pattern.Trim().Trim('"');
        if (expanded.StartsWith('~'))
            expanded = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                       + expanded[1..];

        var baseDir = Path.IsPathRooted(expanded)
            ? Path.GetDirectoryName(expanded)!
            : Path.GetDirectoryName(sourceFile) ?? ".";
        var leaf = Path.GetFileName(expanded);
        var dir = Path.IsPathRooted(expanded) ? baseDir : Path.Combine(baseDir, Path.GetDirectoryName(expanded) ?? "");

        try
        {
            if (!Directory.Exists(dir))
                return [];
            return Directory.GetFiles(dir, leaf).OrderBy(p => p, StringComparer.Ordinal);
        }
        catch
        {
            return [];
        }
    }
}
