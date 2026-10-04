using System.Numerics;

namespace CoD.Scripts;

public static class CombatFeedbackMath
{
    public static float Bearing(Vector3 forward, Vector3 right, Vector3 direction) =>
        direction.LengthSquared() < .0001f ? 0 : MathF.Atan2(Vector3.Dot(right, direction),
            Vector3.Dot(forward, direction)) * 180 / MathF.PI;
    public static int RoundExperience(MatchPlayer player, bool won) =>
        50 + Math.Clamp(player.Kills, 0, 10000) * 20 + (int)(float.IsFinite(player.ObjectiveSeconds) ? Math.Clamp(player.ObjectiveSeconds, 0, 600) : 0) * 2 + (won ? 100 : 0);
}
