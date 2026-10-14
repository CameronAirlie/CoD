using System.Numerics;

namespace CoD.Scripts;

/// <summary>Subtle ADS friction only: never adds rotation, pulls at targets, or changes shot rays.</summary>
public static class ControllerAimAssist
{
    public const float DefaultStrength = .5f;
    public const float ConeDegrees = 4;
    public const float MaximumRange = 35;
    public const float MaximumSlowdown = .25f;
    public static bool IsOpponent(PlayerTeam local, PlayerTeam target, bool alive) =>
        alive && TeamIdentity.IsCombatTeam(local) && TeamIdentity.IsCombatTeam(target) && local != target;
    public static float Angle(Vector3 origin, Vector3 forward, Vector3 point)
    {
        var ray = point - origin;
        if (!Finite(ray) || !Finite(forward) || ray.LengthSquared() < .0001f ||
            ray.LengthSquared() > MaximumRange * MaximumRange || forward.LengthSquared() < .0001f)
            return float.PositiveInfinity;
        return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(forward), Vector3.Normalize(ray)), -1, 1)) * 180 / MathF.PI;
    }
    public static Vector2 Apply(Vector2 stick, Vector2 mouse, bool controllerAds, float strength, float angle)
    {
        if (!controllerAds || mouse != Vector2.Zero || stick == Vector2.Zero || !float.IsFinite(strength) ||
            !float.IsFinite(angle) || angle < 0 || angle >= ConeDegrees) return stick;
        var weight = 1 - angle / ConeDegrees;
        weight = weight * weight * (3 - 2 * weight);
        return stick * (1 - MaximumSlowdown * Math.Clamp(strength, 0, 1) * weight);
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
