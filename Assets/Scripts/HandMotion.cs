using System;
using System.Numerics;

namespace CoD.Scripts;

/// <summary>Per-weapon local poses. The rig and weapon remain in the same coordinate space.</summary>
public sealed record HandMotionProfile(
    Vector3 Hip, Vector3 Aim, Vector3 Sprint, float Sharpness = 18,
    float Sway = .0018f, float BobFrequency = 9, float BobAmount = .018f);

public readonly record struct HandPose(Vector3 Position, Vector3 RotationOffset);

/// <summary>Engine-independent, bounded procedural motion layered over authored animation.</summary>
public sealed class HandMotion
{
    private Vector3 _position;
    private float _phase;

    public void Reset(HandMotionProfile profile) { _position = profile.Hip; _phase = 0; }

    public HandPose Tick(HandMotionProfile profile, float deltaTime, Vector2 look,
        bool moving, bool aiming, bool sprinting)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0)
            return new HandPose(_position, Vector3.Zero);
        // Clamp input spikes (cursor recapture / a stalled frame), not elapsed time.
        look = new Vector2(float.IsFinite(look.X) ? Math.Clamp(look.X, -100, 100) : 0,
            float.IsFinite(look.Y) ? Math.Clamp(look.Y, -100, 100) : 0);
        if (moving) _phase = (_phase + deltaTime * profile.BobFrequency * (sprinting ? 1.35f : 1)) % MathF.Tau;
        var amount = moving && !aiming ? profile.BobAmount : 0;
        var bob = new Vector3(MathF.Cos(_phase) * amount, MathF.Abs(MathF.Sin(_phase)) * amount, 0);
        var sway = look * profile.Sway;
        var target = (sprinting ? profile.Sprint : aiming ? profile.Aim : profile.Hip)
            + bob + new Vector3(sway.X, -sway.Y, 0);
        _position = Vector3.Lerp(_position, target, 1 - MathF.Exp(-profile.Sharpness * deltaTime));
        return new HandPose(_position, new Vector3(-sway.Y * 20, sway.X * 20, -bob.X * 180));
    }
}
