using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MountTool.Diagnostics;

/// <summary>
/// Central rolling diagnostics sink. Keeps a bounded in-memory ring and, best
/// effort, appends to a per-user log file. Every recorded message is redacted
/// so a stray password value can never be persisted.
/// </summary>
public sealed class DiagnosticsLog
{
    public const int RingCapacity = 500;

    private static readonly Regex Redactions = new(
        @"(?i)(password|passwd|pwd)\s*[=:]\s*\S+",
        RegexOptions.Compiled);

    private readonly ConcurrentQueue<string> _ring = new();
    private readonly string? _filePath;
    private readonly Func<DateTime> _clock;

    public DiagnosticsLog(string? directory, Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
        if (directory is not null)
        {
            try
            {
                Directory.CreateDirectory(directory);
                _filePath = Path.Combine(directory, "diagnostics.log");
            }
            catch
            {
                _filePath = null;
            }
        }
    }

    public static DiagnosticsLog Instance { get; } =
        new(TryDefaultDirectory());

    public void Record(string category, string message)
    {
        var line = $"{_clock():yyyy-MM-dd HH:mm:ss} [{category}] {Redact(message)}";

        _ring.Enqueue(line);
        while (_ring.Count > RingCapacity && _ring.TryDequeue(out _)) { }

        if (_filePath is not null)
        {
            try { File.AppendAllText(_filePath, line + "\n"); }
            catch { /* diagnostics must never throw */ }
        }
    }

    /// <summary>A redacted support bundle: environment header plus the ring buffer.</summary>
    public string BuildBundle()
    {
        var sb = new StringBuilder();
        sb.AppendLine("PPE Storage Mounter diagnostics");
        sb.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Tool: {typeof(DiagnosticsLog).Assembly.GetName().Version}");
        sb.AppendLine(new string('-', 40));
        foreach (var line in _ring)
            sb.AppendLine(line);
        return sb.ToString();
    }

    public static string Redact(string message) => Redactions.Replace(message, m =>
    {
        var idx = m.Value.IndexOfAny(['=', ':']);
        return m.Value[..(idx + 1)] + " [redacted]";
    });

    private static string? TryDefaultDirectory()
    {
        try
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PPEStorageMounter");
        }
        catch
        {
            return null;
        }
    }
}
