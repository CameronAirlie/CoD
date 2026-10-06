using System.Numerics;

namespace CoD.Scripts;

public enum BotRole { Assault, Defender, Flanker }
public static class BotObjectiveTactics
{
    public static BotRole Role(int peerId) => (BotRole)((uint)peerId % 3);
    public static Vector3 Destination(int peerId, HardpointSnapshot objective, PlayerTeam team,
        Vector3 position, bool retreating, float time, float approachAngle = 0)
    {
        var angle = ((uint)peerId % 1000) * 2.399963f + objective.Index * 1.3f + approachAngle;
        var radius = Role(peerId) switch
        {
            BotRole.Defender => objective.Radius * .65f,
            BotRole.Flanker when objective.Owner == team && !objective.Contested => objective.Radius + 3,
            _ => objective.Radius * .25f
        };
        if (Role(peerId) == BotRole.Flanker) angle += MathF.Floor(time / 8) * .7f;
        if (retreating)
        {
            var away = position - objective.Position; away.Y = 0;
            if (away.LengthSquared() < .01f) away = new(MathF.Cos(angle), 0, MathF.Sin(angle));
            return objective.Position + Vector3.Normalize(away) * (objective.Radius + 5);
        }
        return objective.Position + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * radius;
    }
}
