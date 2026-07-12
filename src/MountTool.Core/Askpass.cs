namespace MountTool;

/// <summary>
/// SSH_ASKPASS handler: ssh invokes this executable once per authentication
/// prompt. The initial "user@host's password:" prompt is answered silently
/// with the password handed over by the main process; anything else (e.g. a
/// PAM two-factor challenge) is routed to <see cref="ChallengeHandler"/>.
/// </summary>
public static class Askpass
{
    public const string ModeVariable = "PPE_ASKPASS_MODE";
    public const string PasswordVariable = "PPE_ASKPASS_PASSWORD";

    /// <summary>
    /// Presents a non-password challenge (e.g. a 2FA code prompt) and returns the
    /// user's response, or null if cancelled. Set by the front-end before
    /// <see cref="Run"/>; when unset, the terminal fallback is used so headless
    /// invocations still work.
    /// </summary>
    public static Func<string, string?>? ChallengeHandler { get; set; }

    public static int Run(string prompt)
    {
        var password = Environment.GetEnvironmentVariable(PasswordVariable);

        // Two login-prompt shapes get the stored password: password auth
        // ("user@host's password:") and PAM keyboard-interactive, which sends
        // a bare "Password:" optionally prefixed with "(user@host)". Longer
        // texts (e.g. "One-time password (OATH)...") are challenges for the
        // user and go to the handler.
        var isLoginPasswordPrompt =
            prompt.Contains("'s password", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(
                prompt, @"^\s*(\([^)]*\)\s*)?Password\s*:\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (password is not null && isLoginPasswordPrompt)
        {
            // Defend against a stray line ending riding along in the password.
            password = password.TrimEnd('\r', '\n');
            Log($"prompt=[{prompt}] -> silent password len={password.Length} " +
                $"ascii={password.All(char.IsAscii)} fp={Fingerprint(password)}");
            // Every conventional askpass terminates its answer with a newline,
            // which ssh strips; cygwin ssh's line reader needs it before EOF.
            WriteRawUtf8(password + "\n");
            return 0;
        }

        Log($"prompt=[{prompt}] -> challenge");
        string? response;
        try
        {
            response = (ChallengeHandler ?? ConsoleChallenge).Invoke(prompt);
        }
        catch (Exception ex)
        {
            Log($"challenge failed: {ex}");
            return 1;
        }

        Log($"challenge result: {(response is null ? "cancelled" : $"{response.Length} chars")}");

        if (response is null)
            return 1;

        WriteRawUtf8(response);
        return 0;
    }

    /// <summary>Terminal fallback challenge used when no GUI handler is registered.</summary>
    private static string? ConsoleChallenge(string prompt)
    {
        Console.Error.Write(string.IsNullOrWhiteSpace(prompt) ? "Response: " : prompt);
        Console.Error.Flush();
        var line = Console.ReadLine();
        return line is null ? null : line + "\n";
    }

    /// <summary>Short non-reversible fingerprint of the secret, so logs can confirm
    /// the delivered value is stable and matches expectations without exposing it.</summary>
    private static string Fingerprint(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash)[..6];
    }

    /// <summary>Writes to stdout as raw UTF-8 bytes, immune to console codepage translation.</summary>
    private static void WriteRawUtf8(string text)
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(System.Text.Encoding.UTF8.GetBytes(text));
        stdout.Flush();
    }

    /// <summary>With PPE_DEBUG set, appends askpass activity to ppe-askpass.log in the temp directory.</summary>
    private static void Log(string message)
    {
        if (Environment.GetEnvironmentVariable("PPE_DEBUG") is null)
            return;
        DebugLog(message);
    }

    /// <summary>Unconditionally appends to ppe-askpass.log in the temp directory.</summary>
    public static void DebugLog(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "ppe-askpass.log"),
                $"{DateTime.Now:HH:mm:ss.fff} pid={Environment.ProcessId} {message}\n");
        }
        catch
        {
            // Diagnostics must never break authentication.
        }
    }
}
