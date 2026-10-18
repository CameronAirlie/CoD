using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Shared FPS mapping for gameplay and menu hotkeys.</summary>
public static class GameInput
{
    public const string DefaultAsset = "project://Input/fps.plutoinput";
    private static InputActionMap? _map;
    private static InputActionMap Map => _map ??= InputActionMap.Load(DefaultAsset);
    private static InputActionMap? _controllerMap;
    private static InputActionMap ControllerMap => _controllerMap ??= new()
    {
        Actions = Map.Actions.Select(action => new InputActionDefinition
        {
            Name = action.Name,
            Bindings = action.Bindings.Where(binding =>
                binding.Kind is InputBindingKind.GamepadAxis or InputBindingKind.GamepadButton).ToList()
        }).ToList()
    };
    public static void Configure(string asset)
    {
        _map = InputActionMap.Load(asset);
        _controllerMap = null;
    }
    public static string Prompt(string action) => InputPrompts.Binding(Map, action);
    public static string FormatPrompt(string template) => InputPrompts.Format(Map, template);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RmlDocument, RmlInputPrompts> Prompts = new();
    public static void RefreshPrompts(RmlDocument document)
    {
        var prompts = Prompts.GetValue(document, doc => {
            var result = new RmlInputPrompts(doc, () => Map);
            result.LoadDocumentBindings();
            return result;
        });
        prompts.Refresh();
    }
    public static bool Down(string action) => Map.IsDown(action);
    public static bool Pressed(string action) => Map.WasPressed(action);
    public static float Axis(string action) => Map.GetAxis(action);
    private static readonly MenuCapture Capture = new();
    public static void SetMenuCapture(bool open) { Capture.Set(open); if (open) ControllerVibration.Stop(); }
    public static bool GameplayAllowed => Capture.Allows(Input.CursorLocked,
        !Capture.RequiresNeutral || MenuButtonsReleased && DismissActions.All(action => !Down(action)));
    private static readonly string[] DismissActions =
        ["Inventory", "Pause", "Jump", "Fire", "Aim", "Reload", "Crouch", "Interact", "ThrowGrenade", "CycleThrowable", "HealthKit", "ArmourPlate"];
    private static readonly string[] MenuButtons =
        ["MenuAccept", "MenuBack", "MenuPreviousTab", "MenuNextTab", "MenuSecondary", "MenuSpace", "MenuDone"];
    public static bool MenuButtonsReleased =>
        MathF.Abs(ControllerAxis("MenuHorizontal")) < .30f && MathF.Abs(ControllerAxis("MenuVertical")) < .30f &&
        MenuButtons.All(action => MathF.Abs(ControllerAxis(action)) < .5f);
    public static bool ControllerPressed(string action) => ControllerMap.WasPressed(action);
    public static float ControllerAxis(string action) => ControllerMap.GetAxis(action);
    public static Vector2 Movement
    {
        get
        {
            var value = new Vector2(Axis("MoveHorizontal"), Axis("MoveVertical"));
            return value.LengthSquared() > 1 ? Vector2.Normalize(value) : value;
        }
    }

    // Named axes clamp to [-1, 1]; mouse motion must retain its pixel magnitude.
    private static float MouseAxis(string action)
    {
        var definition = Map.Actions.FirstOrDefault(a => a.Name == action);
        if (definition is null) return 0;
        float value = 0;
        foreach (var binding in definition.Bindings)
        {
            if (binding.Kind != InputBindingKind.MouseAxis) continue;
            var sample = binding.MouseAxis == PlutoGE.ScriptCore.MouseAxis.X ? Input.MouseDelta.X : Input.MouseDelta.Y;
            if (MathF.Abs(sample) >= binding.DeadZone) value += sample * binding.Scale;
        }
        return value;
    }
    public static Vector2 MouseLook => new(MouseAxis("MouseLookHorizontal"), MouseAxis("MouseLookVertical"));
    public static Vector2 StickLook => new(ControllerAxis("StickLookHorizontal"), ControllerAxis("StickLookVertical"));
    public static MenuFrame ReadMenuFrame() => new(
        GameInput.ControllerAxis("MenuHorizontal"), GameInput.ControllerAxis("MenuVertical"),
        GameInput.ControllerPressed("MenuAccept"), GameInput.ControllerPressed("MenuBack"),
        GameInput.ControllerPressed("MenuPreviousTab"), GameInput.ControllerPressed("MenuNextTab"),
        GameInput.ControllerPressed("MenuSecondary"), GameInput.ControllerPressed("MenuSpace"),
        GameInput.ControllerPressed("MenuDone"), GameInput.MenuButtonsReleased);
}

/// <summary>Independent envelopes prevent rapid shots from masking a damage pulse.</summary>
public sealed class VibrationMixer
{
    private readonly Vector2[] _motors = new Vector2[3];
    private readonly float[] _remaining = new float[3];
    private readonly float[] _duration = new float[3];
    public void Pulse(int channel, Vector2 motors, float seconds)
    {
        if (channel < 0 || channel >= 3 || !float.IsFinite(seconds) || seconds <= 0 || seconds > 5 ||
            !float.IsFinite(motors.X) || !float.IsFinite(motors.Y)) return;
        _motors[channel] = Vector2.Clamp(motors, Vector2.Zero, Vector2.One);
        _remaining[channel] = _duration[channel] = seconds;
    }
    public Vector2 Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) { Clear(); return Vector2.Zero; }
        var result = Vector2.Zero;
        for (int i = 0; i < 3; i++)
        {
            _remaining[i] = MathF.Max(0, _remaining[i] - seconds);
            if (_remaining[i] > 0) result = Vector2.Max(result, _motors[i] * (_remaining[i] / _duration[i]));
        }
        return result;
    }
    public void Clear() { Array.Clear(_remaining); }
}

/// <summary>Local player's controller feedback. Native timed output provides a watchdog.</summary>
public static class ControllerVibration
{
    private static readonly VibrationMixer Mixer = new();
    private static int _pad = -1;
    private static bool _outputActive;
    private static bool Allowed => Input.UsingController && Input.ActiveGamepad >= 0 &&
        PlayerSettings.Current.ControllerVibration > 0 && GameInput.GameplayAllowed;
    private static void Pulse(int channel, Vector2 motors, float seconds)
    {
        if (!Allowed) return;
        if (_pad >= 0 && _pad != Input.ActiveGamepad) Stop();
        _pad = Input.ActiveGamepad;
        Mixer.Pulse(channel, motors, seconds);
        Update(0, true); // Start immediately, including at low frame rates.
    }
    public static void Shot() => Pulse(0, new(.25f, .65f), .10f);
    public static void Damage(float amount)
    {
        if (!Allowed || !float.IsFinite(amount) || amount <= 0) return;
        float intensity = Math.Clamp(amount / 60, .35f, 1);
        Pulse(1, new Vector2(.9f, .45f) * intensity, .30f);
    }
    public static void Reload() => Pulse(2, new(.18f, .3f), .12f);
    public static void Update(float seconds, bool alive)
    {
        if (!alive || !Allowed) { Stop(); return; }
        if (_pad >= 0 && _pad != Input.ActiveGamepad) Stop();
        _pad = Input.ActiveGamepad;
        var output = Mixer.Advance(seconds) * PlayerSettings.Current.ControllerVibration;
        if (output == Vector2.Zero)
        {
            if (_outputActive) Input.StopGamepadVibration(_pad);
            _outputActive = false;
            return;
        }
        _outputActive = Input.TrySetGamepadVibration(output.X, output.Y, .10f, _pad);
    }
    public static void Stop()
    {
        Mixer.Clear();
        if (_pad >= 0) Input.StopGamepadVibration(_pad);
        _pad = -1;
        _outputActive = false;
    }
}
