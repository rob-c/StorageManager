using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MountTool.VsCode;

/// <summary>Abstracts the VS Code command-line tool so setup logic is testable without VS Code.</summary>
public interface IVsCodeCli
{
    /// <summary>Full path to the located `code` executable, or null if none was found.</summary>
    string? ExecutablePath { get; }

    /// <summary>Installed extension IDs (lower-cased), or empty if code is unavailable.</summary>
    IReadOnlyList<string> ListExtensions();

    /// <summary>Installs an extension by ID; returns true on success.</summary>
    bool InstallExtension(string extensionId);
}

/// <summary>
/// Locates the VS Code CLI (`code`, falling back to `code-insiders`) on PATH and
/// in the standard per-OS install locations, then drives it to list and install
/// extensions.
/// </summary>
public sealed class VsCodeCli : IVsCodeCli
{
    public string? ExecutablePath { get; }

    public VsCodeCli(string? explicitPath = null)
    {
        ExecutablePath = explicitPath ?? Locate();
    }

    public IReadOnlyList<string> ListExtensions()
    {
        var output = Run("--list-extensions");
        if (output is null)
            return [];
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.ToLowerInvariant())
            .ToList();
    }

    public bool InstallExtension(string extensionId) =>
        Run("--install-extension", extensionId, "--force") is not null;

    private string? Run(params string[] args)
    {
        if (ExecutablePath is null)
            return null;
        try
        {
            var info = new ProcessStartInfo(ExecutablePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
                info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            if (p is null)
                return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(120_000);
            return p.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The candidate CLI locations, PATH first, then per-OS install paths.</summary>
    public static IEnumerable<string> CandidatePaths()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var names = isWindows ? new[] { "code.cmd", "code-insiders.cmd" } : ["code", "code-insiders"];

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in pathDirs)
            foreach (var name in names)
                yield return Path.Combine(dir, name);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (isWindows)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(localAppData, @"Programs\Microsoft VS Code\bin\code.cmd");
            yield return Path.Combine(programFiles, @"Microsoft VS Code\bin\code.cmd");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code";
            yield return Path.Combine(home,
                "Applications/Visual Studio Code.app/Contents/Resources/app/bin/code");
            yield return "/opt/homebrew/bin/code";
            yield return "/usr/local/bin/code";
        }
        else
        {
            yield return "/usr/bin/code";
            yield return "/usr/local/bin/code";
            yield return "/snap/bin/code";
            yield return Path.Combine(home, ".local/bin/code");
        }
    }

    private static string? Locate() => CandidatePaths().FirstOrDefault(File.Exists);
}
