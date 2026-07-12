using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MountTool.VsCode;

/// <summary>
/// Reads and updates VS Code's user <c>settings.json</c>, merging the Remote-SSH
/// keys that make remote (and 2FA) connections work while preserving every other
/// setting. VS Code allows comments/trailing commas in settings.json; those are
/// tolerated on read but not preserved on write, so callers back up first.
/// </summary>
public static class VsCodeSettings
{
    /// <summary>The keys the setup applies. showLoginTerminal is essential for password/2FA prompts.</summary>
    public static IReadOnlyDictionary<string, JsonNode?> DesiredKeys { get; } = new Dictionary<string, JsonNode?>
    {
        ["remote.SSH.showLoginTerminal"] = true,
        ["remote.SSH.connectTimeout"] = 60,
        ["remote.SSH.useLocalServer"] = true,
    };

    /// <summary>The per-user settings.json path for VS Code stable.</summary>
    public static string DefaultPath
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Code", "User", "settings.json");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Path.Combine(home, "Library", "Application Support", "Code", "User", "settings.json");
            return Path.Combine(home, ".config", "Code", "User", "settings.json");
        }
    }

    /// <summary>
    /// Merges <see cref="DesiredKeys"/> into the given settings.json text and returns
    /// the new text. Existing keys are preserved; our keys are set to the desired values.
    /// </summary>
    public static string Merge(string? existingJson)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(existingJson)
                ? new JsonObject()
                : JsonNode.Parse(existingJson,
                    documentOptions: new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    }) as JsonObject ?? new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        foreach (var (key, value) in DesiredKeys)
            root[key] = value is null ? null : JsonNode.Parse(value.ToJsonString());

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>True when the given settings.json text already has all desired keys set as intended.</summary>
    public static bool IsConfigured(string? existingJson)
    {
        if (string.IsNullOrWhiteSpace(existingJson))
            return false;
        try
        {
            if (JsonNode.Parse(existingJson,
                    documentOptions: new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true,
                    }) is not JsonObject root)
                return false;

            foreach (var (key, value) in DesiredKeys)
            {
                if (root[key] is not { } actual)
                    return false;
                if (actual.ToJsonString() != (value?.ToJsonString() ?? "null"))
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
