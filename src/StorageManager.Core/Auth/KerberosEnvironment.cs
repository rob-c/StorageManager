using System.Runtime.InteropServices;

namespace StorageManager.Auth;

/// <summary>
/// Ensures MIT Kerberos and SSHFS-Win's Cygwin OpenSSH share one credential cache
/// on Windows. Cygwin ssh cannot read MIT KfW's default <c>API:</c>/<c>MSLSA:</c>
/// caches, so we force both to the same <c>FILE:</c> ccache via <c>KRB5CCNAME</c>.
/// Child processes (kinit, ssh) inherit it. A no-op on Linux/macOS, where the
/// system default ccache already works for both.
/// </summary>
public static class KerberosEnvironment
{
    public const string CcacheVariable = "KRB5CCNAME";

    /// <summary>Sets a shared FILE: ccache on Windows if one isn't already configured.</summary>
    public static void EnsureSharedCache()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(CcacheVariable)))
            return; // respect an existing choice

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StorageManager");
            Directory.CreateDirectory(dir);
            var ccache = "FILE:" + Path.Combine(dir, "krb5cc");
            Environment.SetEnvironmentVariable(CcacheVariable, ccache);
        }
        catch
        {
            // If we can't set it, fall back to the system default (best-effort).
        }
    }
}
