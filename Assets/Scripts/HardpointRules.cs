using System.Numerics;

namespace CoD.Scripts;

/// <summary>Pure match rules. The host supplies living participant positions; rendering and networking are separate.</summary>
public sealed class HardpointRules
{
    private readonly HardpointSite[] _sites;
    private readonly float _duration;
    private float _scoreFraction;
    public int Index { get; private set; }
    public float Radius { get; }
    public float SecondsRemaining { get; private set; }
    public PlayerTeam? Owner { get; private set; }
    public bool Contested { get; private set; }
    public int AlphaScore { get; private set; }
    public int BravoScore { get; private set; }
    public HardpointSite Site => _sites[Index];

    public HardpointRules(IEnumerable<HardpointSite> sites, float duration = 45, float radius = 5)
    {
        _sites = sites.Where(s => IsFinite(s.Position) && !string.IsNullOrWhiteSpace(s.Name)).ToArray();
        if (_sites.Length == 0) throw new ArgumentException("At least one finite objective site is required.", nameof(sites));
        _duration = float.IsFinite(duration) ? Math.Clamp(duration, 5, 120) : 45;
        Radius = float.IsFinite(radius) ? Math.Clamp(radius, 2, 12) : 5;
        Reset();
    }

    public void Reset()
    {
        Index = AlphaScore = BravoScore = 0;
        SecondsRemaining = _duration;
        Owner = null; Contested = false; _scoreFraction = 0;
    }

    public bool Contains(Vector3 position) => IsFinite(position) &&
        MathF.Abs(position.Y - Site.Position.Y) <= 3 &&
        Vector2.DistanceSquared(new(position.X, position.Z), new(Site.Position.X, Site.Position.Z)) <= Radius * Radius;

    public void Tick(float deltaTime, IReadOnlyList<ObjectiveParticipant> participants)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0) return;
        // Bound pathological caller input without coupling scoring to frame rate.
        var remaining = MathF.Min(deltaTime, 600);
        while (remaining > 0)
        {
            bool alpha = false, bravo = false;
            foreach (var participant in participants)
            {
                if (!participant.Alive || !Contains(participant.Position)) continue;
                if (participant.Team == PlayerTeam.Alpha) alpha = true; else bravo = true;
            }
            PlayerTeam? owner = alpha == bravo ? null : alpha ? PlayerTeam.Alpha : PlayerTeam.Bravo;
            if (Owner != owner) _scoreFraction = 0;
            Owner = owner; Contested = alpha && bravo;
            var step = MathF.Min(remaining, SecondsRemaining);
            if (Owner is not null)
            {
                _scoreFraction += step;
                var points = (int)MathF.Floor(_scoreFraction + .00001f);
                _scoreFraction = MathF.Max(0, _scoreFraction - points);
                if (Owner == PlayerTeam.Alpha) AlphaScore += points; else BravoScore += points;
            }
            SecondsRemaining -= step; remaining -= step;
            if (SecondsRemaining <= .00001f)
            {
                Index = (Index + 1) % _sites.Length;
                SecondsRemaining = _duration; _scoreFraction = 0; Owner = null; Contested = false;
            }
        }
    }

    public HardpointSnapshot Snapshot() => new(Index, Site.Name, Site.Position.X, Site.Position.Y,
        Site.Position.Z, Radius, SecondsRemaining, Owner, Contested);
    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
