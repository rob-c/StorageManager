namespace MountTool.Errors;

/// <summary>How the front-end should offer to remediate a failed preflight.</summary>
public enum FixKindUi
{
    /// <summary>Run winget to install prerequisites. Payload = ';'-separated package IDs.</summary>
    WingetInstall,
    /// <summary>Open a URL. Payload = the URL.</summary>
    OpenUrl,
    /// <summary>Offer a shell command to copy. Payload = the command.</summary>
    CopyCommand,
}

/// <summary>An offered remediation for a preflight problem.</summary>
public sealed record FixAction(string Label, FixKindUi Kind, string Payload);

/// <summary>
/// The outcome of a mount preflight: a user-facing message and, when we know how
/// to help, an actionable <see cref="FixAction"/>.
/// </summary>
public sealed record PreflightResult(string Message, FixAction? Fix = null);
