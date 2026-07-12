using System.Text.Json;

namespace MountTool.Settings;

/// <summary>
/// Loads and saves <see cref="UserSettings"/> as JSON under a per-user
/// directory. Every operation is best-effort: a corrupt, missing, or
/// unwritable file must never block mounting, so <see cref="Load"/> returns
/// <see cref="UserSettings.Empty"/> and <see cref="Save"/> swallows failures.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _path;

    public SettingsStore(string directory)
    {
        _path = Path.Combine(directory, FileName);
    }

    /// <summary>The default per-user settings directory.</summary>
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PPEStorageMounter");

    public static SettingsStore Default => new(DefaultDirectory);

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
                return UserSettings.Empty;
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), JsonOptions)
                   ?? UserSettings.Empty;
        }
        catch
        {
            return UserSettings.Empty;
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Persisting preferences must never break the app.
        }
    }
}
