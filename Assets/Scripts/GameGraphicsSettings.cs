using PlutoGE.ScriptCore;
using System.Globalization;
namespace CoD.Scripts;

/// <summary>Document-scoped settings presenter shared by mouse and controller actions.</summary>
public static class GameGraphicsSettings
{
    public static GraphicsPreset Preset => PlayerSettings.Current.Quality;
    public static SettingsMenu Bind(RmlDocument doc) => new(doc);
}

public sealed class SettingsMenu
{
    private static readonly string[] Tabs = ["display", "graphics", "audio", "interface", "controls"];
    private readonly RmlDocument _document;
    private readonly ControllerMenu _menu;
    private string _selectedTab = "display";
    public bool IsOpen { get; private set; }
    public SettingsMenu(RmlDocument doc)
    {
        _document = doc;
        _menu = new(doc) { Back = Close, Tab = CycleTab };
        PlayerSettings.ApplyForScene();
        doc.OnClick("graphics-preset", Open);
        foreach (string tab in Tabs) doc.OnClick("settings-tab-" + tab, () => { _menu.UsePointer(); SelectTab(tab); });
        _menu.Add(new("setting-vsync", 0, 0, () => Change(PlayerSettings.Current with { VSync = !PlayerSettings.Current.VSync }),
            () => _selectedTab == "display", direction => Change(PlayerSettings.Current with { VSync = direction > 0 })));
        _menu.Add(new("setting-fullscreen", 1, 0, () => Change(PlayerSettings.Current with { Fullscreen = !PlayerSettings.Current.Fullscreen }),
            () => _selectedTab == "display", direction => Change(PlayerSettings.Current with { Fullscreen = direction > 0 })));
        _menu.Add(new("setting-quality", 0, 0, () => Quality(1), () => _selectedTab == "graphics", Quality));
        Slider("master", "audio", 0, 0, 1, .01f, v => v.Master, (v, x) => v with { Master = x });
        Slider("effects", "audio", 1, 0, 1, .01f, v => v.Effects, (v, x) => v with { Effects = x });
        Slider("feedback", "audio", 2, 0, 1, .01f, v => v.Feedback, (v, x) => v with { Feedback = x });
        Slider("mouse", "controls", 0, .1f, 3, .05f, v => v.Sensitivity, (v, x) => v with { Sensitivity = x });
        Slider("controller", "controls", 1, .1f, 3, .05f, v => v.ControllerSensitivity, (v, x) => v with { ControllerSensitivity = x });
        Slider("aim-assist", "controls", 3, 0, 1, .05f, v => v.ControllerAimAssist, (v, x) => v with { ControllerAimAssist = x });
        Slider("vibration", "controls", 2, 0, 1, .05f, v => v.ControllerVibration, (v, x) => v with { ControllerVibration = x });
        Slider("fov", "display", 2, 80, 110, 1, v => v.FieldOfView, (v, x) => v with { FieldOfView = x });
        Slider("scale", "interface", 0, .75f, 1.25f, .01f, v => v.Scale, (v, x) => v with { Scale = x });
        _menu.Add(new("settings-reset", 10, 0, () => Change(new())));
        _menu.Add(new("settings-close", 10, 1, Close));
        Refresh(); SelectTab("display");
    }
    public void Open()
    {
        IsOpen = true;
        _document.Element("settings-overlay").SetStyle("display", "flex");
        Refresh(); SelectTab(_selectedTab);
        _menu.Open();
    }
    public void Close()
    {
        IsOpen = false;
        _menu.Close();
        _document.Element("settings-overlay").SetStyle("display", "none");
    }
    public void Update(MenuFrame frame) { if (IsOpen) _menu.Update(frame); }
    private void CycleTab(int direction)
    {
        var index = Array.IndexOf(Tabs, _selectedTab);
        SelectTab(Tabs[(index + direction + Tabs.Length) % Tabs.Length]);
    }
    private void SelectTab(string selected)
    {
        _selectedTab = selected;
        foreach (string tab in Tabs)
        {
            _document.Element("settings-tab-" + tab).SetClass("selected", tab == selected);
            _document.Element("settings-section-" + tab).SetClass("settings-active", tab == selected);
        }
        _menu.Navigation.EnsureSelection();
        var first = _menu.Navigation.Entries.FirstOrDefault(entry => entry.Enabled && entry.Row < 10);
        if (first is not null) _menu.Navigation.Select(first.Id);
    }
    private void Quality(int direction)
    {
        var count = Enum.GetValues<GraphicsPreset>().Length;
        Change(PlayerSettings.Current with { Quality = (GraphicsPreset)(((int)GameGraphicsSettings.Preset + direction + count) % count) });
    }
    private void Change(PlayerPreferences next)
    {
        next = next.Validate();
        if (next != PlayerSettings.Current) _document.Element("settings-status").Markup = PlayerSettings.Apply(next);
        Refresh();
    }
    private void Slider(string name, string tab, int row, float min, float max, float step,
        Func<PlayerPreferences, float> read, Func<PlayerPreferences, float, PlayerPreferences> write)
    {
        var slider = _document.Element("setting-" + name);
        slider.On("change", () =>
        {
            if (!float.TryParse(slider["value"], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value)) return;
            var next = write(PlayerSettings.Current, value).Validate();
            // Programmatic refreshes can emit change events; avoid saving them again.
            if (Math.Abs(read(next) - read(PlayerSettings.Current)) > .0001f) Change(next);
        });
        _menu.Add(new(slider.Id, row, 0, () => { }, () => _selectedTab == tab,
            direction => Change(write(PlayerSettings.Current, MenuValues.Step(read(PlayerSettings.Current), step, min, max, direction)))), false);
    }
    private void Refresh()
    {
        var v = PlayerSettings.Current;
        _document.Element("setting-quality").Markup = v.Quality.ToString().ToUpperInvariant();
        _document.Element("setting-vsync").Markup = v.VSync ? "ON" : "OFF";
        _document.Element("setting-fullscreen").Markup = v.Fullscreen ? "ON" : "OFF";
        foreach (var pair in new[] { ("master", v.Master), ("effects", v.Effects), ("feedback", v.Feedback), ("mouse", v.Sensitivity), ("controller", v.ControllerSensitivity), ("aim-assist", v.ControllerAimAssist), ("vibration", v.ControllerVibration), ("scale", v.Scale), ("fov", v.FieldOfView) })
        {
            _document.Element("setting-" + pair.Item1)["value"] = pair.Item2.ToString("0.00", CultureInfo.InvariantCulture);
            _document.Element("setting-" + pair.Item1 + "-value").Markup =
                pair.Item1 is "master" or "effects" or "feedback" or "aim-assist" or "vibration"
                    ? (pair.Item2 * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
                    : pair.Item1 == "fov" ? pair.Item2.ToString("0", CultureInfo.InvariantCulture) + "&#176;"
                    : pair.Item2.ToString("0.00", CultureInfo.InvariantCulture) + "x";
        }
    }
}
