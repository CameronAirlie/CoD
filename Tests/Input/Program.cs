using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PlutoGE.ScriptCore;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var options = new JsonSerializerOptions();
options.Converters.Add(new JsonStringEnumConverter());
var map = JsonSerializer.Deserialize<InputActionMap>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fps.plutoinput")), options)!;
var actions = map.Actions.ToDictionary(a => a.Name);
Check(actions.Count == map.Actions.Count, "Duplicate action names.");
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
MenuTests.Run(root);
AimAssistTests.Run(root);
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Assets/Scripts"), "*.cs"))
{
    var source = File.ReadAllText(file);
    foreach (Match match in Regex.Matches(source, "(?:GameInput\\.(?:Down|Pressed|Axis)|MouseAxis)\\(\"([^\"]+)\"\\)"))
        Check(actions.ContainsKey(match.Groups[1].Value), $"Missing action in {file}: {match.Groups[1].Value}");
    if (!file.EndsWith("GameInput.cs"))
        Check(!Regex.IsMatch(source, "Input\\.Is(?:Key|MouseButton)(?:Down|Pressed)\\("), $"Unmigrated input in {file}");
}
foreach (var action in map.Actions)
{
    Check(action.Bindings.Count > 0, $"Unbound action: {action.Name}");
    foreach (var binding in action.Bindings)
    {
        Check(float.IsFinite(binding.Scale) && binding.Scale != 0, $"Invalid scale: {action.Name}");
        Check(binding.DeadZone >= 0 && binding.DeadZone < 1, $"Invalid dead zone: {action.Name}");
    }
    if (action.Name.StartsWith("MouseLook") || action.Name.StartsWith("StickLook") || action.Name.StartsWith("Weapon")) continue;
    Check(action.Bindings.Any(b => b.Kind is InputBindingKind.Key or InputBindingKind.MouseButton), $"Missing KBM binding: {action.Name}");
    Check(action.Bindings.Any(b => b.Kind is InputBindingKind.GamepadAxis or InputBindingKind.GamepadButton), $"Missing controller binding: {action.Name}");
}
Check(actions["MoveVertical"].Bindings.Single(b => b.Kind == InputBindingKind.GamepadAxis).Scale == -1, "Forward stick direction is inverted.");
Check(actions["Aim"].Bindings.Any(b => b.Axis == GamepadAxis.LeftTrigger && b.Kind == InputBindingKind.GamepadAxis), "ADS trigger missing.");
Check(actions["Fire"].Bindings.Any(b => b.Axis == GamepadAxis.RightTrigger && b.Kind == InputBindingKind.GamepadAxis), "Fire trigger missing.");
Check(File.ReadAllText(Path.Combine(root, "CoDplutoproject.plutoproject")).Contains("BUILD_ALWAYS_INCLUDE\tproject://Input/fps.plutoinput"), "Cooked build omits FPS input asset.");
RuntimeMenuTests.Run(map);
Console.WriteLine($"FPS input checks passed: {actions.Count} actions, complete migrated action references and dual-device bindings.");

foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Assets/UI"), "*.rml"))
{
    var document = System.Xml.Linq.XDocument.Load(path);
    foreach (var attribute in document.Descendants().Attributes("data-input-prompt"))
    {
        Check(attribute.Parent!.Attribute("id") is not null, $"Prompt has no stable ID in {path}");
        foreach (Match token in Regex.Matches(attribute.Value, @"\{([A-Za-z0-9_.-]+)\}"))
            Check(actions.ContainsKey(token.Groups[1].Value), $"Unknown prompt action {token.Groups[1].Value} in {path}");
    }
}
Console.WriteLine("PASS: RML prompt tokens reference registered asset actions.");
