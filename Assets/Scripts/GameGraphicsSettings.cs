using PlutoGE.ScriptCore;
using System.Text.Json;

namespace CoD.Scripts;

/// <summary>Player preferences belong to the game; rendering policy belongs to the engine.</summary>
public static class GameGraphicsSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlutoCombat", "graphics.json");
    private static bool _loaded;
    public static GraphicsPreset Preset { get; private set; } = GraphicsPreset.High;

    public static void Bind(RmlDocument document)
    {
        Load();
        if (!GraphicsSettings.TryApplyPreset(Preset)) Debug.LogWarning("Graphics presets are unavailable in this engine build.");
        var button = document.Element("graphics-preset");
        button.Markup = $"GRAPHICS: {Preset.ToString().ToUpperInvariant()}";
        document.OnClick("graphics-preset", () =>
        {
            var next = (GraphicsPreset)(((int)Preset + 1) % 4);
            if (!GraphicsSettings.TryApplyPreset(next))
            {
                Debug.LogWarning("Could not apply graphics preset.");
                return;
            }
            Preset = next;
            button.Markup = $"GRAPHICS: {Preset.ToString().ToUpperInvariant()}";
            Save();
        });
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(SettingsPath) &&
                Enum.TryParse<GraphicsPreset>(JsonSerializer.Deserialize<string>(File.ReadAllText(SettingsPath)), out var preset) &&
                Enum.IsDefined(preset)) Preset = preset;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.LogWarning($"Could not load graphics settings: {error.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Preset.ToString()));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.LogWarning($"Graphics settings applied but could not be saved: {error.Message}");
        }
    }
}
