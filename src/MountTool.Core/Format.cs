namespace MountTool;

/// <summary>Small shared formatting helpers used across the GUI, TUI, and CLI.</summary>
public static class Format
{
    /// <summary>Human-readable byte size, e.g. 1.2 TB.</summary>
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        var u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.#} {units[u]}";
    }
}
