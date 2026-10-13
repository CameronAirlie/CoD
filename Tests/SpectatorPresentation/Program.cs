using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CoD.Scripts;
using PlutoGE.ScriptCore;

unsafe class Program
{
    static readonly Type Bridge = typeof(GameObject).Assembly.GetType("PlutoGE.ScriptCore.Native.ScriptBridge")!;
    static readonly Dictionary<uint, bool> Enabled = new();
    static Vector3 cameraPosition, cameraRotation;
    static float fov;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static int Has(uint id, int kind) => Enabled.ContainsKey(id) ? 1 : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static int GetEnabled(uint id, int kind) => Enabled.GetValueOrDefault(id) ? 1 : 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static void SetEnabled(uint id, int kind, int value) => Enabled[id] = value != 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static Vector3 GetVector(uint id) => Vector3.Zero;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static void SetVector(uint id, Vector3 value) { }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static void SetWorldPosition(uint id, Vector3 value) => cameraPosition = value;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static void SetWorldRotation(uint id, Vector3 value) => cameraRotation = value;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static int GetInt(uint id) => 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static void SetInt(uint id, int value) { }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static float GetFov(uint id) => fov;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static void SetFov(uint id, float value) => fov = value;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static nint GetName(uint id) => 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static nint GetTag(uint id, int index) => 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static uint FindName(byte* name) => 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static int CountTag(byte* tag) => 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static uint FindTag(byte* tag, int index) => 0;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static Quaternion GetQuaternion(uint id) => Quaternion.Identity;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])] static void SetQuaternion(uint id, Quaternion value) { }
    static nint Api(string name) => Bridge.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.MethodHandle.GetFunctionPointer();
    static object Construct(Type type, params object[] args) => Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null)!;
    static GameObject Object(uint id) => (GameObject)Construct(typeof(GameObject), id);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        var component = (delegate* unmanaged[Cdecl]<nint,nint,nint,int>)Api("RegisterComponentApi");
        Check(component((nint)(delegate* unmanaged[Cdecl]<uint,int,int>)&Has, (nint)(delegate* unmanaged[Cdecl]<uint,int,int>)&GetEnabled,
            (nint)(delegate* unmanaged[Cdecl]<uint,int,int,void>)&SetEnabled) == 1, "Component mock registration failed.");
        var mesh = (MeshComponent)Construct(typeof(MeshComponent), 10u);
        var bot = new EnemySoldierBot();
        Enabled[10] = true; Enabled[11] = false;
        Set(bot, "rifleWeapon", Object(11));
        bot.SetSpectatorHidden(true); // Body reference is not ready on the first frame.
        Set(bot, "spectatorBodyMesh", mesh);
        bot.SetSpectatorHidden(true);
        Check(!Enabled[10] && !Enabled[11], "Late-bound body or weapon stayed visible.");
        Enabled[10] = true; // Another presentation system toggles rendering.
        bot.SetSpectatorHidden(true);
        Check(!Enabled[10], "Observed body was allowed to reappear.");
        bot.SetSpectatorHidden(false);
        Check(Enabled[10] && !Enabled[11], "Stopping spectating failed to restore original render states.");
        bot.SetSpectatorHidden(false);

        // Exercise the real controller LateUpdate with a tiny native-transform adapter.
        var vector = (nint)(delegate* unmanaged[Cdecl]<uint,Vector3>)&GetVector;
        var setter = (nint)(delegate* unmanaged[Cdecl]<uint,Vector3,void>)&SetVector;
        var integer = (nint)(delegate* unmanaged[Cdecl]<uint,int>)&GetInt;
        var intSetter = (nint)(delegate* unmanaged[Cdecl]<uint,int,void>)&SetInt;
        var gameObject = (delegate* unmanaged[Cdecl]<nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,nint,int>)Api("RegisterGameObjectApi");
        Check(gameObject(vector,vector,setter,vector,setter,vector,setter,vector,vector,integer,intSetter,integer,
            (nint)(delegate* unmanaged[Cdecl]<uint,int,nint>)&GetTag,integer,
            (nint)(delegate* unmanaged[Cdecl]<uint,nint>)&GetName,
            (nint)(delegate* unmanaged[Cdecl]<byte*,uint>)&FindName,
            (nint)(delegate* unmanaged[Cdecl]<byte*,int>)&CountTag,
            (nint)(delegate* unmanaged[Cdecl]<byte*,int,uint>)&FindTag,
            (nint)(delegate* unmanaged[Cdecl]<uint,Vector3,void>)&SetWorldPosition,vector,
            (nint)(delegate* unmanaged[Cdecl]<uint,Vector3,void>)&SetWorldRotation,
            (nint)(delegate* unmanaged[Cdecl]<uint,Quaternion>)&GetQuaternion,
            (nint)(delegate* unmanaged[Cdecl]<uint,Quaternion,void>)&SetQuaternion) == 1, "Transform mock registration failed.");
        var cameraApi = (delegate* unmanaged[Cdecl]<nint,nint,nint,nint,int>)Api("RegisterCameraComponentApi");
        cameraApi(integer,intSetter,(nint)(delegate* unmanaged[Cdecl]<uint,float>)&GetFov,(nint)(delegate* unmanaged[Cdecl]<uint,float,void>)&SetFov);
        var session = new MultiplayerSession(); var health = new PlayerHealth(); Set(health,"_dead",true); Set(session,"_playerHealth",health);
        var match = new MatchSnapshot(MatchPhase.Playing,60,10,0,0,1,[new(1,"Observer",PlayerTeam.Alpha,0,0,false,Alive:false),new(2,"Ally",PlayerTeam.Alpha,0,0,false)]);
        typeof(MultiplayerSession).GetProperty("CurrentMatch")!.SetValue(session,match);
        var view = new SpectatorViewState(2,1,1,12,1.7f,8,-20,120,0,60,1,10,50,false,false,false,.2f,100,100,0,0,800,0,0,0,0,0,0,false,0,1,0,0,0,"frag");
        var views = (IDictionary)typeof(MultiplayerSession).GetField("_spectatorViews",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)!;
        var orientation = SpectatorCameraMath.FromHeading(-20,120);
        var euler = SpectatorCameraMath.ToEngineEuler(orientation);
        view = view with { Pitch = euler.X, Yaw = euler.Y, Roll = euler.Z,
            OrientationX = orientation.X, OrientationY = orientation.Y, OrientationZ = orientation.Z, OrientationW = orientation.W };
        views.Add(2,(view,0f));
        var proxies = (IDictionary)typeof(MultiplayerSession).GetField("_remotePlayers",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(session)!;
        var remoteType = typeof(MultiplayerSession).GetNestedType("RemotePlayer",BindingFlags.NonPublic)!;
        proxies.Add(2,Construct(remoteType,Object(20),Vector3.Zero,0f));
        var controller = new PlayerController();
        Set(controller,"_dead",true); Set(controller,"_spectatorPeer",2); Set(controller,"_multiplayer",session);
        Set(controller,"camera",Construct(typeof(CameraComponent),30u));
        cameraPosition = new(0,.12f,0); cameraRotation = new(6,0,28);
        controller.OnLateUpdate(1f/60);
        Check(cameraPosition == view.Position && MathF.Abs(Quaternion.Dot(SpectatorCameraMath.FromEngineEuler(cameraRotation),orientation)) > .99999f && fov == 60, "Death/physics pose survived spectator LateUpdate.");
        // Simulate subsequent physics/rig drift and verify every late frame corrects it.
        cameraPosition.Y = -.5f; cameraRotation.Z = 28;
        controller.OnLateUpdate(1f/60);
        Check(cameraPosition == view.Position && MathF.Abs(Vector3.Transform(Vector3.UnitX,SpectatorCameraMath.FromEngineEuler(cameraRotation)).Y) < .0001f, "Spectator pose was not reapplied on the render timeline.");
        var instances = (IDictionary)Bridge.GetField("InstancesByEntity",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        instances.Add(20u,new List<ScriptBehaviour> { bot });
        Enabled[10] = true;
        controller.OnLateUpdate(1f/60);
        Check(!Enabled[10], "Controller never retried binding a late-created body script.");
        var replacement = new EnemySoldierBot(); Enabled[12] = true;
        Set(replacement,"spectatorBodyMesh",Construct(typeof(MeshComponent),12u));
        instances.Add(21u,new List<ScriptBehaviour> { replacement });
        proxies[2] = Construct(remoteType,Object(21),Vector3.Zero,0f);
        controller.OnLateUpdate(1f/60);
        Check(Enabled[10] && !Enabled[12], "Same-peer proxy replacement failed to restore/hide the correct body.");
        controller.ExitDeathState();
        Check(Enabled[12], "Respawn left the observed body invisible.");
        Console.WriteLine("PASS: late-bound mesh hiding/restoration and render-timeline camera height, rotation and FOV.");
    }
}
