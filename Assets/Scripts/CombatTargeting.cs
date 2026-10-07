using System.Numerics;
namespace CoD.Scripts;

/// <summary>Participant stance and aim geometry, independently of model proportions.</summary>
public static class CombatTargeting
{
    public static float StanceHeight(float standingHeight, float radius, float cameraDrop, bool crouching) =>
        crouching ? MathF.Max(radius * 2, standingHeight - MathF.Max(0, cameraDrop)) : standingHeight;
    public static Vector3 AimPoint(Vector3 position, Vector3 colliderCenter, float colliderHeight)
    {
        var height = float.IsFinite(colliderHeight) ? Math.Clamp(colliderHeight, .1f, 10) : 1.5f;
        return position + colliderCenter + Vector3.UnitY * height * .15f;
    }
    public static Vector3 CapturePosition(Vector3 desired, HardpointSnapshot objective)
    {
        var horizontal = desired - objective.Position; horizontal.Y = 0;
        var limit = MathF.Max(0, objective.Radius * .8f);
        if (horizontal.LengthSquared() > limit * limit) horizontal = Vector3.Normalize(horizontal) * limit;
        return new(objective.X + horizontal.X, desired.Y, objective.Z + horizontal.Z);
    }
}
