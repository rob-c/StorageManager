using System.Text.RegularExpressions;

namespace StorageManager.Ssh;

/// <summary>
/// Validates host and user strings before they are written into ssh_config or
/// passed to ssh/sshfs. Rejecting whitespace, newlines, '#', and control
/// characters closes an ssh_config-injection hole (an embedded newline could
/// otherwise smuggle a ProxyCommand directive into the user's config).
/// </summary>
public static class SshName
{
    // RFC-1123 hostname: dot-separated labels of [A-Za-z0-9-], no leading/trailing '-'.
    private static readonly Regex Host = new(
        @"^(?=.{1,253}$)([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$",
        RegexOptions.Compiled);

    private static readonly Regex User = new(@"^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

    public static bool IsValidHost(string? host) => host is not null && Host.IsMatch(host);
    public static bool IsValidUser(string? user) => user is not null && User.IsMatch(user);

    /// <summary>Throws <see cref="ArgumentException"/> if a host or user is unsafe.</summary>
    public static void Validate(string targetHost, string targetUser, string jumpHost, string jumpUser)
    {
        if (!IsValidHost(targetHost)) throw new ArgumentException($"Invalid target host: '{targetHost}'.");
        if (!IsValidHost(jumpHost)) throw new ArgumentException($"Invalid jump host: '{jumpHost}'.");
        if (!IsValidUser(targetUser)) throw new ArgumentException($"Invalid username: '{targetUser}'.");
        if (!IsValidUser(jumpUser)) throw new ArgumentException($"Invalid username: '{jumpUser}'.");
    }
}
