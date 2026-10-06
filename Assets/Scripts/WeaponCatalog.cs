namespace CoD.Scripts;

public enum WeaponType { AssaultRifle, Pistol, MachineGun }

/// <summary>Immutable gameplay tuning shared by local simulation and host validation.</summary>
public sealed record WeaponDefinition(string Id, string Name, WeaponType Type, int MagazineSize,
    bool Automatic, float RoundsPerMinute, float Damage, float Range, float ReloadSeconds,
    float HipSpread, float AimSpread, float RecoilPitch, float RecoilYaw, float DrawSeconds, float ShotPitch)
{
    public float ShotInterval => 60 / RoundsPerMinute;
}

public static class WeaponCatalog
{
    public static readonly WeaponDefinition AssaultRifle = new("ar", "AR-24 ASSAULT RIFLE", WeaponType.AssaultRifle,
        30, true, 660, 28, 180, 2.1f, 1.2f, .18f, .8f, .3f, .45f, 1);
    public static readonly WeaponDefinition Pistol = new("pistol", "P-12 PISTOL", WeaponType.Pistol,
        12, false, 360, 34, 90, 1.5f, 1.5f, .25f, 1.1f, .25f, .3f, 1.25f);
    public static readonly WeaponDefinition MachineGun = new("lmg", "MG-60 MACHINE GUN", WeaponType.MachineGun,
        60, true, 780, 23, 200, 3.4f, 2.1f, .3f, .65f, .6f, .6f, .8f);

    public const int SlotCount = 3;
    public static WeaponDefinition At(int slot) => slot switch
    {
        0 => AssaultRifle, 1 => Pistol, 2 => MachineGun,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };
    public static bool TryFind(string? id, out WeaponDefinition definition)
    {
        definition = id switch { "ar" => AssaultRifle, "pistol" => Pistol, "lmg" => MachineGun, _ => null! };
        return definition is not null;
    }
    public static int DefaultSlot(CombatLoadout loadout) => loadout switch
    {
        CombatLoadout.Medic => 1, CombatLoadout.Defender => 2, _ => 0
    };

    /// <summary>Keep cooldown across switches so alternating slots cannot bypass fire-rate limits.</summary>
    public static float MinimumShotInterval(WeaponDefinition weapon, string? previousWeaponId)
    {
        var interval = weapon.ShotInterval;
        if (previousWeaponId is null || previousWeaponId == weapon.Id) return interval;
        if (TryFind(previousWeaponId, out var previous)) interval = MathF.Max(interval, previous.ShotInterval);
        return MathF.Max(interval, weapon.DrawSeconds);
    }
}

/// <summary>Player-owned per-weapon magazines; reserve ammunition remains a shared inventory supply.</summary>
public sealed class WeaponLoadout
{
    private readonly int[] _magazines = new int[WeaponCatalog.SlotCount];
    public int SelectedSlot { get; private set; }
    public WeaponDefinition Equipped => WeaponCatalog.At(SelectedSlot);
    public int Magazine
    {
        get => _magazines[SelectedSlot];
        set => _magazines[SelectedSlot] = Math.Clamp(value, 0, Equipped.MagazineSize);
    }
    public WeaponLoadout() => Reset(CombatLoadout.Assault);
    public bool Select(int slot)
    {
        if (slot < 0 || slot >= WeaponCatalog.SlotCount || slot == SelectedSlot) return false;
        SelectedSlot = slot;
        return true;
    }
    public int NextSlot => (SelectedSlot + 1) % WeaponCatalog.SlotCount;
    public void Reset(CombatLoadout loadout)
    {
        for (var i = 0; i < _magazines.Length; i++) _magazines[i] = WeaponCatalog.At(i).MagazineSize;
        SelectedSlot = WeaponCatalog.DefaultSlot(loadout);
    }
}
