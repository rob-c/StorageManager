namespace StorageManager;

/// <summary>Where users should turn for help. Surfaced in the GUI, TUI, CLI help, and errors.</summary>
public static class Support
{
    public const string Name = "Robert Currie";
    public const string Email = "rob.currie@ed.ac.uk";

    /// <summary>A one-line contact string, e.g. "Need help? Contact Robert Currie (rob.currie@ed.ac.uk)".</summary>
    public static string Line => $"Need help? Contact {Name} ({Email}).";
}
