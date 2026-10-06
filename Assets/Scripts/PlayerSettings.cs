using System.Text.Json;
using PlutoGE.ScriptCore;
namespace CoD.Scripts;

public sealed record PlayerPreferences
{
    public int Version { get; init; } = 1;
    public GraphicsPreset Quality { get; init; } = GraphicsPreset.High;
    public bool VSync { get; init; } = true;
    public bool Fullscreen { get; init; }
    public float Master { get; init; } = 1;
    public float Effects { get; init; } = 1;
    public float Feedback { get; init; } = 1;
    public float Sensitivity { get; init; } = 1;
    public float Scale { get; init; } = 1;
    public float FieldOfView { get; init; } = 90;
    public PlayerPreferences Validate() => this with {
        Quality = Enum.IsDefined(Quality) ? Quality : GraphicsPreset.High,
        Master = Bound(Master, 0, 1), Effects = Bound(Effects, 0, 1), Feedback = Bound(Feedback, 0, 1),
        Sensitivity = Bound(Sensitivity, .1f, 3), Scale = Bound(Scale, .75f, 1.25f),
        FieldOfView = float.IsFinite(FieldOfView) ? Math.Clamp(FieldOfView, 80, 110) : 90 };
    private static float Bound(float x, float min, float max) => float.IsFinite(x) ? Math.Clamp(x, min, max) : 1;
}

/// <summary>Validated preferences, atomic persistence, and engine application independent of the menu.</summary>
public static class PlayerSettings
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlutoCombat");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    public static PlayerPreferences Current { get; private set; } = Load();
    public static float EffectsGain => Current.Master * Current.Effects;
    public static float FeedbackGain => Current.Master * Current.Feedback;
    public static void ApplyForScene()
    {
        GraphicsSettings.TryApplyPreset(Current.Quality);
        GraphicsSettings.TrySetVSync(Current.VSync);
        Application.Fullscreen = Current.Fullscreen;
        if (UISettings.IsSupported) UISettings.TrySetInterfaceScale(Current.Scale);
    }
    public static string Apply(PlayerPreferences value)
    {
        value = value.Validate();
        bool quality = GraphicsSettings.TryApplyPreset(value.Quality);
        bool vsync = GraphicsSettings.TrySetVSync(value.VSync);
        Application.Fullscreen = value.Fullscreen;
        bool fullscreen = Application.Fullscreen == value.Fullscreen;
        bool interfaceScale = UISettings.IsSupported && UISettings.TrySetInterfaceScale(value.Scale);
        Current = value with { Quality = quality ? value.Quality : Current.Quality,
            VSync = vsync ? value.VSync : Current.VSync, Fullscreen = fullscreen ? value.Fullscreen : Current.Fullscreen,
            Scale = interfaceScale ? value.Scale : Current.Scale };
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(Current));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Debug.LogWarning(e.Message); return "Applied, but preferences could not be saved."; }
        return quality && vsync && fullscreen && interfaceScale ? "Settings saved." : "Saved; some display controls are unavailable in this host.";
    }
    private static PlayerPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var value = JsonSerializer.Deserialize<PlayerPreferences>(File.ReadAllText(FilePath));
                return value is { Version: 1 } ? value.Validate() : new();
            }
            var legacy = Path.Combine(Folder, "graphics.json");
            if (File.Exists(legacy) && Enum.TryParse<GraphicsPreset>(JsonSerializer.Deserialize<string>(File.ReadAllText(legacy)), out var preset))
                return new PlayerPreferences { Quality = preset }.Validate();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { Debug.LogWarning($"Could not load settings: {e.Message}"); }
        return new();
    }
}
