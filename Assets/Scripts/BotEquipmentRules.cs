using System.Numerics;

namespace CoD.Scripts;

/// <summary>AI intent only. EconomyAccount, ServerEquipment and GrenadeSupply enforce the rules.</summary>
public static class BotEquipmentRules
{
    public static IEnumerable<string> Purchases(EconomyAccount account, GrenadeSupply supply, bool support)
    {
        var budget = account.Money;
        if (!account.Owns(0) && !account.Owns(2))
        {
            var primary = support && account.Money >= 3200 ? "lmg" : "ar";
            var price = EconomyRules.Find(primary)!.Price;
            if (budget >= price) { budget -= price; yield return primary; }
            else budget -= 500;
        }
        // Keep $500 towards the next primary when playing with a sidearm.
        foreach (var kind in support ? new[] { "smoke", "molotov", "flash" } : new[] { "flash", "molotov", "smoke" })
        {
            var item = EconomyRules.Find(kind)!;
            if (supply.Count(kind) == 0 && budget >= item.Price)
            { yield return kind; budget -= item.Price; }
        }
    }
    public static int WeaponSlot(EconomyAccount account, WeaponLoadout weapons)
    {
        var primary = account.Owns(0) ? 0 : account.Owns(2) ? 2 : 1;
        // A dry primary can be exchanged for the sidearm without refilling either magazine.
        if (weapons.MagazineAt(primary) == 0 && weapons.MagazineAt(1) > 0) return 1;
        return primary;
    }
    public static string? Throwable(GrenadeSupply supply, float distance, float healthFraction, bool visible)
    {
        if (!visible || !float.IsFinite(distance) || distance < 10 || distance > 28) return null;
        if (healthFraction < .35f && supply.Count("smoke") > 0) return "smoke";
        if (supply.Count("frag") > 0) return "frag";
        if (supply.Count("molotov") > 0) return "molotov";
        if (distance > 19 && supply.Count("flash") > 0) return "flash";
        return null;
    }
    public static bool Aim(GrenadeDefinition definition, Vector3 origin, Vector3 target,
        out Vector3 direction, out float strength, out float flight)
    {
        direction = Vector3.Zero; strength = 0; flight = definition.Id == "molotov" ? 1.1f : definition.Fuse;
        if (!GrenadeRules.IsFinite(origin) || !GrenadeRules.IsFinite(target)) return false;
        var velocity = (target - origin) / flight + Vector3.UnitY * (.5f * definition.Gravity * flight);
        var a = definition.ThrowSpeed * definition.ThrowSpeed - definition.UpwardSpeed * definition.UpwardSpeed;
        var b = definition.UpwardSpeed * velocity.Y;
        strength = (-b + MathF.Sqrt(b * b + a * velocity.LengthSquared())) / a;
        if (!float.IsFinite(strength) || strength < .2f || strength > 1) return false;
        direction = (velocity - Vector3.UnitY * definition.UpwardSpeed * strength) / (definition.ThrowSpeed * strength);
        return GrenadeRules.IsFinite(direction);
    }
}

public sealed class BotEquipmentState
{
    public ServerEquipment Equipment { get; } = new();
    public float NextBuy, NextThrow;
    public BotEquipmentState() { Equipment.Reset(CombatLoadout.Assault); Equipment.Weapons.Select(1); }
}
