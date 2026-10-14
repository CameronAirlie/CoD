using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using CoD.Scripts;

public static class AimAssistTests
{
    public static void Run(string root)
    {
        static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        var stick = new Vector2(.6f, -.2f);
        Check(ControllerAimAssist.Apply(stick, Vector2.Zero, true, 0, 0) == stick, "Disabled assist modified aim.");
        Check(ControllerAimAssist.Apply(stick, Vector2.Zero, false, 1, 0) == stick, "Hip fire acquired ADS assist.");
        Check(ControllerAimAssist.Apply(stick, new(.001f, 0), true, 1, 0) == stick, "Mixed mouse input acquired controller assist.");
        Check(ControllerAimAssist.Apply(Vector2.Zero, Vector2.Zero, true, 1, 0) == Vector2.Zero, "Assist moved a stationary stick.");
        Check(ControllerAimAssist.Apply(stick, Vector2.Zero, true, 1, ControllerAimAssist.ConeDegrees) == stick, "Assist exceeded the cone.");
        Check(ControllerAimAssist.Apply(stick, Vector2.Zero, true, 1, float.PositiveInfinity) == stick, "No visible target altered aiming.");
        Check(ControllerAimAssist.Apply(stick, Vector2.Zero, true, float.NaN, 0) == stick, "Non-finite settings corrupted aim.");
        var mild = ControllerAimAssist.Apply(stick, Vector2.Zero, true, .5f, 0);
        var full = ControllerAimAssist.Apply(stick, Vector2.Zero, true, 1, 0);
        Check(Vector2.Distance(mild, stick * .875f) < .000001f && Vector2.Distance(full, stick * .75f) < .000001f,
            "Slowdown cap or default strength changed.");
        var edge = ControllerAimAssist.Apply(stick, Vector2.Zero, true, 1, 3);
        Check(edge.Length() > full.Length() && edge.Length() < stick.Length(), "Cone falloff is discontinuous.");
        Check(MathF.Abs(full.X / stick.X - full.Y / stick.Y) < .000001f, "Assist steered instead of scaling stick motion.");
        foreach (var fps in new[] { 30, 60, 120 })
        {
            var total = Vector2.Zero;
            for (var frame = 0; frame < fps; frame++) total += mild * 180 / fps;
            Check(Vector2.Distance(total, mild * 180) < .001f, "Assist depends on frame rate.");
        }
        var forward = -Vector3.UnitZ;
        Check(ControllerAimAssist.Angle(Vector3.Zero, forward, forward * 10) < .001f, "Centered target did not qualify.");
        Check(ControllerAimAssist.Angle(Vector3.Zero, forward, -forward * 10) > 170, "Behind-camera target qualified.");
        Check(float.IsPositiveInfinity(ControllerAimAssist.Angle(Vector3.Zero, forward, forward * 36)), "Out-of-range target qualified.");
        Check(float.IsPositiveInfinity(ControllerAimAssist.Angle(Vector3.Zero, forward, new(float.NaN, 0, 0))), "Invalid target position qualified.");
        Check(ControllerAimAssist.IsOpponent(PlayerTeam.Alpha, PlayerTeam.Bravo, true), "Opponent did not qualify.");
        Check(!ControllerAimAssist.IsOpponent(PlayerTeam.Alpha, PlayerTeam.Alpha, true) &&
            !ControllerAimAssist.IsOpponent(PlayerTeam.Alpha, PlayerTeam.Bravo, false) &&
            !ControllerAimAssist.IsOpponent(PlayerTeam.Spectator, PlayerTeam.Bravo, true) &&
            !ControllerAimAssist.IsOpponent(PlayerTeam.Alpha, PlayerTeam.Spectator, true), "Friendly/dead/spectator target qualified.");
        Check(new PlayerPreferences { ControllerAimAssist = -1 }.Validate().ControllerAimAssist == 0, "Assist minimum failed.");
        Check(new PlayerPreferences { ControllerAimAssist = 9 }.Validate().ControllerAimAssist == 1, "Assist maximum failed.");
        Check(new PlayerPreferences { ControllerAimAssist = float.NaN }.Validate().ControllerAimAssist == .5f, "Assist fallback failed.");
        Check(JsonSerializer.Deserialize<PlayerPreferences>("{}")!.ControllerAimAssist == .5f, "Older settings lost the assist default.");
        var preferences = new PlayerPreferences { ControllerAimAssist = .25f };
        Check(JsonSerializer.Deserialize<PlayerPreferences>(JsonSerializer.Serialize(preferences)) == preferences, "Assist persistence roundtrip failed.");
        XElement? previous = null;
        foreach (var name in new[] { "title", "pause-menu" })
        {
            var doc = XDocument.Load(Path.Combine(root, "Assets/UI", name + ".rml"));
            var controls = doc.Descendants().Single(e => (string?)e.Attribute("id") == "settings-section-controls");
            var slider = controls.Descendants("input").Single(e => (string?)e.Attribute("id") == "setting-aim-assist");
            Check((string?)slider.Attribute("min") == "0" && (string?)slider.Attribute("max") == "1" &&
                (string?)slider.Attribute("step") == "0.05" && (string?)slider.Attribute("value") == "0.5", "Aim assist slider bounds/default failed.");
            Check(controls.Descendants().Any(e => (string?)e.Attribute("id") == "setting-aim-assist-value"), "Assist readout missing.");
            if (previous is not null) Check(XNode.DeepEquals(previous, controls), "Title and pause controls settings diverged.");
            previous = controls;
        }
        Console.WriteLine("PASS: controller-only friction, no snap/stationary/mouse assistance, cone/range/falloff, team policy, frame-rate independence, preference persistence and both settings menus.");
    }
}
