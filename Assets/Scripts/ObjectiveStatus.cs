using System.Numerics;
namespace CoD.Scripts;

public readonly record struct ObjectiveStatus(string Headline, string Instruction, string Style, bool Inside)
{
    public static ObjectiveStatus Describe(HardpointSnapshot point, PlayerTeam? team, Vector3 position, bool alive)
    {
        var dx = position.X - point.X; var dz = position.Z - point.Z;
        var inside = alive && MathF.Abs(position.Y - point.Y) <= 3 && dx * dx + dz * dz <= point.Radius * point.Radius;
        if (point.Contested) return new("CONTESTED - SCORING PAUSED", "Eliminate enemies inside the zone to score.", "contested", inside);
        if (point.Owner is null) return new("NEUTRAL - CAPTURE THE ZONE", "Stand inside the marked zone. Kills alone do not score.", "neutral", inside);
        if (point.Owner == team) return new("YOUR TEAM CONTROLS THE ZONE", "Defend the zone: your team earns 1 point per second.", "friendly", inside);
        return new("ENEMY CONTROLS THE ZONE", "Enter the zone to stop their score. Clear it to capture.", "enemy", inside);
    }
}
