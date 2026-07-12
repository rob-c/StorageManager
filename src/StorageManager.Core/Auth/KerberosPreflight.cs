using StorageManager.Errors;

namespace StorageManager.Auth;

/// <summary>
/// Checks that the Kerberos client tools are installed and, when they aren't,
/// offers to install them: a one-click winget install of MIT Kerberos for Windows,
/// or a copyable package command on macOS/Linux.
/// </summary>
public static class KerberosPreflight
{
    public const string WingetPackage = "MIT.Kerberos";

    public static PreflightResult? Check(IKerberosCli cli, bool isWindows)
    {
        if (cli.ToolsAvailable)
            return null;

        if (isWindows)
            return new PreflightResult(
                "Kerberos is required for jump-host connections, but MIT Kerberos for Windows " +
                "was not found.\n\nClick \"Install for me\" to install it, then try again.",
                new FixAction("Install for me", FixKindUi.WingetInstall, WingetPackage));

        return new PreflightResult(
            "Kerberos tools (kinit/klist) were not found.\n\n" +
            "Install them with your package manager, e.g.:\n" +
            "  sudo apt install krb5-user      (Debian/Ubuntu)\n" +
            "  sudo dnf install krb5-workstation (Fedora/RHEL)\n" +
            "  brew install krb5               (macOS)",
            new FixAction("Copy install command", FixKindUi.CopyCommand, "sudo apt install krb5-user"));
    }
}
