using System.Text.RegularExpressions;

namespace StorageManager.Errors;

/// <summary>
/// Maps raw sshfs/ssh stderr to a <see cref="FriendlyError"/>. Pure and
/// order-sensitive: the first matching rule wins, falling through to a generic
/// message that still carries the raw text.
/// </summary>
public static class ErrorTranslator
{
    private sealed record Rule(Regex Pattern, string Headline, string Guidance);

    // Ordered most-specific first.
    private static readonly Rule[] Rules =
    [
        // macOS: macFUSE's system extension hasn't been approved / loaded.
        new(new Regex("mount_macfuse|file system is not available|osxfuse|macFUSE|MOUNT_FUSEFS",
                RegexOptions.IgnoreCase),
            "macFUSE needs to be approved.",
            "macOS can't mount until macFUSE is allowed. Open System Settings → Privacy & Security, " +
            "approve the blocked system software from \"Benjamin Fleischer\" (macFUSE), then restart " +
            "your Mac and try again. If you haven't installed it, run: brew install macfuse"),

        // A jump/gateway (ProxyJump) hop that failed to start. On Windows this is
        // usually SSHFS-Win missing its /bin/sh helper; the mount preflight offers a fix.
        new(new Regex(@"/bin/sh:|banner exchange|Connection to UNKNOWN|ProxyJump|ProxyCommand|hostname nor servname",
                RegexOptions.IgnoreCase),
            "Couldn't connect through the jump host.",
            "The connection to the jump/gateway host didn't start. On Windows this usually means " +
            "SSHFS-Win is missing a small helper (sh.exe) — the app's checks offer to add it. " +
            "Otherwise, check the jump host name and that you're on the university network or VPN."),

        new(new Regex("Permission denied|Authentication failed|Too many authentication failures",
                RegexOptions.IgnoreCase),
            "Sign-in was rejected.",
            "Check your university username and password and try again. " +
            "If you recently changed your password, use the new one."),

        new(new Regex("reading remote|remote directory|not a directory|No such file or directory",
                RegexOptions.IgnoreCase),
            "That folder could not be opened.",
            "The remote folder does not exist for your account, or you don't have access to it. " +
            "Pick a different folder and try again."),

        new(new Regex("Connection reset|Connection closed|closed by remote host|Connection refused",
                RegexOptions.IgnoreCase),
            "The server refused the connection.",
            "The server dropped the connection. If you are off campus, connect to the University VPN, " +
            "then try again in a moment."),

        new(new Regex("Host key verification failed|REMOTE HOST IDENTIFICATION HAS CHANGED|known_hosts",
                RegexOptions.IgnoreCase),
            "The server's identity could not be verified.",
            "The server's key does not match the one stored in your known_hosts file. " +
            "If you expect the server to have changed, remove its old entry from " +
            "~/.ssh/known_hosts and try again; otherwise contact IT before continuing."),

        new(new Regex("Timed out|timeout|Operation timed out|Network is unreachable|" +
                "Could not resolve|Name or service not known|Temporary failure in name resolution",
                RegexOptions.IgnoreCase),
            "The server could not be reached.",
            "Check your internet connection. If you are off campus, connect to the University VPN first, " +
            "then try again."),
    ];

    public static FriendlyError Translate(string stderr, int exitCode, bool twoFactor)
    {
        var raw = stderr ?? "";

        var match = Rules.FirstOrDefault(r => r.Pattern.IsMatch(raw));

        var headline = match?.Headline ?? "The storage could not be mounted.";
        var guidance = match?.Guidance ?? "";

        if (twoFactor && match is null)
        {
            guidance = "If you entered a two-factor code, make sure it was current and try again.";
        }

        return new FriendlyError(headline, guidance, raw);
    }
}
