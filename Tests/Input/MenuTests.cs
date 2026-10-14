using CoD.Scripts;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public static class MenuTests
{
    public static void Run(string root)
    {
        static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        var available = true;
        var activated = 0;
        var navigation = new MenuNavigation();
        navigation.Entries.AddRange([
            new("left", 0, 0, () => activated++),
            new("disabled", 0, 1, () => throw new Exception("Disabled item activated"), () => false),
            new("right", 0, 2, () => activated += 10, () => available),
            new("lower-left", 1, 0, () => { }),
            new("lower-right", 1, 2, () => { })]);
        navigation.EnsureSelection();
        Check(navigation.Selected?.Id == "left", "Initial selection is not the first enabled item.");
        navigation.Move(1, 0);
        Check(navigation.Selected?.Id == "right", "Horizontal movement did not skip a disabled purchase.");
        navigation.Move(1, 0);
        Check(navigation.Selected?.Id == "right", "Movement wrapped at the menu edge.");
        navigation.Move(0, 1);
        Check(navigation.Selected?.Id == "lower-right", "Vertical movement did not preserve the nearest column.");
        navigation.Move(0, -1); navigation.Activate();
        Check(activated == 10, "Selection activated a different item.");
        available = false; navigation.Activate();
        Check(activated == 11 && navigation.Selected?.Id == "left", "Stale purchase availability was not checked at activation.");
        var tab = "display";
        var settings = new MenuNavigation();
        var value = 90f;
        settings.Entries.Add(new("fov", 0, 0, () => { }, () => tab == "display",
            direction => value = MenuValues.Step(value, 1, 80, 110, direction)));
        settings.Entries.Add(new("volume", 0, 0, () => { }, () => tab == "audio"));
        settings.Move(1, 0);
        Check(value == 91 && settings.Selected?.Id == "fov", "Left/right should adjust the selected slider.");
        tab = "audio"; settings.EnsureSelection();
        Check(settings.Selected?.Id == "volume", "Hidden tab controls retained selection.");
        Check(MenuValues.Step(110, 1, 80, 110, 1) == 110 && MenuValues.Step(80, 1, 80, 110, -1) == 80, "FOV bounds escaped.");
        Check(MathF.Abs(MenuValues.Step(.1f, .05f, .1f, 3, 1) - .15f) < .0001f, "Slider step is misaligned.");
        var repeat = new MenuAxisRepeat();
        Check(repeat.Tick(.2f, .1f) == 0 && repeat.Tick(.6f, 0) == 1, "Stick activation threshold failed.");
        Check(repeat.Tick(.4f, .1f) == 0, "Stick hysteresis retriggered near the threshold.");
        Check(repeat.Tick(.6f, .1f) == 0 && repeat.Tick(.6f, .1f) == 0 && repeat.Tick(.6f, .1f) == 1, "Initial repeat delay failed.");
        Check(repeat.Tick(.6f, .1f) == 1, "Held navigation does not repeat.");
        Check(repeat.Tick(-.4f, .1f) == 0 && repeat.Tick(-.6f, 0) == -1, "Direction reversal threshold failed.");
        Check(repeat.Tick(float.NaN, 0) == 0 && repeat.Tick(-1, 0) == -1, "Non-finite input did not reset repeat.");
        var held = new MenuFrame(0, 0, true, false, false, false, false, false, false, false);
        var neutral = held with { Accept = false, Neutral = true };
        var guard = new MenuInputGuard();
        Check(!guard.Allow(held) && !guard.Allow(held) && !guard.Allow(neutral) && guard.Allow(held), "Menu-opening button leaked into selection.");
        guard.Reset(); Check(!guard.Allow(held), "Submenu failed to re-arm its release guard.");
        var capture = new MenuCapture();
        capture.Set(true); Check(!capture.Allows(true, true), "Open menu allowed gameplay.");
        capture.Set(false); Check(!capture.Allows(true, false), "Menu-closing button leaked into gameplay.");
        Check(capture.Allows(true, true) && capture.Allows(true, false), "Gameplay did not resume after release.");
        Check(!capture.Allows(false, true), "Unlocked cursor allowed gameplay.");
        var text = new MenuTextBuffer("1234", 6);
        text.Move(-1); text.Insert(".");
        Check(text.Text == "123.4" && text.Caret == 4, "Keyboard caret insertion failed.");
        text.Backspace(); Check(text.Text == "1234" && text.Caret == 3, "Keyboard backspace failed.");
        text.Insert("abcdef"); Check(text.Text == "123ab4", "Keyboard exceeded the field limit.");
        text.Clear(); text.Backspace(); text.Move(-1);
        Check(text.Text == "" && text.Caret == 0, "Empty keyboard edit failed.");

        var documents = new[] { "title", "pause-menu", "inventory" }.ToDictionary(name => name,
            name => XDocument.Load(Path.Combine(root, "Assets/UI", name + ".rml")));
        foreach (var (name, doc) in documents)
        {
            var ids = doc.Descendants().Attributes("id").Select(a => a.Value).ToArray();
            Check(ids.Distinct().Count() == ids.Length, $"Duplicate IDs in {name}.");
            Check(ids.Contains("menu-body"), $"Missing controller prompt root in {name}.");
        }
        foreach (var (script, document) in new[] { ("TitleScreen", "title"), ("UIManager", "pause-menu"), ("GameGraphicsSettings", "title"), ("ControllerConfirmation", "pause-menu") })
        {
            var ids = documents[document].Descendants().Attributes("id").Select(a => a.Value).ToHashSet();
            var source = File.ReadAllText(Path.Combine(root, "Assets/Scripts", script + ".cs"));
            foreach (Match match in Regex.Matches(source, "\\.Add\\(new\\(\"([^\"]+)\""))
                Check(ids.Contains(match.Groups[1].Value), $"Controller entry {match.Groups[1].Value} is missing from {document}.");
        }
        var template = File.ReadAllText(Path.Combine(root, "Assets/UI/theme.rcss.in"));
        Check(template.Contains(".controller-focus") && template.Contains("border: 2dp"), "Missing scalable controller selection styling.");
        Console.WriteLine("PASS: menu graph, availability, adjustment bounds, repeat hysteresis, release guards, text editing and RML wiring.");
    }
}
