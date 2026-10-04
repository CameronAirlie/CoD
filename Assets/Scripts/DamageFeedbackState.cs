namespace CoD.Scripts;

/// <summary>Independent hit flash and persistent low-health feedback.</summary>
public sealed class DamageFeedbackState
{
    public float Pulse { get; private set; }
    public void Hit() => Pulse = 1;
    public void Reset() => Pulse = 0;
    public void Update(float deltaTime)
    {
        if (float.IsFinite(deltaTime) && deltaTime > 0) Pulse = MathF.Max(0, Pulse - deltaTime * 2.5f);
    }
    public float Opacity(float healthFraction, float maximum) =>
        MathF.Max(Pulse, (1 - Math.Clamp(healthFraction, 0, 1)) * .65f) * Math.Clamp(maximum, 0, 1);
}
