namespace StorageManager.Ssh;

/// <summary>
/// Builds the environment that makes ssh answer password prompts non-interactively
/// via this executable's built-in SSH_ASKPASS handler. Used for password-based
/// jump connections (same password on both hops) when Kerberos is off. The same
/// mechanism the direct mounters already use.
/// </summary>
public static class AskpassEnvironment
{
    public static IReadOnlyDictionary<string, string> ForPassword(string password)
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("Cannot determine the executable path for SSH_ASKPASS.");
        if (OperatingSystem.IsWindows())
            exe = exe.Replace('\\', '/'); // cygwin ssh copes better with forward slashes

        var env = new Dictionary<string, string>
        {
            [Askpass.ModeVariable] = "1",
            [Askpass.PasswordVariable] = password,
            ["SSH_ASKPASS"] = exe,
            ["SSH_ASKPASS_REQUIRE"] = "force",
        };
        // Older ssh only consults SSH_ASKPASS when DISPLAY is set.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            env["DISPLAY"] = ":0";
        return env;
    }
}
