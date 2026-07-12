namespace MountTool.Doctor;

public enum Severity { Info, Warning, Error }

/// <summary>How a <see cref="SuggestedFix"/> mutates the config file.</summary>
public enum FixKind
{
    /// <summary>Set the keyword in the target host block, replacing an existing line if present.</summary>
    SetOrReplace,
    /// <summary>Append the keyword line to the target host block.</summary>
    AppendToHost,
    /// <summary>Remove the matching keyword line.</summary>
    RemoveLine,
}

/// <summary>A concrete, applyable change to an ssh_config.</summary>
public sealed record SuggestedFix(
    string Description,
    string Keyword,
    string? NewValue,
    string TargetHostPattern,
    FixKind Kind);

/// <summary>A single diagnostic produced by a check.</summary>
public sealed record Finding(
    string CheckId,
    Severity Severity,
    string Title,
    string Explanation,
    string? EffectiveValue = null,
    SuggestedFix? Fix = null);
