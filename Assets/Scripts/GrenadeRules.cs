using System.Numerics;

namespace CoD.Scripts;

/// <summary>Immutable tuning, independent of transport, rendering and engine physics.</summary>
public sealed record GrenadeDefinition(string Id, float Fuse, float ThrowSpeed, float UpwardSpeed,
    float Gravity, float Restitution, float MaximumDamage, float InnerRadius, float BlastRadius);

public static class GrenadeRules
{
    public static readonly GrenadeDefinition Frag = new("frag", 3, 28, 7, 20, .45f, 120, 2, 7);
    public const int GrenadesPerLife = 2;
    public const float ThrowCooldown = .8f;
    public static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public static float Damage(GrenadeDefinition definition, float distance)
    {
        if (!float.IsFinite(distance) || distance < 0 || distance >= definition.BlastRadius) return 0;
        if (distance <= definition.InnerRadius) return definition.MaximumDamage;
        return definition.MaximumDamage * (definition.BlastRadius - distance) /
            (definition.BlastRadius - definition.InnerRadius);
    }
}

/// <summary>A host-owned budget. Client requests cannot supply ammunition or tuning.</summary>
public sealed class GrenadeSupply
{
    public int Remaining { get; private set; } = GrenadeRules.GrenadesPerLife;
    private float _nextThrow;
    public bool TrySpend(float now)
    {
        if (!float.IsFinite(now) || now < _nextThrow || Remaining <= 0) return false;
        Remaining--; _nextThrow = now + GrenadeRules.ThrowCooldown; return true;
    }
    public void Reset() { Remaining = GrenadeRules.GrenadesPerLife; _nextThrow = 0; }
}

public readonly record struct GrenadeCollision(Vector3 Point, Vector3 Normal);

/// <summary>Fixed-step projectile simulation. The engine adapter supplies swept collision queries.</summary>
public sealed class GrenadeProjectile(GrenadeDefinition definition, Vector3 position, Vector3 direction)
{
    public GrenadeDefinition Definition { get; } = definition;
    public Vector3 Position { get; private set; } = position;
    public Vector3 Velocity { get; private set; } = direction * definition.ThrowSpeed + Vector3.UnitY * definition.UpwardSpeed;
    public float Age { get; private set; }
    public bool Exploded => Age >= Definition.Fuse;
    private double _accumulator;
    private int _steps;

    public void Tick(float deltaTime, Func<Vector3, Vector3, GrenadeCollision?> sweep)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0 || Exploded) return;
        _accumulator += deltaTime;
        const double interval = 1.0 / 120;
        const float step = (float)interval;
        while (_accumulator + 1e-7 >= interval && !Exploded)
        {
            _accumulator -= interval;
            Age = MathF.Min(Definition.Fuse, ++_steps / 120f);
            Velocity -= Vector3.UnitY * Definition.Gravity * step;
            var target = Position + Velocity * step;
            var hit = sweep(Position, target);
            if (hit is { } collision && GrenadeRules.IsFinite(collision.Normal) && collision.Normal.LengthSquared() > .001f)
            {
                var normal = Vector3.Normalize(collision.Normal);
                Position = collision.Point + normal * .085f;
                if (Vector3.Dot(Velocity, normal) < 0)
                    Velocity = Vector3.Reflect(Velocity, normal) * Definition.Restitution;
            }
            else Position = target;
        }
    }
}
