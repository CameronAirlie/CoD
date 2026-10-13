using System.Numerics;

namespace CoD.Scripts;

public enum MatchPhase { Waiting, Warmup, Playing, Results }
public enum PlayerTeam { Alpha, Bravo, Spectator }
public enum MatchMode { TeamDeathmatch, Hardpoint, Defusal }
public sealed record MatchPlayer(int PeerId, string Username, PlayerTeam Team, int Kills,
    int Deaths, bool IsBot, float ObjectiveSeconds = 0, bool Alive = true);
public sealed record HardpointSnapshot(int Index, string Name, float X, float Y, float Z,
    float Radius, float SecondsRemaining, PlayerTeam? Owner, bool Contested)
{
    public Vector3 Position => new(X, Y, Z);
}
public sealed record MatchSnapshot(MatchPhase Phase, float SecondsRemaining, int ScoreLimit,
    int AlphaScore, int BravoScore, int LocalPeerId, MatchPlayer[] Players,
    MatchMode Mode = MatchMode.TeamDeathmatch, HardpointSnapshot? Objective = null, DefusalSnapshot? Defusal = null);
public sealed record KillFeedEntry(string Killer, string Victim, PlayerTeam KillerTeam);

public readonly record struct HardpointSite(string Name, Vector3 Position);
public readonly record struct ObjectiveParticipant(PlayerTeam Team, Vector3 Position, bool Alive);

/// <summary>Role-aware viewing policy shared by transport, camera adapters and engine-independent tests.</summary>
public static class SpectatorRules
{
    public static bool CanReceiveView(MatchPlayer source, MatchPlayer recipient) =>
        source.PeerId != recipient.PeerId && source.Alive && TeamIdentity.IsCombatTeam(source.Team) && !recipient.IsBot &&
        (recipient.Team == PlayerTeam.Spectator || !recipient.Alive && source.Team == recipient.Team);

    public static int? Select(MatchSnapshot? match, int? current, int direction, Func<int, bool> available, PlayerTeam? viewedTeam = null)
    {
        if (match is null) return null;
        var local = Array.Find(match.Players, p => p.PeerId == match.LocalPeerId);
        if (local is null) return null;
        var team = local.Team == PlayerTeam.Spectator ? viewedTeam ?? PlayerTeam.Alpha : local.Team;
        if (!TeamIdentity.IsCombatTeam(team)) return null;
        // Most frames keep the current target; avoid sorting and allocating in that path.
        if (direction == 0 && current is { } selected && Array.Exists(match.Players,
            p => p.PeerId == selected && p.PeerId != local.PeerId && p.Team == team && p.Alive) && available(selected))
            return selected;
        var peers = match.Players.Where(p => p.PeerId != local.PeerId && p.Team == team && p.Alive && available(p.PeerId)).Select(p => p.PeerId).OrderBy(id => id).ToArray();
        if (peers.Length == 0) return null;
        var index = current.HasValue ? Array.IndexOf(peers, current.Value) : -1;
        return index < 0 ? peers[0] : peers[(index + Math.Sign(direction) + peers.Length) % peers.Length];
    }
}

/// <summary>Presentation only. Never used for movement, aim validation, damage or inventory.</summary>
public sealed record SpectatorViewState(int PeerId, int Life, long Sequence,
    float X, float Y, float Z, float Pitch, float Yaw, float Roll, float Fov,
    int Slot, int Magazine, int Reserve, bool Reloading, bool Aiming, bool Sprinting,
    float Spread, float Health, float MaximumHealth, int Armour, float Flash, int Money,
    float HandX, float HandY, float HandZ, float HandPitch, float HandYaw, float HandRoll,
    bool HasHands, long Shots, int Frag, int Flashbang, int Smoke, int Molotov, string SelectedThrowable,
    int Kits = 0, int Plates = 0, bool UsingSupply = false, float UseProgress = 0,
    float OrientationX = 0, float OrientationY = 0, float OrientationZ = 0, float OrientationW = 0, double SampleTime = 0)
{
    [System.Text.Json.Serialization.JsonIgnore] public Vector3 Position => new(X, Y, Z);
    [System.Text.Json.Serialization.JsonIgnore] public Vector3 Rotation => new(Pitch, Yaw, Roll);
    [System.Text.Json.Serialization.JsonIgnore] public Vector3 HandPosition => new(HandX, HandY, HandZ);
    [System.Text.Json.Serialization.JsonIgnore] public Vector3 HandRotation => new(HandPitch, HandYaw, HandRoll);
    [System.Text.Json.Serialization.JsonIgnore] public Quaternion Orientation =>
        OrientationX * OrientationX + OrientationY * OrientationY + OrientationZ * OrientationZ + OrientationW * OrientationW > .000001f
        ? Quaternion.Normalize(new(OrientationX, OrientationY, OrientationZ, OrientationW)) : SpectatorCameraMath.FromEngineEuler(Rotation);
    [System.Text.Json.Serialization.JsonIgnore] public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);
    [System.Text.Json.Serialization.JsonIgnore] public float Heading => MathF.Atan2(-Forward.X, -Forward.Z) * 180 / MathF.PI;
    public bool IsNewerThan(SpectatorViewState previous) => Life > previous.Life || Life == previous.Life && Sequence > previous.Sequence;
    public bool IsValid() => double.IsFinite(SampleTime) && SampleTime >= 0 &&
        float.IsFinite(OrientationX) && float.IsFinite(OrientationY) && float.IsFinite(OrientationZ) && float.IsFinite(OrientationW) &&
        (OrientationX == 0 && OrientationY == 0 && OrientationZ == 0 && OrientationW == 0 ||
            OrientationX * OrientationX + OrientationY * OrientationY + OrientationZ * OrientationZ + OrientationW * OrientationW is > .5f and < 1.5f) && Kits is >= 0 and <= 10000 && Plates is >= 0 and <= 10000 &&
        float.IsFinite(UseProgress) && UseProgress is >= 0 and <= 1 && Sequence > 0 && Life >= 0 && Slot is >= 0 and < 3 &&
        Finite(Position) && Finite(Rotation) && Finite(HandPosition) && Finite(HandRotation) &&
        HandPosition.LengthSquared() <= 100 && Fov is >= 1 and <= 170 &&
        float.IsFinite(Spread) && Spread is >= 0 and <= 45 && float.IsFinite(Health) && Health is > 0 and <= 10000 &&
        float.IsFinite(MaximumHealth) && MaximumHealth is > 0 and <= 10000 && Health <= MaximumHealth &&
        float.IsFinite(Flash) && Flash is >= 0 and <= 1 && Armour is >= 0 and <= 100 &&
        Magazine is >= 0 and <= 10000 && Reserve is >= 0 and <= 100000 && Money is >= 0 and <= 16000 && Shots >= 0 &&
        Frag is >= 0 and <= 4 && Flashbang is >= 0 and <= 4 && Smoke is >= 0 and <= 4 && Molotov is >= 0 and <= 4 &&
        SelectedThrowable is "frag" or "flash" or "smoke" or "molotov";
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

/// <summary>Stable team identity independent of viewer allegiance and attacker/defender roles.</summary>
public static class TeamIdentity
{
    public static bool IsCombatTeam(PlayerTeam team) => team is PlayerTeam.Alpha or PlayerTeam.Bravo;
    public static string Name(PlayerTeam team) => team switch { PlayerTeam.Alpha => "ALPHA", PlayerTeam.Bravo => "BRAVO", _ => "SPECTATORS" };
    public static string CssClass(PlayerTeam team) => team switch { PlayerTeam.Alpha => "alpha-team", PlayerTeam.Bravo => "bravo-team", _ => "observer-team" };
    public static string Badge(PlayerTeam team) => team switch { PlayerTeam.Alpha => "[A]", PlayerTeam.Bravo => "[B]", _ => "[OBS]" };
    public static string EmblemMarkup(PlayerTeam team) => team switch
    {
        PlayerTeam.Alpha => "<span class=\"team-emblem alpha-emblem\"><span>A</span></span>",
        PlayerTeam.Bravo => "<span class=\"team-emblem bravo-emblem\"><span>B</span></span>",
        _ => "<span class=\"observer-emblem\">OBS</span>"
    };
}
