using System.Numerics;
using System.Text.Json;
using System.Xml.Linq;
using CoD.Scripts;
using PlutoGE.ScriptCore;

public static class VibrationTests
{
    public static void Run(string root)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Check(new PlayerPreferences { ControllerVibration = -1 }.Validate().ControllerVibration == 0, "Vibration lower bound failed");
        Check(new PlayerPreferences { ControllerVibration = 2 }.Validate().ControllerVibration == 1, "Vibration upper bound failed");
        Check(new PlayerPreferences { ControllerVibration = float.NaN }.Validate().ControllerVibration == 1, "Vibration fallback failed");
        Check(JsonSerializer.Deserialize<PlayerPreferences>("{}")!.ControllerVibration == 1, "Old settings lost vibration default");
        var vibrationPreference = new PlayerPreferences { ControllerVibration = .35f };
        Check(JsonSerializer.Deserialize<PlayerPreferences>(JsonSerializer.Serialize(vibrationPreference)) == vibrationPreference, "Vibration roundtrip failed");
        var mixer = new VibrationMixer();
        mixer.Pulse(1, new System.Numerics.Vector2(.8f, .4f), .4f);
        mixer.Pulse(0, new System.Numerics.Vector2(.2f, .9f), .2f);
        var mixed = mixer.Advance(.1f);
        Check(System.Numerics.Vector2.Distance(mixed, new(.6f, .45f)) < .0001f, "Independent motor mixing failed");
        Check(System.Numerics.Vector2.Distance(mixer.Advance(.1f), new(.4f, .2f)) < .0001f, "Short shot masked damage envelope");
        Check(mixer.Advance(1) == System.Numerics.Vector2.Zero, "Expired vibration did not stop");
        mixer.Pulse(0, System.Numerics.Vector2.One, .2f); mixer.Clear();
        Check(mixer.Advance(0) == System.Numerics.Vector2.Zero, "Capture/disable clear retained vibration");
        mixer.Pulse(0, new(float.NaN, 1), .2f);
        Check(mixer.Advance(0) == System.Numerics.Vector2.Zero, "Invalid pulse corrupted motors");
        foreach (var fps in new[] { 30, 60, 120 })
        {
            mixer.Clear(); mixer.Pulse(2, System.Numerics.Vector2.One, .5f);
            System.Numerics.Vector2 output = default;
            for (int frame = 0; frame < fps / 4; frame++) output = mixer.Advance(1f / fps);
            float elapsed = (fps / 4) / (float)fps;
            Check(MathF.Abs(output.X - (1 - elapsed / .5f)) < .0001f, "Vibration decay depends on frame rate");
        }
        foreach (var name in new[] { "title", "pause-menu" })
        {
            var doc = XDocument.Load(Path.Combine(root, "Assets/UI", name + ".rml"));
            var slider = doc.Descendants("input").Single(e => (string?)e.Attribute("id") == "setting-vibration");
            Check((string?)slider.Attribute("min") == "0" && (string?)slider.Attribute("max") == "1" &&
                (string?)slider.Attribute("value") == "1" && (string?)slider.Attribute("step") == "0.05", "Vibration slider bounds failed");
        }
        Check(!Input.TrySetGamepadVibration(float.NaN, 0, 1) && !Input.TrySetGamepadVibration(1, 0, 6) &&
            !Input.TrySetGamepadVibration(1, 0, 1, -1), "Invalid motor request accepted");
        Console.WriteLine("Vibration envelope mixing, expiry, clearing, frame timing, validation, persistence and both menu controls passed.");

    }
}
