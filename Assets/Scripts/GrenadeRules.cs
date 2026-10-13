using System.Numerics;

namespace CoD.Scripts;

/// <summary>Immutable tuning, independent of transport, rendering and engine physics.</summary>
public sealed record GrenadeDefinition(string Id, float Fuse, float ThrowSpeed, float UpwardSpeed,
    float Gravity, float Restitution, float MaximumDamage, float InnerRadius, float BlastRadius);

public static class GrenadeRules
{
    public static readonly GrenadeDefinition Frag = new("frag", 3, 28, 7, 20, .45f, 120, 2, 7);
    public static readonly GrenadeDefinition Flash = Frag with { Id = "flash", Fuse = 1.6f, MaximumDamage = 0 };
    public static readonly GrenadeDefinition Smoke = Frag with { Id = "smoke", Fuse = 2, MaximumDamage = 0 };
    public static readonly GrenadeDefinition Molotov = Frag with { Id = "molotov", Fuse = 2, MaximumDamage = 10, InnerRadius = 2.5f, BlastRadius = 3.5f };
    public static readonly IReadOnlyList<string> Types = Array.AsReadOnly(new[] { "frag", "flash", "smoke", "molotov" });
    public static int IndexOfKind(this IReadOnlyList<string> kinds, string kind)
    { for (var i = 0; i < kinds.Count; i++) if (kinds[i] == kind) return i; return -1; }
    public static GrenadeDefinition? Find(string? id) => id switch { "frag" => Frag, "flash" => Flash, "smoke" => Smoke, "molotov" => Molotov, _ => null };
    public static string Name(string id) => id switch { "flash" => "FLASHBANG", "smoke" => "SMOKE", "molotov" => "MOLOTOV", _ => "FRAG" };
    public static bool IntersectsSmoke(Vector3 start, Vector3 end, Vector3 center, float radius = 4)
    {
        var ray = end - start;
        var t = ray.LengthSquared() < .00001f ? 0 : Math.Clamp(Vector3.Dot(center - start, ray) / ray.LengthSquared(), 0, 1);
        return Vector3.DistanceSquared(start + ray * t, center) <= radius * radius;
    }
    public static float FlashSeconds(float distance, float facing)
        => !float.IsFinite(distance) || !float.IsFinite(facing) || distance < 0 || distance >= 18 ? 0 :
            4 * (1 - distance / 18) * (facing > .3f ? 1 : .25f);
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
    private readonly int[] _counts = [GrenadeRules.GrenadesPerLife, 0, 0, 0];
    public int Remaining => _counts.Sum();
    public int Count(string kind) { var i = Index(kind); return i < 0 ? 0 : _counts[i]; }
    public int[] Snapshot() => (int[])_counts.Clone();
    private static int Index(string kind) => kind switch { "frag" => 0, "flash" => 1, "smoke" => 2, "molotov" => 3, _ => -1 };
    public bool TryAdd(string kind)
    {
        var i = Index(kind);
        if (i < 0 || Remaining >= 4 || _counts[i] >= (kind == "flash" ? 2 : 1)) return false;
        _counts[i]++; return true;
    }
    private float _nextThrow;
    public bool TrySpend(float now) => TrySpend("frag", now);
    public bool TrySpend(string kind, float now)
    {
        var i = Index(kind);
        if (i < 0 || !float.IsFinite(now) || now < _nextThrow || _counts[i] <= 0) return false;
        _counts[i]--; _nextThrow = now + GrenadeRules.ThrowCooldown; return true;
    }
    public void Reset() { Array.Clear(_counts); _counts[0] = GrenadeRules.GrenadesPerLife; _nextThrow = 0; }
}

public readonly record struct GrenadeCollision(Vector3 Point, Vector3 Normal);

/// <summary>Fixed-step projectile simulation. The engine adapter supplies swept collision queries.</summary>
public sealed class GrenadeProjectile(GrenadeDefinition definition, Vector3 position, Vector3 direction)
{
    public GrenadeDefinition Definition { get; } = definition;
    public Vector3 Position { get; private set; } = position;
    public Vector3 Velocity { get; private set; } = direction * definition.ThrowSpeed + Vector3.UnitY * definition.UpwardSpeed;
    public float Age { get; private set; }
    private bool _impact;
    public bool Exploded => _impact || Age >= Definition.Fuse;
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
                if (Definition.Id == "molotov") _impact = true;
                if (Vector3.Dot(Velocity, normal) < 0)
                    Velocity = Vector3.Reflect(Velocity, normal) * Definition.Restitution;
            }
            else Position = target;
        }
    }
}
