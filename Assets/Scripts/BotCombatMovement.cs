using System;

namespace CoD.Scripts;

/// <summary>Bounds firing holds and reserves time to move before holding again.</summary>
internal sealed class BotCombatMovement
{
    private float _holdEndsAt = float.PositiveInfinity;
    private float _repositionEndsAt;
    private float _retreatEndsAt;
    private float _nextRetreatAt;

    public bool WantsRetreat(float time, float healthFraction, bool reloading)
    {
        if (healthFraction <= .3f && time >= _nextRetreatAt)
        {
            _retreatEndsAt = time + 2.5f;
            _nextRetreatAt = time + 8;
        }
        return reloading || time < _retreatEndsAt;
    }

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
        _retreatEndsAt = _nextRetreatAt = 0;
    }
}
