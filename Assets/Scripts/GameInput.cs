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
    public static bool Down(string action) => Map.IsDown(action);
    public static bool Pressed(string action) => Map.WasPressed(action);
    public static float Axis(string action) => Map.GetAxis(action);
    private static readonly MenuCapture Capture = new();
    public static void SetMenuCapture(bool open) => Capture.Set(open);
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
    public static Vector2 StickLook => new(Axis("StickLookHorizontal"), Axis("StickLookVertical"));
}
