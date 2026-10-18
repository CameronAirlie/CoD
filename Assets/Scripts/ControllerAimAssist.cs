using System.Numerics;

namespace CoD.Scripts;

/// <summary>Subtle ADS friction only: never adds rotation, pulls at targets, or changes shot rays.</summary>
public static class ControllerAimAssist
{
    public const float DefaultStrength = .5f;
    public const float ConeDegrees = 7;
    public const float MaximumRange = 35;
    public const float MaximumSlowdown = .65f;
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

/// <summary>Time-based turn boost; release and reversal reset the ramp.</summary>
public struct ControllerLookAcceleration
{
    private Vector2 _previous;
    private float _held;
    public void Reset() { _previous = Vector2.Zero; _held = 0; }
    public Vector2 Apply(Vector2 stick, float deltaTime)
    {
        if (!float.IsFinite(stick.X) || !float.IsFinite(stick.Y) || !float.IsFinite(deltaTime) || deltaTime <= 0)
        { Reset(); return Vector2.Zero; }
        var magnitude = stick.Length();
        if (magnitude < .05f) { Reset(); return stick; }
        if (magnitude < .65f || Vector2.Dot(stick, _previous) <= 0) _held = 0;
        var before = _held;
        _held = MathF.Min(.6f, _held + deltaTime);
        _previous = stick;
        var deflection = Math.Clamp((magnitude - .65f) / .35f, 0, 1);
        // Integrate the ramp over each frame, including time beyond its cap.
        var area = .5f * (_held * _held - before * before) / .6f + MathF.Max(0, deltaTime - (_held - before));
        return stick * (1 + .75f * deflection * area / deltaTime);
    }
}
