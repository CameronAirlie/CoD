using System.Numerics;

namespace CoD.Scripts;

public enum MatchPhase { Waiting, Warmup, Playing, Results }
public enum PlayerTeam { Alpha, Bravo }
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
