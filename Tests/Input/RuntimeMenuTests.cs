using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CoD.Scripts;
using PlutoGE.ScriptCore;

/// <summary>Exercise the authored mappings and controllers through a simulated native input/DOM boundary.</summary>
public static unsafe class RuntimeMenuTests
{
    private static readonly HashSet<int> Down = [], Pressed = [];
    private static readonly Dictionary<(string, string), bool> Classes = [];
    private static readonly Dictionary<(string, string), string> Styles = [];
    private static readonly Dictionary<string, string> Text = [];
    private static readonly Dictionary<(string, string), string> Attributes = [];
    private static readonly nint TrueText = Marshal.StringToCoTaskMemUTF8("true");
    private static readonly nint FalseText = Marshal.StringToCoTaskMemUTF8("false");
    private static readonly List<string> Revealed = [];
    private static float _leftX, _leftY;
    private static float _rightX;
    private static int _cursor = 1;
    private static bool _mouseHeld, _spaceHeld;
    private static bool _domReady = true;
    private static bool _connected = true;
    private static nint Export(string name) => typeof(Input).Assembly.GetType("PlutoGE.ScriptCore.Native.ScriptBridge")!
        .GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.MethodHandle.GetFunctionPointer();
    private static string Decode(nint value) => Marshal.PtrToStringUTF8(value) ?? "";
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int Zero(int _) => 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int KeyDown(int key) => _spaceHeld && key == (int)KeyCode.Space ? 1 : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int MouseDown(int button) => _mouseHeld && button == (int)MouseButton.Left ? 1 : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int Connected(int _) => _connected ? 1 : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int ButtonDown(int button) => Down.Contains(button) ? 1 : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int ButtonPressed(int button) => Pressed.Contains(button) ? 1 : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int Flag() => _cursor;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static void SetFlag(int value) => _cursor = value;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static Vector3 Vector() => default;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static float Axis(int packed) =>
        (GamepadAxis)(packed & 0xffff) switch { GamepadAxis.LeftX => _leftX, GamepadAxis.LeftY => _leftY, GamepadAxis.RightX => _rightX, _ => 0 };
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int SetText(nint _, nint id, nint text)
    { Text[Decode(id)] = Decode(text); return 1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int SetClass(nint _, nint id, nint name, int enabled)
    { if (!_domReady) return 0; Classes[(Decode(id), Decode(name))] = enabled != 0; return 1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int SetAttribute(nint _, nint id, nint name, nint value)
    { Attributes[(Decode(id), Decode(name))] = Decode(value); return 1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static nint GetAttribute(nint _, nint id, nint name) =>
        Attributes.TryGetValue((Decode(id), Decode(name)), out var value) ? (value == "true" ? TrueText : FalseText) : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int SetStyle(nint _, nint id, nint name, nint value)
    { Styles[(Decode(id), Decode(name))] = Decode(value); return 1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int Event(nint _, nint __, nint ___) => 1;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] private static int Reveal(nint _, nint id)
    { Revealed.Add(Decode(id)); return 1; }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static float PreviousAxis(int axis) => 0;
    private static int _activeDevice = 1;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Device() => _connected ? _activeDevice : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ActivePad() => _connected ? 0 : -1;
    private static MenuFrame Frame(GamepadButton? button = null, bool held = false, float x = 0, float y = 0)
    {
        Down.Clear(); Pressed.Clear(); _leftX = x; _leftY = y;
        if (button is { } selected)
        {
            Down.Add((int)selected);
            if (!held) Pressed.Add((int)selected);
        }
        return GameInput.ReadMenuFrame();
    }
    public static void Run(InputActionMap map)
    {
        // Registration uses the same unmanaged signatures as the engine smoke harness.
        var register = (delegate* unmanaged[Cdecl]<nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,int>)Export("RegisterInputApi");
        var zero = (nint)(delegate* unmanaged[Cdecl]<int,int>)&Zero;
        var vector = (nint)(delegate* unmanaged[Cdecl]<Vector3>)&Vector;
        var flag = (nint)(delegate* unmanaged[Cdecl]<int>)&Flag;
        Check(register((nint)(delegate* unmanaged[Cdecl]<int,int>)&KeyDown, zero, zero,
            (nint)(delegate* unmanaged[Cdecl]<int,int>)&MouseDown, zero, zero, vector, vector, vector, flag, flag,
            (nint)(delegate* unmanaged[Cdecl]<int,void>)&SetFlag,
            (nint)(delegate* unmanaged[Cdecl]<int,int>)&Connected,
            (nint)(delegate* unmanaged[Cdecl]<int,int>)&ButtonDown,
            (nint)(delegate* unmanaged[Cdecl]<int,int>)&ButtonPressed,
            (nint)(delegate* unmanaged[Cdecl]<int,float>)&Axis) == 1, "Input callback registration failed.");
        typeof(GameInput).GetField("_map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, map);
        map.Actions.Single(action => action.Name == "StickLookHorizontal").Bindings.Add(new() { Kind = InputBindingKind.Key, Key = KeyCode.Space });
        _spaceHeld = true;
        Check(GameInput.StickLook == Vector2.Zero, "Keyboard binding was treated as controller look.");
        _spaceHeld = false; _rightX = .6f;
        Check(MathF.Abs(GameInput.StickLook.X - .6f) < .0001f, "Real controller look was filtered out.");
        _rightX = 0;
        var rml = (delegate* unmanaged[Cdecl]<nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,int>)Export("RegisterRmlUiApi");
        Check(rml(0, 0, (nint)(delegate* unmanaged[Cdecl]<nint,nint,nint,int>)&SetText, 0,
            (nint)(delegate* unmanaged[Cdecl]<nint,nint,nint,nint,int>)&SetAttribute,
            (nint)(delegate* unmanaged[Cdecl]<nint,nint,nint,nint>)&GetAttribute,
            (nint)(delegate* unmanaged[Cdecl]<nint,nint,nint,int,int>)&SetClass,
            (nint)(delegate* unmanaged[Cdecl]<nint,nint,nint,nint,int>)&SetStyle,
            (nint)(delegate* unmanaged[Cdecl]<nint,nint,nint,int>)&Event, 0, 0, 0, 0, 0, 0, 0) == 1,
            "DOM callback registration failed.");
        var navigation = (delegate* unmanaged[Cdecl]<nint,int>)Export("RegisterRmlNavigationApi");
        Check(navigation((nint)(delegate* unmanaged[Cdecl]<nint,nint,int>)&Reveal) == 1, "Reveal callback registration failed.");

        var history = (delegate* unmanaged[Cdecl]<nint,nint,nint,int>)Export("RegisterInputHistoryApi");
        Check(history((nint)(delegate* unmanaged[Cdecl]<int,float>)&PreviousAxis,
            (nint)(delegate* unmanaged[Cdecl]<int>)&Device,
            (nint)(delegate* unmanaged[Cdecl]<int>)&ActivePad) == 1, "Device registration failed.");
        var doc = new RmlDocument("UI/title.rml");
        var accepted = 0;
        var backed = 0;
        var menu = new CoD.Scripts.ControllerMenu(doc) { Back = () => backed++ };
        menu.Add(new("first", 0, 0, () => accepted++));
        menu.Add(new("second", 1, 0, () => accepted += 10));
        _domReady = false; menu.Open();
        _domReady = true; menu.Update(Frame(GamepadButton.A));
        Check(accepted == 0, "Opening A press activated a button.");
        Check(Classes[("first", "controller-focus")], "Selection did not recover after late native document creation.");
        menu.Update(Frame()); menu.Update(Frame(GamepadButton.A));
        menu.Update(Frame(GamepadButton.A, true));
        Check(accepted == 1, "A acceptance was lost or repeated while held.");
        menu.Update(Frame(y: 1));
        Check(menu.Navigation.Selected?.Id == "second" && Classes[("second", "controller-focus")] && Revealed.Last() == "second",
            "Stick selection was not highlighted and revealed.");
        var revealCount = Revealed.Count;
        menu.Update(Frame()); Check(Revealed.Count == revealCount, "Unchanged selection scrolls every frame.");
        Attributes.Remove(("second", "data-controller-selected")); Classes.Clear();
        menu.Update(Frame()); Check(Classes[("second", "controller-focus")], "Native hot reload lost controller selection.");
        menu.Update(Frame(GamepadButton.B)); Check(backed == 1, "B did not go back.");
        menu.Close(); Check(!Classes[("second", "controller-focus")], "Closed menu retained controller highlight.");
        menu.Open("first"); menu.Update(Frame());
        _connected = false; menu.Update(Frame());
        _connected = true; menu.Update(Frame(GamepadButton.A));
        Check(accepted == 1, "Hotplugged controller accepted a button held during connection.");
        menu.Update(Frame()); menu.Update(Frame(GamepadButton.A));
        Check(accepted == 2, "Controller did not recover after hotplug/release.");
        menu.Close();

        _activeDevice = 0;
        menu.RefreshHints();
        Check(!Classes[("menu-body", "controller-connected")], "Idle connected pad overrode keyboard/mouse hints.");
        _activeDevice = 1;
        menu.RefreshHints();
        Check(Classes[("menu-body", "controller-connected")], "Controller activity did not restore controller hints.");

        var keyboard = new ControllerKeyboard(doc);
        string? committed = null;
        keyboard.Open("CALLSIGN", "", 20, value => committed = value);
        keyboard.Update(Frame(GamepadButton.A));
        keyboard.Update(Frame()); keyboard.Update(Frame(GamepadButton.A));
        Check(Text["keyboard-value"].StartsWith("1"), "Controller keyboard did not type selected key.");
        keyboard.Update(Frame(GamepadButton.X));
        Check(!Text["keyboard-value"].StartsWith("1"), "X did not delete a character.");
        keyboard.Update(Frame(GamepadButton.Y)); keyboard.Update(Frame(GamepadButton.Start));
        Check(committed == " " && !keyboard.IsOpen, "Y space/Start commit failed.");
        keyboard.Open("HOST ADDRESS", "127.0.0.1", 253, value => committed = value);
        keyboard.Update(Frame()); keyboard.Update(Frame(GamepadButton.B));
        Check(committed == " " && !keyboard.IsOpen, "B cancel committed a text edit.");

        var confirmation = new ControllerConfirmation(doc);
        var left = false;
        confirmation.Open("LEAVE MATCH?", "Disconnect?", () => left = true);
        confirmation.Update(Frame(GamepadButton.A)); Check(confirmation.IsOpen && !left, "Opening press accepted confirmation.");
        confirmation.Update(Frame()); confirmation.Update(Frame(GamepadButton.A));
        Check(!confirmation.IsOpen && !left, "Confirmation should initially select Cancel.");
        confirmation.Open("LEAVE MATCH?", "Disconnect?", () => left = true);
        confirmation.Update(Frame()); confirmation.Update(Frame(x: 1));
        confirmation.Update(Frame()); confirmation.Update(Frame(GamepadButton.A));
        Check(left && Styles[("confirm-overlay", "display")] == "none", "Explicit confirmation did not execute and close.");

        GameInput.SetMenuCapture(true); Frame();
        Check(!GameInput.GameplayAllowed, "Menu did not capture gameplay.");
        GameInput.SetMenuCapture(false); Frame(GamepadButton.A);
        Check(!GameInput.GameplayAllowed, "A menu dismissal leaked into Jump.");
        Frame(); Check(GameInput.GameplayAllowed, "Neutral input did not release gameplay capture.");
        GameInput.SetMenuCapture(false); _mouseHeld = true;
        Check(!GameInput.GameplayAllowed, "Mouse resume click leaked into firing.");
        _mouseHeld = false; _spaceHeld = true;
        Check(!GameInput.GameplayAllowed, "Keyboard Space activation leaked into jumping.");
        _spaceHeld = false; Check(GameInput.GameplayAllowed, "KBM release failed to restore gameplay.");
        _cursor = 0; Check(!GameInput.GameplayAllowed, "Unlocked cursor failed to block gameplay."); _cursor = 1;
        Marshal.FreeCoTaskMem(TrueText); Marshal.FreeCoTaskMem(FalseText);
        Console.WriteLine("PASS: actual asset/controller sampling, A/B navigation, highlight/reveal, controller keyboard, confirmation and gameplay capture through native callbacks.");
    }
}
