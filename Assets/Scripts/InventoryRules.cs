namespace CoD.Scripts;

public enum CombatLoadout { Assault, Medic, Defender }
public readonly record struct SupplyLoadout(int Ammo, int Kits, int Plates);
public static class InventoryRules
{
    public const int Capacity = 8;
    public static int Slots(int ammo, int kits, int plates) => (Math.Max(0, ammo) + 29) / 30 + Math.Max(0, kits) * 2 + Math.Max(0, plates);
    public static SupplyLoadout Loadout(CombatLoadout loadout) => loadout switch
    {
        CombatLoadout.Medic => new(60, 3, 0),
        CombatLoadout.Defender => new(90, 1, 3),
        _ => new(150, 1, 1)
    };
    public static int AddAmmo(int ammo, int kits, int plates, int requested) =>
        Math.Min(Math.Max(0, requested), Math.Max(0, Math.Min(180, (Capacity - Math.Max(0, kits) * 2 - Math.Max(0, plates)) * 30) - ammo));
    public static int AddItems(int ammo, int kits, int plates, int requested, bool kit) =>
        Math.Min(Math.Max(0, requested), Math.Min(Math.Max(0, 3 - (kit ? kits : plates)),
            Math.Max(0, Capacity - Slots(ammo, kits, plates)) / (kit ? 2 : 1)));
}
