using System.Numerics;
namespace CoD.Scripts;

public enum BombState { Carried, Dropped, Planted, Defused, Exploded }
public readonly record struct DefusalAgent(int Id, PlayerTeam Team, Vector3 Position, bool Alive, bool Interacting);
public sealed record DefusalSnapshot(int Round, PlayerTeam Attackers, BombState Bomb, int? Carrier,
    int Site, float X, float Y, float Z, float Seconds, int? Operator, float Progress,
    PlayerTeam? Winner, string Reason, bool MatchOver = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector3 Position => new(X, Y, Z);
}

/// <summary>Deterministic bomb-round rules. Only the host supplies actors and held interaction input.</summary>
public sealed class DefusalRules
{
    public static PlayerTeam AttackersForRound(int round) => round <= 6 ? PlayerTeam.Alpha : PlayerTeam.Bravo;
    public const float SiteRadius = 4;
    public const float UseRadius = 2.5f;
    public const float PlantSeconds = 3;
    public const float DefuseSeconds = 5;
    private readonly Vector3[] _sites;
    private readonly float _fuse;
    private float _actionTime;
    private int? _operator;
    public int Round { get; }
    public PlayerTeam Attackers { get; }
    public PlayerTeam Defenders => Attackers == PlayerTeam.Alpha ? PlayerTeam.Bravo : PlayerTeam.Alpha;
    public BombState Bomb { get; private set; } = BombState.Carried;
    public int? Carrier { get; private set; }
    public int Site { get; private set; } = -1;
    public Vector3 Position { get; private set; }
    public float Remaining { get; private set; }
    public PlayerTeam? Winner { get; private set; }
    public string Reason { get; private set; } = "";
    public DefusalRules(int round, PlayerTeam attackers, int carrier, Vector3 start, Vector3[] sites, float duration = 90, float fuse = 35)
    {
        if (sites.Length != 2 || sites.Any(p => !Finite(p))) throw new ArgumentException("Two finite bomb sites are required.", nameof(sites));
        Round = round; Attackers = attackers; Carrier = carrier; Position = start; _sites = (Vector3[])sites.Clone();
        Remaining = float.IsFinite(duration) ? Math.Clamp(duration, 10, 300) : 90;
        _fuse = float.IsFinite(fuse) ? Math.Clamp(fuse, 10, 90) : 35;
    }
    public static bool Near(Vector3 a, Vector3 b, float radius) => Finite(a) && Finite(b) &&
        MathF.Abs(a.Y - b.Y) <= 3 && new Vector2(a.X - b.X, a.Z - b.Z).LengthSquared() <= radius * radius;
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public void Tick(float dt, IReadOnlyList<DefusalAgent> agents)
    {
        if (Winner is not null || !float.IsFinite(dt) || dt <= 0) return;
        // Time is split at action/timer boundaries so a frame spanning the plant
        // does not charge the full frame against the newly started bomb fuse.
        var budget = MathF.Min(dt, 600);
        while (budget > .000001f && Winner is null)
        {
            var alive = agents.Where(a => a.Alive && Finite(a.Position)).OrderBy(a => a.Id).ToArray();
            if (!alive.Any(a => a.Team == Defenders)) { End(Attackers, "Defenders eliminated"); break; }
            if (Bomb != BombState.Planted && !alive.Any(a => a.Team == Attackers)) { End(Defenders, "Attackers eliminated"); break; }
            if (Bomb == BombState.Carried)
            {
                var holder = alive.FirstOrDefault(a => a.Id == Carrier);
                if (!alive.Any(a => a.Id == Carrier)) { Bomb = BombState.Dropped; Carrier = null; }
                else Position = holder.Position;
            }
            if (Bomb == BombState.Dropped)
            {
                foreach (var actor in alive)
                    if (actor.Team == Attackers && Near(actor.Position, Position, UseRadius))
                    { Carrier = actor.Id; Position = actor.Position; Bomb = BombState.Carried; break; }
            }
            DefusalAgent? candidate = null;
            int candidateSite = -1;
            foreach (var actor in alive)
            {
                if (!actor.Interacting) continue;
                if (Bomb == BombState.Carried && Carrier == actor.Id)
                {
                    for (var i = 0; i < _sites.Length; i++) if (Near(actor.Position, _sites[i], SiteRadius)) { candidate = actor; candidateSite = i; break; }
                }
                else if (Bomb == BombState.Planted && actor.Team == Defenders && Near(actor.Position, Position, UseRadius)) candidate = actor;
                if (candidate is not null) break;
            }
            var nextOperator = candidate?.Id;
            if (nextOperator != _operator) _actionTime = 0;
            _operator = nextOperator;
            var needed = Bomb == BombState.Planted ? DefuseSeconds : PlantSeconds;
            var step = MathF.Min(budget, Remaining);
            if (candidate is not null) step = MathF.Min(step, needed - _actionTime);
            Remaining = MathF.Max(0, Remaining - step); budget -= step;
            if (candidate is not null) _actionTime += step;
            // The deadline wins a simultaneous completion (no last-frame defuse after detonation).
            if (Remaining <= .000001f)
            {
                if (Bomb == BombState.Planted) { Bomb = BombState.Exploded; End(Attackers, "Bomb detonated"); }
                else End(Defenders, "Time expired");
            }
            else if (candidate is not null && _actionTime >= needed - .000001f)
            {
                if (Bomb == BombState.Planted) { Bomb = BombState.Defused; End(Defenders, "Bomb defused"); }
                else { Bomb = BombState.Planted; Site = candidateSite; Position = candidate.Value.Position; Carrier = null; Remaining = _fuse; }
                _operator = null; _actionTime = 0;
            }
        }
    }
    private void End(PlayerTeam team, string reason) { Winner = team; Reason = reason; _operator = null; _actionTime = 0; }
    public DefusalSnapshot Snapshot(bool matchOver = false) => new(Round, Attackers, Bomb, Carrier, Site,
        Position.X, Position.Y, Position.Z, Remaining, _operator, _actionTime / (Bomb == BombState.Planted ? DefuseSeconds : PlantSeconds), Winner, Reason, matchOver);
}
