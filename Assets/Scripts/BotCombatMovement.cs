using System;

namespace CoD.Scripts;

/// <summary>Bounds firing holds and reserves time to move before holding again.</summary>
internal sealed class BotCombatMovement
{
    private float _holdEndsAt = float.PositiveInfinity;
    private float _repositionEndsAt;

    public bool CanHold(float time) => time >= _repositionEndsAt;
    public bool HoldExpired(float time) => time >= _holdEndsAt;
    public void BeginHold(float time, float duration) =>
        _holdEndsAt = time + MathF.Max(0.25f, duration);
    public void BeginReposition(float time, float duration)
    {
        _holdEndsAt = float.PositiveInfinity;
        _repositionEndsAt = time + MathF.Max(0.25f, duration);
    }
    public void Reset()
    {
        _holdEndsAt = float.PositiveInfinity;
        _repositionEndsAt = 0.0f;
    }
}
