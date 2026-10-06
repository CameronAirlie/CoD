using PlutoGE.ScriptCore;
using System.Globalization;
namespace CoD.Scripts;

/// <summary>Document-scoped settings presenter; subscriptions are owned by the document.</summary>
public static class GameGraphicsSettings
{
    private static readonly string[] Tabs = ["display", "graphics", "audio", "interface", "controls"];
    public static GraphicsPreset Preset => PlayerSettings.Current.Quality;
    public static void Bind(RmlDocument doc)
    {
        PlayerSettings.ApplyForScene();
        string selectedTab = "display";
        void Select(string selected)
        {
            selectedTab = selected;
            foreach (string tab in Tabs)
            {
                doc.Element("settings-tab-" + tab).SetClass("selected", tab == selected);
                doc.Element("settings-section-" + tab).SetClass("settings-active", tab == selected);
            }
        }
        void Refresh()
        {
            var v = PlayerSettings.Current;
            doc.Element("setting-quality").Markup = v.Quality.ToString().ToUpperInvariant();
            doc.Element("setting-vsync").Markup = v.VSync ? "ON" : "OFF";
            doc.Element("setting-fullscreen").Markup = v.Fullscreen ? "ON" : "OFF";
            foreach (var pair in new[] { ("master", v.Master), ("effects", v.Effects), ("feedback", v.Feedback), ("mouse", v.Sensitivity), ("scale", v.Scale) })
            {
                doc.Element("setting-" + pair.Item1)["value"] = pair.Item2.ToString("0.00", CultureInfo.InvariantCulture);
                doc.Element("setting-" + pair.Item1 + "-value").Markup =
                    pair.Item1 is "master" or "effects" or "feedback"
                        ? (pair.Item2 * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
                        : pair.Item2.ToString("0.00", CultureInfo.InvariantCulture) + "x";
            }
            PlayerSettings.ScaleDocument(doc, "settings-panel");
            PlayerSettings.ScaleDocument(doc, "menu", "0% 50%");
        }
        void Change(PlayerPreferences next)
        { doc.Element("settings-status").Markup = PlayerSettings.Apply(next); Refresh(); }
        doc.OnClick("graphics-preset", () => { doc.Element("settings-overlay").SetStyle("display", "flex"); Refresh(); Select(selectedTab); });
        doc.OnClick("settings-close", () => doc.Element("settings-overlay").SetStyle("display", "none"));
        foreach (string tab in Tabs) doc.OnClick("settings-tab-" + tab, () => Select(tab));
        doc.OnClick("setting-quality", () => Change(PlayerSettings.Current with { Quality = (GraphicsPreset)(((int)Preset + 1) % Enum.GetValues<GraphicsPreset>().Length) }));
        doc.OnClick("setting-vsync", () => Change(PlayerSettings.Current with { VSync = !PlayerSettings.Current.VSync }));
        doc.OnClick("setting-fullscreen", () => Change(PlayerSettings.Current with { Fullscreen = !PlayerSettings.Current.Fullscreen }));
        void Slider(string name, Func<PlayerPreferences, float> read, Func<PlayerPreferences, float, PlayerPreferences> write)
        {
            var slider = doc.Element("setting-" + name);
            slider.On("change", () => {
                if (!float.TryParse(slider["value"], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value)) return;
                var next = write(PlayerSettings.Current, value).Validate();
                // Programmatic refreshes can emit change events; avoid saving them again.
                if (Math.Abs(read(next) - read(PlayerSettings.Current)) > .0001f) Change(next);
            });
        }
        Slider("master", v => v.Master, (v, x) => v with { Master = x });
        Slider("effects", v => v.Effects, (v, x) => v with { Effects = x });
        Slider("feedback", v => v.Feedback, (v, x) => v with { Feedback = x });
        Slider("mouse", v => v.Sensitivity, (v, x) => v with { Sensitivity = x });
        Slider("scale", v => v.Scale, (v, x) => v with { Scale = x });
        doc.OnClick("settings-reset", () => Change(new()));
        Refresh(); Select("display");
    }
}
