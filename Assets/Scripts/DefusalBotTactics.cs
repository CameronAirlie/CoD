using System.Numerics;

namespace CoD.Scripts;

public readonly record struct DefusalBotPlan(int Site, int Lane, float ApproachAngle);

/// <summary>Host-owned round plans, stable during a round and varied between rounds.</summary>
public sealed class DefusalBotTactics
{
    private Dictionary<int, DefusalBotPlan> _plans = new();
    public int AttackSite { get; private set; }
    public DefusalBotPlan PlanFor(int peerId) => _plans[peerId];

    public void BeginRound(IEnumerable<(int Id, bool Attacking)> bots, int seed)
    {
        var random = new Random(seed);
        AttackSite = random.Next(2);
        var roster = bots.OrderBy(bot => bot.Id).ToArray();
        // Shuffle coverage assignments so defenders retain balanced sites
        // without permanently assigning the same bot to the same position.
        random.Shuffle(roster);
        int defenderSite = random.Next(2);
        var next = new Dictionary<int, DefusalBotPlan>();
        foreach (var bot in roster)
        {
            int lane = _plans.TryGetValue(bot.Id, out var previous)
                ? (previous.Lane + 1 + random.Next(2)) % 3 : random.Next(3);
            int site = bot.Attacking ? AttackSite : defenderSite++ % 2;
            next.Add(bot.Id, new(site, lane, random.NextSingle() * MathF.Tau));
        }
        _plans = next;
    }

    public static Vector3 CoverOffset(float approachAngle) =>
        new Vector3(MathF.Cos(approachAngle), 0, MathF.Sin(approachAngle)) * 3;
}

/// <summary>A lane waypoint is used once, with a bounded fallback to the objective.</summary>
public sealed class DefusalApproachProgress
{
    private float? _startedAt;
    public bool Complete { get; private set; }

    public Vector3 Destination(Vector3 position, Vector3 objective, Vector3? waypoint, float now, bool urgent)
    {
        if (Complete) return objective;
        _startedAt ??= now;
        if (urgent || waypoint is null || Near(position, objective, 5) ||
            Near(position, waypoint.Value, 2) || now - _startedAt.Value >= 18)
        {
            Complete = true;
            return objective;
        }
        return waypoint.Value;
    }

    private static bool Near(Vector3 a, Vector3 b, float radius)
    {
        var delta = a - b;
        return delta.X * delta.X + delta.Z * delta.Z <= radius * radius;
    }
}
