using System;
using System.Numerics;

namespace CoD.Scripts;

/// <summary>Prefers navigation placement without making authored spawns depend on a valid bake.</summary>
internal static class BotSpawnResolver
{
    public static bool TryResolve(Vector3 marker,
        Func<Vector3, Vector3?> project, Func<Vector3, Vector3?> placeAboveGround,
        Func<Vector3, bool> isClear, out Vector3 spawn)
    {
        spawn = default;
        var projected = project(marker);
        if (projected is Vector3 navigable && TryPlace(navigable, placeAboveGround, isClear, out spawn))
            return true;
        return TryPlace(marker, placeAboveGround, isClear, out spawn);
    }

    private static bool TryPlace(Vector3 point, Func<Vector3, Vector3?> placeAboveGround,
        Func<Vector3, bool> isClear, out Vector3 spawn)
    {
        spawn = default;
        if (!isClear(point)) return false;
        var grounded = placeAboveGround(point);
        if (grounded is not Vector3 position ||
            !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            return false;
        spawn = position;
        return true;
    }
}
